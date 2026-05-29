using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SysBot.Pokemon.Helpers;

/// <summary>
/// Loads and converts wondercard (mystery gift) events for mythical Pokemon that ALM cannot legalize.
/// Searches the PKHeX EncounterEvent database (loaded from MGDB) and converts to the target PKM format.
/// </summary>
public static class WondercardLoader
{
    /// <summary>
    /// Species IDs for mythical/event-only Pokemon that commonly fail ALM legalization.
    /// </summary>
    private static readonly HashSet<ushort> MythicalSpecies =
    [
        (ushort)Species.Mew,        // 151
        (ushort)Species.Celebi,     // 251
        (ushort)Species.Jirachi,    // 385
        (ushort)Species.Deoxys,     // 386
        (ushort)Species.Manaphy,    // 490
        (ushort)Species.Darkrai,    // 491
        (ushort)Species.Shaymin,    // 492
        (ushort)Species.Arceus,     // 493
        (ushort)Species.Victini,    // 494
        (ushort)Species.Keldeo,     // 647
        (ushort)Species.Meloetta,   // 648
        (ushort)Species.Genesect,   // 649
        (ushort)Species.Diancie,    // 719
        (ushort)Species.Hoopa,      // 720
        (ushort)Species.Volcanion,  // 721
        (ushort)Species.Magearna,   // 801
        (ushort)Species.Marshadow,  // 802
        (ushort)Species.Zeraora,    // 807
        (ushort)Species.Meltan,     // 808
        (ushort)Species.Melmetal,   // 809
        (ushort)Species.Zarude,     // 893
        (ushort)Species.Pecharunt,  // 1025
    ];

    public static bool IsMythical(ushort species) => MythicalSpecies.Contains(species);

    /// <summary>
    /// Attempts to generate a Pokemon from a matching wondercard event in the MGDB.
    /// </summary>
    public static T? TryGenerateFromEvent<T>(ushort species, byte form, bool shiny, ITrainerInfo sav) where T : PKM, new()
    {
        var speciesName = GameInfo.Strings.Species[species];
        Console.WriteLine($"[WondercardLoader] Searching for {speciesName} (form={form}, shiny={shiny})...");

        var allEvents = EncounterEvent.GetAllEvents().ToArray();
        if (allEvents.Length == 0)
        {
            LogUtil.LogInfo("[WondercardLoader] No events loaded in MGDB. Configure MGDBPath in Legality settings.", "WC");
            return null;
        }

        // Find matching events
        var candidates = allEvents
            .Where(mg => mg.Species == species && mg.Form == form && !mg.IsEgg)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = allEvents
                .Where(mg => mg.Species == species && !mg.IsEgg)
                .ToList();

            if (candidates.Count == 0)
            {
                Console.WriteLine($"[WondercardLoader] No events found for {speciesName}");
                return null;
            }
        }

        Console.WriteLine($"[WondercardLoader] Found {candidates.Count} candidate events for {speciesName}");

        // Sort: shiny match first, same gen, newer gen
        var targetGen = GetTargetGeneration<T>();
        var sorted = candidates
            .OrderByDescending(mg => shiny && mg.IsShiny ? 2 : (!shiny && !mg.IsShiny ? 1 : 0))
            .ThenByDescending(mg => GetMGGeneration(mg) == targetGen ? 1 : 0)
            .ThenByDescending(mg => GetMGGeneration(mg))
            .ToList();

        // Limit to top 10 candidates to avoid excessive processing time
        var top = sorted.Take(10).ToList();
        foreach (var mg in top)
        {
            var result = TryConvertEvent<T>(mg, sav, speciesName);
            if (result != null)
                return result;
        }

