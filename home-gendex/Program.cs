using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Pokemon;

// Builds a full National Dex SHINY living dex (#1..max), generating each species in the
// best game where it's legally shiny (SV → SwSh → BDSP → PLA → LGPE). Shiny-locked species
// fall back to non-shiny so they stay legal. Output is sorted into per-game subfolders, ready
// to inject + deposit to HOME from each matching Switch. Files are legal-minus-tracker
// ("no tracker only") — HOME stamps the tracker on at deposit.
//
// args: [outFolder]  [--max=1025]
class GenDex
{
    static readonly (string code, GameVersion ver, byte gen)[] GAMES =
    {
        ("SV",   GameVersion.SL,  9),
        ("SWSH", GameVersion.SW,  8),
        ("ZA",   GameVersion.ZA,  9),
        ("BDSP", GameVersion.BD,  8),
        ("PLA",  GameVersion.PLA, 8),
        ("LGPE", GameVersion.GP,  7),
    };

    static int Main(string[] args)
    {
        string outDir = args.FirstOrDefault(a => !a.StartsWith("--")) ?? @"C:\Users\ericr\OneDrive\Desktop\National-ShinyDex-GENERATED";
        int max = int.TryParse(Arg(args, "--max"), out var m) ? m : 1025;
        Directory.CreateDirectory(outDir);

        // --only=165,166,327,...  → generate just these specific species (gap-filling)
        var onlyArg = Arg(args, "--only");
        var only = string.IsNullOrEmpty(onlyArg) ? null
            : onlyArg.Split(',').Select(x => ushort.TryParse(x.Trim(), out var v) ? v : (ushort)0).Where(v => v > 0).ToHashSet();

        var cfg = new SysBot.Pokemon.LegalitySettings { EnableHOMETrackerCheck = true };
        AutoLegalityWrapper.EnsureInitialized(cfg);
        APILegality.GameVersionPriority = GameVersionPriorityType.NativeOnly; // native-legal in the chosen game
        // Prefer wild/static catches over bred eggs (BDSP eggs produce the invalid-move failures).
        EncounterMovesetGenerator.PriorityList = new List<EncounterTypeGroup>
        { EncounterTypeGroup.Slot, EncounterTypeGroup.Static, EncounterTypeGroup.Trade, EncounterTypeGroup.Mystery, EncounterTypeGroup.Egg };
        // Reset to PKHeX default legality (matches the standalone verifier). Default does not
        // reject a legal-but-untracked mon, but DOES catch real encounter/date errors — so this
        // correctly rejects illegal BDSP shinies instead of masking them.
        ParseSettings.Settings = new PKHeX.Core.LegalitySettings();

        // --allgames: generate each species in EVERY game where it's legal (per-game complete sets),
        // instead of just the first. Populates every game folder.
        bool allGames = args.Contains("--allgames");
        // --game=BDSP restricts generation to a single game (everything legally shiny in THAT game)
        var gameFilter = Arg(args, "--game");
        var games = gameFilter == null ? GAMES
            : GAMES.Where(g => g.code.Equals(gameFilter, StringComparison.OrdinalIgnoreCase)).ToArray();

        var savs = games.ToDictionary(g => g.code, g => (ITrainerInfo)new SimpleTrainerInfo(g.ver)
        { OT = "Trainer", TID16 = 24521, SID16 = 44321, Language = (int)LanguageID.English, Generation = g.gen });
        foreach (var g in games) Directory.CreateDirectory(Path.Combine(outDir, g.code));

        var strings = GameInfo.Strings;
        Console.WriteLine("PKHeX.Core: " + typeof(PKM).Assembly.GetName().Version);
        Console.WriteLine("Building National Dex shiny living dex #1.." + max + " -> " + outDir);
        Console.WriteLine();

        // diagnostic: --diag --only=327 prints per-game outcome for each species
        if (args.Contains("--diag") && only != null)
        {
            foreach (ushort sp2 in only.OrderBy(x => x))
            {
                string nm = strings.Species[sp2];
                Console.WriteLine($"=== #{sp2} {nm} ===");
                foreach (var g in games)
                {
                    foreach (bool sh in new[] { true, false })
                    {
                        try
                        {
                            var pkd = TryGen(savs[g.code], nm, sh); // real generation path (incl. move-fix)
                            if (pkd == null) { Console.WriteLine($"  {g.code,-5} shiny={sh}: NULL"); continue; }
                            var rp = EntityFormat.GetFromBytes(pkd.Data.ToArray());
                            var la = new LegalityAnalysis(rp ?? pkd);
                            var inv = la.Report().Split('\n').Where(l => l.Contains("Invalid")).Take(3);
                            Console.WriteLine($"  {g.code,-5} shiny={sh}: {pkd.GetType().Name} isShiny={pkd.IsShiny} valid={la.Valid}  {string.Join(" | ", inv)}");
                        }
                        catch (Exception ex) { Console.WriteLine($"  {g.code,-5} shiny={sh}: EX {ex.GetType().Name}: {ex.Message}"); }
                    }
                }
            }
            return 0;
        }

        int shiny = 0, nonshiny = 0, skipped = 0;
        var perGame = GAMES.ToDictionary(g => g.code, _ => 0);
        var skippedList = new List<string>();

        for (ushort sp = 1; sp <= max; sp++)
        {
            if (only != null && !only.Contains(sp)) continue;
            string name = strings.Species[sp];
            if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

            // --allgames: write the species into EVERY game where it's legal (shiny where possible)
            if (allGames)
            {
                bool any = false;
                foreach (var g in games)
                {
                    var pk = TryGen(savs[g.code], name, true);
                    bool sh = pk != null && pk.IsShiny && SafeValid(pk);
                    if (!sh)
                    {
                        var ns = TryGen(savs[g.code], name, false);
                        if (ns != null && SafeValid(ns)) pk = ns;
                        else if (!(pk != null && SafeValid(pk))) pk = null;
                    }
                    if (pk != null && SafeValid(pk))
                    {
                        string fn2 = $"{sp:0000}{(pk.IsShiny ? " ★" : "")} - {name} - GEN.{pk.Extension}";
                        File.WriteAllBytes(Path.Combine(outDir, g.code, fn2), pk.Data.ToArray());
                        perGame[g.code]++;
                        if (pk.IsShiny) shiny++; else nonshiny++;
                        any = true;
                    }
                }
                if (!any) { skipped++; skippedList.Add($"{sp:0000} {name}"); }
                if (sp % 25 == 0) Console.WriteLine($"  ...{sp}/{max}  (files: shiny {shiny}, non-shiny {nonshiny}, skipped {skipped})");
                continue;
            }

            PKM chosen = null; string chosenGame = null; bool chosenShiny = false;

            // pass 1: first game where it's legally SHINY
            foreach (var g in games)
            {
                var pk = TryGen(savs[g.code], name, true);
                if (pk != null && pk.IsShiny && SafeValid(pk)) { chosen = pk; chosenGame = g.code; chosenShiny = true; break; }
            }
            // pass 2: shiny-locked everywhere → first game where it's legal non-shiny
            if (chosen == null)
            {
                foreach (var g in games)
                {
                    var pk = TryGen(savs[g.code], name, false);
                    if (pk != null && SafeValid(pk)) { chosen = pk; chosenGame = g.code; chosenShiny = false; break; }
                }
            }

            if (chosen != null)
            {
                string fn = $"{sp:0000}{(chosenShiny ? " ★" : "")} - {name} - GEN.{chosen.Extension}";
                File.WriteAllBytes(Path.Combine(outDir, chosenGame, fn), chosen.Data);
                perGame[chosenGame]++;
                if (chosenShiny) shiny++; else nonshiny++;
            }
            else { skipped++; skippedList.Add($"{sp:0000} {name}"); }

            if (sp % 25 == 0) Console.WriteLine($"  ...{sp}/{max}  (shiny {shiny}, non-shiny {nonshiny}, skipped {skipped})");
        }

        Console.WriteLine();
        Console.WriteLine("================ NATIONAL SHINY DEX DONE ================");
        Console.WriteLine($"Shiny:      {shiny}");
        Console.WriteLine($"Non-shiny:  {nonshiny}  (shiny-locked species)");
        Console.WriteLine($"Skipped:    {skipped}  (no legal encounter in any supported game)");
        Console.WriteLine($"TOTAL made: {shiny + nonshiny} / {max}");
        Console.WriteLine("Per game (deposit each folder from its matching Switch):");
        foreach (var g in GAMES) Console.WriteLine($"  {g.code,-5} {perGame[g.code]}");
        File.WriteAllLines(Path.Combine(outDir, "_skipped.txt"),
            new[] { "Species not produced in any supported game:", "" }.Concat(skippedList));
        return 0;
    }