        Console.WriteLine($"[WondercardLoader] Top {top.Count} of {sorted.Count} candidates failed for {speciesName}");
        return null;
    }

    /// <summary>
    /// Converts a mystery gift to the target PKM type with comprehensive fixes.
    /// Strategy: Convert → Apply fixes → Iterative repair loop → LegalizePokemon (preserving shiny)
    /// </summary>
    private static T? TryConvertEvent<T>(MysteryGift mg, ITrainerInfo sav, string speciesName) where T : PKM, new()
    {
        try
        {
            var mgGen = GetMGGeneration(mg);
            var mgDesc = $"{mg.OriginalTrainerName} {speciesName} (Gen{mgGen}, {(mg.IsShiny ? "Shiny" : "NotShiny")})";
            Console.WriteLine($"[WondercardLoader] Trying: {mgDesc}");

            var pkm = mg.ConvertToPKM(sav);
            if (pkm == null) return null;

            // Get the PKM in the right format
            T? target = null;

            if (pkm is T directMatch)
            {
                target = directMatch;
            }
            else if (EntityConverter.IsConvertibleToFormat(pkm, sav.Generation))
            {
                var converted = EntityConverter.ConvertToType(pkm, typeof(T), out var convResult);
                if (converted is T convertedPk)
                    target = convertedPk;
            }

            if (target == null)
            {
                Console.WriteLine($"[WondercardLoader] Could not convert {mgDesc} to {typeof(T).Name}");
                return null;
            }

            // ═══════════════════════════════════════════════════════
            // CHECK IF ALREADY VALID BEFORE APPLYING ANY FIXES
            // Some WC9/WC8 events are perfect out of ConvertToPKM — don't corrupt them
            // ═══════════════════════════════════════════════════════
            target.RefreshChecksum();
            var earlyLA = new LegalityAnalysis(target);
            if (earlyLA.Valid)
            {
                Console.WriteLine($"[WondercardLoader] Valid immediately (no fixes needed): {mgDesc}");
                return target;
            }

            // ═══════════════════════════════════════════════════════
            // COMPREHENSIVE FIX PIPELINE (only for invalid conversions)
            // ═══════════════════════════════════════════════════════

            // Step 1: Basic fixes (handler, tracker, TID)
            ApplyBasicFixes(target, sav);

            // Step 2: Clear all memories
            ClearAllMemories(target);

            // Step 3: Iterative repair — parse legality report and fix each issue
            // Run up to 5 iterations since fixes can uncover new issues
            for (int i = 0; i < 5; i++)
            {
                target.RefreshChecksum();
                var la = new LegalityAnalysis(target);
                if (la.Valid)
                {
                    Console.WriteLine($"[WondercardLoader] Valid after iteration {i}: {mgDesc} -> {typeof(T).Name}");
                    return target;
                }

                var report = la.Report(true);
                bool anyFixed = ApplyReportBasedFixes(target, report, sav);
                if (!anyFixed) break; // No more fixable issues
            }

            // Step 4: Final check
            target.RefreshChecksum();
            var finalLA = new LegalityAnalysis(target);
            if (finalLA.Valid)
            {
                Console.WriteLine($"[WondercardLoader] Valid after fixes: {mgDesc} -> {typeof(T).Name}");
                return target;
            }

            // Step 5: Last resort — LegalizePokemon() but ONLY accept if shiny is preserved
            Console.WriteLine($"[WondercardLoader] Trying LegalizePokemon for {mgDesc}: {finalLA.Report(true)}");
            bool wasShiny = target.IsShiny;
            var legalized = target.LegalizePokemon();
            if (legalized is T legalPk)
            {
                var legalLA = new LegalityAnalysis(legalPk);
                if (legalLA.Valid)
                {
                    // Reject if shiny was stripped (defeats the purpose)
                    if (wasShiny && !legalPk.IsShiny)
                    {
                        Console.WriteLine($"[WondercardLoader] LegalizePokemon stripped shiny — rejecting {mgDesc}");
                        return null;
                    }
                    Console.WriteLine($"[WondercardLoader] LegalizePokemon succeeded: {mgDesc}");
                    return legalPk;
                }
            }

            Console.WriteLine($"[WondercardLoader] All fixes failed for {mgDesc}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WondercardLoader] Exception: {speciesName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Basic fixes: HOME tracker, handler, TID/SID, held items.
    /// </summary>
    private static void ApplyBasicFixes<T>(T pkm, ITrainerInfo sav) where T : PKM, new()
    {
        // HOME tracker
        if (pkm is IHomeTrack homeTrack && homeTrack.Tracker == 0)
            homeTrack.Tracker = (ulong)Random.Shared.NextInt64(1, long.MaxValue);

        // Handler
        pkm.CurrentHandler = 1;
        pkm.HandlingTrainerName = sav.OT;
        pkm.HandlingTrainerGender = 0;
        if (pkm is IHandlerLanguage hl)
            hl.HandlingTrainerLanguage = (byte)sav.Language;

        // TID/SID for events that inherit trainer data
        if (pkm.TID16 == 0 && pkm.SID16 == 0)
        {
            pkm.TID16 = sav.TID16;
            pkm.SID16 = sav.SID16;
        }

        // Held items for form-dependent species
        if (pkm.Species == (ushort)Species.Giratina && pkm.Form > 0)
            pkm.HeldItem = 112;
        else if (pkm.Species == (ushort)Species.Silvally && pkm.Form > 0)
            pkm.HeldItem = pkm.Form + 903;

        // Suggested moves if empty
        if (pkm.Move1 == 0)
        {
            pkm.SetSuggestedMoves();
            pkm.HealPP();
        }
    }

    /// <summary>
    /// Parse the legality report and fix every issue we can.
    /// Returns true if at least one fix was applied.
    /// </summary>
    private static bool ApplyReportBasedFixes<T>(T pkm, string report, ITrainerInfo sav) where T : PKM, new()
    {
        bool anyFixed = false;

        // ── HOME Tracker ──
        if (report.Contains("HOME Transfer Tracker") && pkm is IHomeTrack ht && ht.Tracker == 0)
        {
            ht.Tracker = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
            anyFixed = true;
        }

        // ── Memories ──
        if (report.Contains("Memory"))
        {
            ClearAllMemories(pkm);
            anyFixed = true;
        }

        // ── Scale ──
        if (report.Contains("Scale should be") && pkm is PK9 pk9Scale)
        {
            var m = Regex.Match(report, @"Scale should be (\d+)");
            if (m.Success && byte.TryParse(m.Groups[1].Value, out byte s))
            {
                pk9Scale.Scale = s;
                anyFixed = true;
            }
        }

        // ── Height/Weight ──
        if (report.Contains("Height") && report.Contains("should be") && pkm is PK9 pk9H)
        {
            var m = Regex.Match(report, @"Height should be (\d+)");
            if (m.Success && byte.TryParse(m.Groups[1].Value, out byte h))
            {
                pk9H.HeightScalar = h;
                anyFixed = true;
            }
        }
        if (report.Contains("Weight") && report.Contains("should be") && pkm is PK9 pk9W)
        {
            var m = Regex.Match(report, @"Weight should be (\d+)");
            if (m.Success && byte.TryParse(m.Groups[1].Value, out byte w))
            {
                pk9W.WeightScalar = w;
                anyFixed = true;
            }
        }

        // ── Tera Type (PK9) ──
        if (report.Contains("Tera Type") && pkm is PK9 pk9Tera)
        {
            // Brute-force all Tera Type combinations
            for (byte tt = 0; tt <= 18; tt++)
            {
                pk9Tera.TeraTypeOriginal = (MoveType)tt;
                pk9Tera.TeraTypeOverride = (MoveType)19;
                pk9Tera.RefreshChecksum();
                var la = new LegalityAnalysis(pk9Tera);
                if (!la.Report(true).Contains("Tera Type")) { anyFixed = true; break; }
            }
            if (!anyFixed)
            {
                for (byte tt = 0; tt <= 18; tt++)
                {
                    pk9Tera.TeraTypeOriginal = (MoveType)tt;
                    pk9Tera.TeraTypeOverride = (MoveType)tt;
                    pk9Tera.RefreshChecksum();
                    var la = new LegalityAnalysis(pk9Tera);
                    if (!la.Report(true).Contains("Tera Type")) { anyFixed = true; break; }
                }
            }
        }

        // ── Invalid Moves ──
        if (report.Contains("Invalid Move") || report.Contains("Can't learn move"))
        {
            pkm.SetSuggestedMoves();
            pkm.HealPP();
            anyFixed = true;
        }

        // ── Relearn Moves ──
        if (report.Contains("Invalid Relearn Move") || report.Contains("Relearn"))
        {
            // Try setting suggested relearn moves first
            try { pkm.SetRelearnMoves(la: new LegalityAnalysis(pkm)); } catch { }
            // If still failing, clear them
            var checkLA = new LegalityAnalysis(pkm);
            if (checkLA.Report(true).Contains("Relearn"))
            {
                pkm.RelearnMove1 = 0; pkm.RelearnMove2 = 0;
                pkm.RelearnMove3 = 0; pkm.RelearnMove4 = 0;
            }
            anyFixed = true;
        }

        // ── Ribbons ──
        if (report.Contains("Invalid Ribbons") && pkm is IRibbonIndex ribbonPkm)
        {
            foreach (var ri in Enum.GetValues<RibbonIndex>())
            {
                try { if (ribbonPkm.GetRibbonIndex(ri)) ribbonPkm.SetRibbonIndex(ri, false); } catch { }
            }
            anyFixed = true;
        }

        // ── Fateful Encounter ──
        if (report.Contains("Fateful Encounter should not be checked"))
        {
            pkm.FatefulEncounter = false;
            anyFixed = true;
        }
        else if (report.Contains("Fateful Encounter") && !pkm.FatefulEncounter)
        {
            pkm.FatefulEncounter = true;
            anyFixed = true;
        }

        // ── EVs ──
        if (report.Contains("EVs remaining") || report.Contains("EV total") || report.Contains("EVs"))
        {
            pkm.EV_HP = 0; pkm.EV_ATK = 0; pkm.EV_DEF = 0;
            pkm.EV_SPA = 0; pkm.EV_SPD = 0; pkm.EV_SPE = 0;
            anyFixed = true;
        }

        // ── Level ──
        if (report.Contains("Current level is below met level"))
        {
            pkm.CurrentLevel = pkm.MetLevel;
            anyFixed = true;
        }

        // ── Contest Stats ──
        if (report.Contains("Contest stat") && pkm is IContestStats cs)
        {
            cs.ContestCool = 0; cs.ContestBeauty = 0; cs.ContestCute = 0;
            cs.ContestSmart = 0; cs.ContestTough = 0; cs.ContestSheen = 0;
            anyFixed = true;
        }

        // ── Handler ──
        if (report.Contains("Handling Trainer") && !report.Contains("Memory"))
        {
            pkm.CurrentHandler = 1;
            pkm.HandlingTrainerName = sav.OT;
            pkm.HandlingTrainerGender = 0;
            if (pkm is IHandlerLanguage hl)
                hl.HandlingTrainerLanguage = (byte)sav.Language;
            anyFixed = true;
        }

        // ── Friendship ──
        if (report.Contains("Friendship"))
        {
            pkm.OriginalTrainerFriendship = 70;
            pkm.HandlingTrainerFriendship = 0;
            anyFixed = true;
        }

        pkm.RefreshChecksum();
        return anyFixed;
    }

    /// <summary>
    /// Clears ALL memory fields. All OT and HT memory fields set to 0.
    /// </summary>
    private static void ClearAllMemories(PKM pkm)
    {
        switch (pkm)
        {
            case PK8 pk8:
                pk8.OriginalTrainerMemory = 0; pk8.OriginalTrainerMemoryIntensity = 0;
                pk8.OriginalTrainerMemoryFeeling = 0; pk8.OriginalTrainerMemoryVariable = 0;
                pk8.HandlingTrainerMemory = 0; pk8.HandlingTrainerMemoryIntensity = 0;
                pk8.HandlingTrainerMemoryFeeling = 0; pk8.HandlingTrainerMemoryVariable = 0;
                pk8.Enjoyment = 0; pk8.Fullness = 0; pk8.HandlingTrainerFriendship = 0;
                break;
            case PB8 pb8:
                pb8.OriginalTrainerMemory = 0; pb8.OriginalTrainerMemoryIntensity = 0;
                pb8.OriginalTrainerMemoryFeeling = 0; pb8.OriginalTrainerMemoryVariable = 0;
                pb8.HandlingTrainerMemory = 0; pb8.HandlingTrainerMemoryIntensity = 0;
                pb8.HandlingTrainerMemoryFeeling = 0; pb8.HandlingTrainerMemoryVariable = 0;
                pb8.Enjoyment = 0; pb8.Fullness = 0; pb8.HandlingTrainerFriendship = 0;
                break;
            case PK9 pk9:
                pk9.OriginalTrainerMemory = 0; pk9.OriginalTrainerMemoryIntensity = 0;
                pk9.OriginalTrainerMemoryFeeling = 0; pk9.OriginalTrainerMemoryVariable = 0;
                pk9.HandlingTrainerMemory = 0; pk9.HandlingTrainerMemoryIntensity = 0;
                pk9.HandlingTrainerMemoryFeeling = 0; pk9.HandlingTrainerMemoryVariable = 0;
                pk9.HandlingTrainerFriendship = 0;
                break;
            case PA9 pa9:
                pa9.OriginalTrainerMemory = 0; pa9.OriginalTrainerMemoryIntensity = 0;
                pa9.OriginalTrainerMemoryFeeling = 0; pa9.OriginalTrainerMemoryVariable = 0;
                pa9.HandlingTrainerMemory = 0; pa9.HandlingTrainerMemoryIntensity = 0;
                pa9.HandlingTrainerMemoryFeeling = 0; pa9.HandlingTrainerMemoryVariable = 0;
                pa9.HandlingTrainerFriendship = 0;
                break;
        }
    }

    private static byte GetMGGeneration(MysteryGift mg) => mg switch
    {
        WC9 => 9, WA8 => 8, WB8 => 8, WC8 => 8,
        WB7 => 7, WC7 => 7, WC6 => 6, PGF => 5, _ => 4,
    };

    private static byte GetTargetGeneration<T>() where T : PKM, new() => typeof(T).Name switch
    {
        nameof(PB7) => 7, nameof(PK8) => 8, nameof(PB8) => 8,
        nameof(PA8) => 8, nameof(PK9) => 9, nameof(PA9) => 9, _ => 9,
    };

    public static string AutoDetectMGDBPath()
    {
        var knownPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PKHeX-Latest", "mgdb"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PKHeX-Latest", "mgdb", "EventsGallery-master"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PKHeX", "mgdb"),
            Path.Combine(Directory.GetCurrentDirectory(), "mgdb"),
            Path.Combine(Directory.GetCurrentDirectory(), "wc"),
        };

        foreach (var path in knownPaths)
        {
            if (Directory.Exists(path))
            {
                Console.WriteLine($"[WondercardLoader] Auto-detected MGDB path: {path}");
                return path;
            }
        }

        return string.Empty;
    }
}