    static PKM TryGen(ITrainerInfo sav, string name, bool shiny)
    {
        try
        {
            if (!ShowdownParsing.TryParseAnyLanguage(name + (shiny ? "\nShiny: Yes" : ""), out ShowdownSet s)) return null;
            var pk = sav.GetLegalFromSet(new RegenTemplate(s)).Created;
            if (pk == null) return null;
            pk = pk.Clone().LegalizePokemon();
            pk.RefreshChecksum();
            // Move-fix fallback for finicky encounters (e.g. Spinda) where the encounter matches
            // but the auto-assigned moveset is invalid.
            var rp0 = EntityFormat.GetFromBytes(pk.Data.ToArray());
            if (rp0 != null && !new LegalityAnalysis(rp0).Valid)
            {
                try
                {
                    var la2 = new LegalityAnalysis(pk);
                    Span<ushort> cur = stackalloc ushort[4];
                    la2.GetSuggestedCurrentMoves(cur, MoveSourceType.All);
                    pk.SetMoves(cur.ToArray());
                    pk.SetRelearnMoves(la: new LegalityAnalysis(pk));
                    pk.HealPP();
                    pk.RefreshChecksum();
                }
                catch { }
            }
            return pk;
        }
        catch { return null; }
    }
    // Validate the SERIALIZED bytes (re-parsed fresh), exactly like the standalone verifier —
    // this drops the cached encounter that GetLegalFromSet attaches, so we don't accept a mon
    // that only looks legal because of its in-memory encounter cache.
    static bool SafeValid(PKM pk)
    {
        try
        {
            var rp = EntityFormat.GetFromBytes(pk.Data.ToArray());
            if (rp == null) return false;
            return new LegalityAnalysis(rp).Valid;
        }
        catch { return false; }
    }
    static string Arg(string[] a, string k) { var h = a.FirstOrDefault(x => x.StartsWith(k + "=")); return h?.Substring(k.Length + 1); }
}
