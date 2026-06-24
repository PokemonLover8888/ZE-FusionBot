using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

public static class AutoLegalityWrapper
{
    private static bool Initialized;
    private static LegalitySettings? ConfiguredSettings;

    public static void EnsureInitialized(LegalitySettings cfg)
    {
        if (Initialized)
            return;
        Initialized = true;
        ConfiguredSettings = cfg; // Cache for later use
        InitializeAutoLegality(cfg);
    }

    private static void InitializeAutoLegality(LegalitySettings cfg)
    {
        InitializeCoreStrings();
        // Updated to convert the string to a ReadOnlySpan<string> array as required by the method signature.
        EncounterEvent.RefreshMGDB([cfg.MGDBPath]);
        InitializeTrainerDatabase(cfg);
        InitializeSettings(cfg);
    }

    // The list of encounter types in the priority we prefer if no order is specified.
    private static readonly EncounterTypeGroup[] EncounterPriority = [EncounterTypeGroup.Egg, EncounterTypeGroup.Slot, EncounterTypeGroup.Static, EncounterTypeGroup.Mystery, EncounterTypeGroup.Trade];

    // Used by TryGetAsWildOrEgg to temporarily narrow the encounter search.
    private static readonly SemaphoreSlim s_wildRetryLock = new SemaphoreSlim(1, 1);
    private static readonly List<EncounterTypeGroup> s_wildAndEggPriority = [EncounterTypeGroup.Slot, EncounterTypeGroup.Egg];
    // Used by GetLegalForTradeRaidPriority: raid groups (Mystery/Static) FIRST, full fallback after.
    private static readonly List<EncounterTypeGroup> s_raidPriority = [EncounterTypeGroup.Mystery, EncounterTypeGroup.Static, EncounterTypeGroup.Slot, EncounterTypeGroup.Egg, EncounterTypeGroup.Trade];

    private static void InitializeSettings(LegalitySettings cfg)
    {
        // Disable expensive PID+ validation for PLZA shiny Pokemon
        // PKHeX will skip correlation checks when SearchShiny1 is false (see EncounterGift9a.TryGetSeed)
        LumioseSolver.SearchShiny1 = false;

        APILegality.SetAllLegalRibbons = cfg.SetAllLegalRibbons;
        APILegality.SetMatchingBalls = cfg.SetMatchingBalls;
        APILegality.ForceSpecifiedBall = cfg.ForceSpecifiedBall;
        APILegality.ForceLevel100for50 = cfg.ForceLevel100for50;
        Legalizer.EnableEasterEggs = cfg.EnableEasterEggs;
        APILegality.AllowTrainerOverride = cfg.AllowTrainerDataOverride;
        APILegality.AllowBatchCommands = cfg.AllowBatchCommands;
        APILegality.GameVersionPriority = cfg.GameVersionPriority;
        APILegality.SetBattleVersion = cfg.SetBattleVersion;
        APILegality.Timeout = cfg.Timeout;
        var settings = ParseSettings.Settings;
        settings.WordFilter.CheckWordFilter = false;
        settings.Handler.CheckActiveHandler = false;
        settings.Handler.Restrictions.Disable();
        var validRestriction = new NicknameRestriction { NicknamedTrade = Severity.Fishy, NicknamedMysteryGift = Severity.Fishy };
        settings.Nickname.SetAllTo(validRestriction);

        // As of February 2024, the default setting in PKHeX is Invalid for missing HOME trackers.
        // If the host wants to allow missing HOME trackers, we need to disable the default setting.
        bool allowMissingHOME = !cfg.EnableHOMETrackerCheck;
        if (allowMissingHOME)
            settings.HOMETransfer.Disable();

        // We need all the encounter types present, so add the missing ones at the end.
        var missing = EncounterPriority.Except(cfg.PrioritizeEncounters);
        cfg.PrioritizeEncounters.AddRange(missing);
        cfg.PrioritizeEncounters = [.. cfg.PrioritizeEncounters.Distinct()]; // Don't allow duplicates.
        EncounterMovesetGenerator.PriorityList = cfg.PrioritizeEncounters;
    }

    private static List<GameVersion> SanitizePriorityOrder(List<GameVersion> versionList)
    {
        var validVersions = Enum.GetValues<GameVersion>().Where(GameUtil.IsValidSavedVersion).Reverse().ToList();

        foreach (var ver in validVersions)
        {
            if (!versionList.Contains(ver))
                versionList.Add(ver); // Add any missing versions.
        }

        // Remove any versions in versionList that are not in validVersions and clean up duplicates in the process.
        return [.. versionList.Intersect(validVersions)];
    }

    private static void InitializeTrainerDatabase(LegalitySettings cfg)
    {
        var externalSource = cfg.GeneratePathTrainerInfo;
        if (Directory.Exists(externalSource))
            TrainerSettings.LoadTrainerDatabaseFromPath(externalSource);

        // Seed the Trainer Database with enough fake save files so that we return a generation sensitive format when needed.  
        var fallback = GetDefaultTrainer(cfg);
        for (byte generation = 1; generation <= GameUtil.get_Generation(GameVersion.Gen9); generation++)
        {
            // Convert generation -> GameVersion, then -> EntityContext, since GetVersionsInGeneration expects an EntityContext.
            var gameVersionForGen = GameUtil.GetVersion(generation);
            var context = GameUtil.GetContextFromSaved(gameVersionForGen);
            var versions = GameUtil.GetVersionsInGeneration(context, GameVersion.Any);
            foreach (var version in versions)
                RegisterIfNoneExist(fallback, generation, version);
        }
        // Manually register for LGP/E since Gen7 above will only register the 3DS versions.  
        RegisterIfNoneExist(fallback, 7, GameVersion.GP);
        RegisterIfNoneExist(fallback, 7, GameVersion.GE);
    }

    private static SimpleTrainerInfo GetDefaultTrainer(LegalitySettings cfg)
    {
        var OT = cfg.GenerateOT;
        if (OT.Length == 0)
            OT = "Blank"; // Will fail if actually left blank.
        var fallback = new SimpleTrainerInfo(GameVersion.Any)
        {
            Language = (byte)cfg.GenerateLanguage,
            TID16 = cfg.GenerateTID16,
            SID16 = cfg.GenerateSID16,
            OT = OT,
            Generation = 0,
        };
        return fallback;
    }

    public static SimpleTrainerInfo GetFallbackTrainer()
    {
        if (ConfiguredSettings == null)
            throw new InvalidOperationException("AutoLegalityWrapper has not been initialized. Call EnsureInitialized first.");

        return GetDefaultTrainer(ConfiguredSettings);
    }

    private static void RegisterIfNoneExist(SimpleTrainerInfo fallback, byte generation, GameVersion version)
    {
        fallback = new SimpleTrainerInfo(version)
        {
            Language = fallback.Language,
            TID16 = fallback.TID16,
            SID16 = fallback.SID16,
            OT = fallback.OT,
            Generation = generation,
        };

        // In NET10, ALM has internal defaults that override our configuration
        // We need to register our fallback to ensure it's used instead of the "ALM" defaults
        TrainerSettings.Register(fallback);
    }

    private static void InitializeCoreStrings()
    {
        var lang = Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName[..2];
        LocalizationUtil.SetLocalization(typeof(LegalityCheckResultCode), lang);
        LocalizationUtil.SetLocalization(typeof(MessageStrings), lang);

        // Pre-initialize BattleTemplateLocalization to prevent concurrent dictionary access issues
        // This forces all localizations to be loaded at startup before any concurrent operations
        _ = BattleTemplateLocalization.ForceLoadAll();
    }

    public static bool CanBeTraded(this PKM pkm)
    {
        if (pkm.IsNicknamed && StringsUtil.IsSpammyString(pkm.Nickname))
            return false;
        if (StringsUtil.IsSpammyString(pkm.OriginalTrainerName) && !IsFixedOT(new LegalityAnalysis(pkm).EncounterOriginal, pkm))
            return false;
        return !FormInfo.IsFusedForm(pkm.Species, pkm.Form, pkm.Format);
    }

    public static bool IsFixedOT(IEncounterTemplate t, PKM pkm) => t switch
    {
        IFixedTrainer { IsFixedTrainer: true } => true,
        MysteryGift g => !g.IsEgg && g switch
        {
            WC9 wc9 => wc9.GetHasOT(pkm.Language),
            WA8 wa8 => wa8.GetHasOT(pkm.Language),
            WB8 wb8 => wb8.GetHasOT(pkm.Language),
            WC8 wc8 => wc8.GetHasOT(pkm.Language),
            WB7 wb7 => wb7.GetHasOT(pkm.Language),
            { Generation: >= 5 } gift => gift.OriginalTrainerName.Length > 0,
            _ => true,
        },
        _ => false,
    };

    /// <summary>
    /// Tries to generate the Pokémon using only wild (Slot) and Egg encounters,
    /// bypassing fixed-OT static/gift/trade encounters. Returns null if no valid
    /// non-fixed-OT encounter exists for this species, or if another thread is
    /// already performing a retry (avoids lock contention stalling the queue).
    /// </summary>
    public static PKM? TryGetAsWildOrEgg(ITrainerInfo sav, IBattleTemplate template)
    {
        if (!s_wildRetryLock.Wait(200))
            return null;

        var originalPriority = EncounterMovesetGenerator.PriorityList;
        try
        {
            EncounterMovesetGenerator.PriorityList = s_wildAndEggPriority;
            // Use GetLegalForTrade (not plain GetLegal) so the wild/egg search keeps the bot's
            // NATIVE game priority (ZA/BDSP/PLA). Otherwise the swap could pull a wild encounter
            // from a different game — e.g. a Z-A bot swapping a native Z-A NPC-trade Riolu for a
            // non-native SV wild Riolu (South Province), which then "Cannot enter HOME".
            var pk = sav.GetLegalForTrade(template, out _);
            if (pk == null)
                return null;

            var la = new LegalityAnalysis(pk);
            if (!la.Valid || IsFixedOT(la.EncounterOriginal, pk))
                return null;

            // NEVER swap to a non-native wild encounter. If the only non-fixed-OT alternative is
            // from another game (context mismatch), keep the original native encounter instead —
            // a native fixed-OT mon is HOME-legal; a non-native one is not.
            if (la.EncounterOriginal.Context != pk.Context)
                return null;

            return pk;
        }
        finally
        {
            EncounterMovesetGenerator.PriorityList = originalPriority;
            s_wildRetryLock.Release();
        }
    }

    // Z-A native legendaries that PKHeX's encounter database currently has Z-A entries for.
    // For these, force NativeOnly priority during PA9 generation so ALM doesn't fall back
    // to SwSh Max Lair encounters (legal but produces wrong met location for a Z-A trade).
    private static readonly HashSet<ushort> ZANativeLegendaries = new()
    {
        150, 380, 381, 382, 383, 384, 485, 491, 638, 639, 640, 647, 648, 649,
        670, 716, 717, 718, 719, 720, 721, 801, 802, 807, 808, 809
    };

    private static readonly object _almPriorityLock = new();

    /// <summary>
    /// Wrapper around <see cref="EntityConverterExtensions.GetLegal"/> that forces
    /// <c>NativeOnly</c> encounter priority for Z-A native legendaries when generating PA9.
    /// Without this, ALM falls back to SwSh Max Lair encounters which produce a legal
    /// PKM with the wrong met location (and SW/SH version stamp) for a Z-A trade.
    /// Thread-safe: acquires a lock around the global priority toggle.
    /// </summary>
    public static PKM GetLegalForTrade(this ITrainerInfo sav, IBattleTemplate template, out string res)
    {
        // For Z-A, BDSP, and PLA trades, force PriorityOrder so ALM picks the native game's
        // encounter first instead of falling back to a different game's encounter that
        // converts with the wrong met location (e.g. SwSh Max Lair → PA8, or Movie 15 → PA9).
        bool isZA = sav.Version == GameVersion.ZA;
        bool isBDSP = sav.Version is GameVersion.BDSP or GameVersion.BD or GameVersion.SP;
        bool isPLA = sav.Version == GameVersion.PLA;

        if (!isZA && !isBDSP && !isPLA)
            return sav.GetLegal(template, out res);

        lock (_almPriorityLock)
        {
            var previousType = APILegality.GameVersionPriority;
            var previousOrder = APILegality.PriorityOrder;
            try
            {
            // Z-A FIRST PASS — NativeOnly. Any species catchable in Z-A (shiny OR non-shiny)
            // generates from its real Z-A encounter, so it can NEVER ship "Non-Native / Cannot
            // enter HOME" by leaking an SV/SwSh encounter. Only when Z-A genuinely can't make it
            // (species not in Z-A at all — e.g. a cross-gen event mon) does generation fall
            // through to the cross-game PriorityOrder below. Verified across Froakie/Riolu/
            // Sceptile/Noivern (incl. shiny): NativeOnly yields a valid native Z-A mon.
            if (isZA)
            {
                APILegality.GameVersionPriority = GameVersionPriorityType.NativeOnly;
                var native = sav.GetLegal(template, out res);
                if (native is PA9 np && np.Species == template.Species && new LegalityAnalysis(np).Valid)
                    return native;
            }

            APILegality.GameVersionPriority = GameVersionPriorityType.PriorityOrder;

            if (isZA)
            {
                APILegality.PriorityOrder = new List<GameVersion>
                {
                    GameVersion.ZA,
                    GameVersion.SL, GameVersion.VL, GameVersion.SV,
                    GameVersion.SW, GameVersion.SH, GameVersion.SWSH,
                    GameVersion.PLA, GameVersion.BD, GameVersion.SP, GameVersion.BDSP,
                    GameVersion.GP, GameVersion.GE, GameVersion.GG
                };
            }
            else if (isBDSP)
            {
                // BDSP first so Sinnoh legendaries (Mesprit, Azelf, Uxie, Dialga, Palkia,
                // Giratina, Heatran, Cresselia, Manaphy, Phione, etc.) come from native
                // Lake/Spear Pillar/etc. locations instead of SwSh Max Lair via conversion.
                APILegality.PriorityOrder = new List<GameVersion>
                {
                    GameVersion.BD, GameVersion.SP, GameVersion.BDSP,
                    GameVersion.PLA,
                    GameVersion.SW, GameVersion.SH, GameVersion.SWSH,
                    GameVersion.SL, GameVersion.VL, GameVersion.SV,
                    GameVersion.ZA,
                    GameVersion.GP, GameVersion.GE, GameVersion.GG
                };
            }
            else // PLA
            {
                // PLA first so Hisui-native legendaries/mythicals come from PLA encounters
                // (Mass Outbreak, Daybreak content, Distortion). Falls back to BDSP for
                // Sinnoh natives that aren't in PLA, then SV/SwSh as last resort to avoid
                // SwSh Max Lair leaking through for cross-region species like Landorus.
                APILegality.PriorityOrder = new List<GameVersion>
                {
                    GameVersion.PLA,
                    GameVersion.BD, GameVersion.SP, GameVersion.BDSP,
                    GameVersion.SL, GameVersion.VL, GameVersion.SV,
                    GameVersion.SW, GameVersion.SH, GameVersion.SWSH,
                    GameVersion.ZA,
                    GameVersion.GP, GameVersion.GE, GameVersion.GG
                };
            }

            return sav.GetLegal(template, out res);
            }
            finally
            {
                APILegality.GameVersionPriority = previousType;
                APILegality.PriorityOrder = previousOrder;
            }
        }
    }

    /// <summary>
    /// Generates with raid (Mystery/Static) encounters prioritized FIRST. Needed when a
    /// raid-exclusive mark like the Mightiest Mark ("The Unrivaled") is requested: the host
    /// disables HOME-tracker checks for trading convenience, which makes wild/egg cross-origin
    /// encounters legal without a tracker, so the default priority (Slot/Egg before Mystery)
    /// makes ALM pick a wild/egg encounter and the mark can't legally attach. Forcing
    /// Mystery/Static first makes ALM pick the 7-star raid encounter (EncounterMight9) so the
    /// mark is legal and applied. Falls back to the normal types if no raid encounter exists.
    /// </summary>
    public static PKM GetLegalForTradeRaidPriority(this ITrainerInfo sav, IBattleTemplate template, out string res)
    {
        lock (_almPriorityLock)
        {
            var previousList = EncounterMovesetGenerator.PriorityList;
            try
            {
                EncounterMovesetGenerator.PriorityList = s_raidPriority;
                return sav.GetLegalForTrade(template, out res);
            }
            finally
            {
                EncounterMovesetGenerator.PriorityList = previousList;
            }
        }
    }

    /// <summary>
    /// Generates allowing encounters from ANY game (overrides the host's NativeOnly
    /// GameVersionPriority). Needed for cross-gen-only event mons that have NO native
    /// encounter in the bot's game — e.g. Ash-Greninja (Greninja form 1 / Battle Bond),
    /// which only exists as a Gen-7 Sun/Moon event. With NativeOnly, ALM can't reach that
    /// event and fails; searching all games (newest first) lets it pull the legitimate
    /// event. Intended as a retry only after native generation fails.
    /// </summary>
    public static PKM GetLegalForTradeAnyGame(this ITrainerInfo sav, IBattleTemplate template, out string res)
    {
        lock (_almPriorityLock)
        {
            var previousType = APILegality.GameVersionPriority;
            try
            {
                APILegality.GameVersionPriority = GameVersionPriorityType.NewestFirst;
                return sav.GetLegal(template, out res);
            }
            finally
            {
                APILegality.GameVersionPriority = previousType;
            }
        }
    }

    public static ITrainerInfo GetTrainerInfo<T>() where T : PKM, new()
    {
        ITrainerInfo trainerInfo;

        if (typeof(T) == typeof(PB7))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.GG);
        else if (typeof(T) == typeof(PK8))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.SWSH);
        else if (typeof(T) == typeof(PB8))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.BDSP);
        else if (typeof(T) == typeof(PA8))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.PLA);
        else if (typeof(T) == typeof(PK9))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.SV);
        else if (typeof(T) == typeof(PA9))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.ZA);
        else
            throw new ArgumentException("Type does not have a recognized trainer fetch.", typeof(T).Name);

        // NET10 Fix: Force override ALM's internal defaults with our configured values
        return OverrideALMDefaults(trainerInfo);
    }

    public static ITrainerInfo GetTrainerInfo(byte gen)
    {
        // Convert generation (byte) to a representative GameVersion.
        // GameUtil.GetVersion(byte) is used elsewhere in this file and returns a GameVersion for a generation.
        var version = GameUtil.GetVersion(gen);

        // Fetch saved trainer data using the GameVersion overload (correct type).
        var trainerInfo = TrainerSettings.GetSavedTrainerData(version);

        // NET10 Fix: Force override ALM's internal defaults with our configured values
        return OverrideALMDefaults(trainerInfo);
    }

    /// <summary>
    /// In NET10, ALM has internal "ALM" defaults that override our configuration.
    /// This method intercepts retrieved trainer info and replaces ALM defaults with our configured values.
    /// </summary>
    private static ITrainerInfo OverrideALMDefaults(ITrainerInfo trainerInfo)
    {
        // Check if this is ALM's default trainer info
        if (trainerInfo.OT != "ALM")
            return trainerInfo; // Not ALM defaults, return as-is

        // Ensure we have configured settings cached
        if (ConfiguredSettings == null)
            return trainerInfo; // No configured settings available, return as-is

        // ALM defaults detected - replace with our configured values
        var generation = trainerInfo.Generation;
        var version = trainerInfo.Version;

        var OT = ConfiguredSettings.GenerateOT;
        if (OT.Length == 0)
            OT = "Blank"; // Safety fallback

        // Create a new SimpleTrainerInfo with our configured defaults
        var configuredTrainer = new SimpleTrainerInfo(version)
        {
            Language = (byte)ConfiguredSettings.GenerateLanguage,
            TID16 = ConfiguredSettings.GenerateTID16,
            SID16 = ConfiguredSettings.GenerateSID16,
            OT = OT,
            Generation = generation,
        };

        return configuredTrainer;
    }


    public static PKM GetLegal(this ITrainerInfo sav, IBattleTemplate set, out string res)
    {
        var task = Task.Run(() => sav.GetLegalFromSet(set));
        if (task.Wait(TimeSpan.FromSeconds(30)))
        {
            var result = task.Result;
            res = result.Status switch
            {
                LegalizationResult.Regenerated => "Regenerated",
                LegalizationResult.Failed => "Failed",
                LegalizationResult.Timeout => "Timeout",
                LegalizationResult.VersionMismatch => "VersionMismatch",
                _ => "",
            };

            var pk = result.Created;

            // NET10 Fix: Replace ALM defaults with configured defaults after generation
            if (pk != null && ConfiguredSettings != null)
            {
                // Check if Pokemon has ALM defaults
                if (pk.OriginalTrainerName == "ALM")
                {
                    var OT = ConfiguredSettings.GenerateOT;
                    if (OT.Length == 0)
                        OT = "Blank";

                    // Replace with configured defaults
                    pk.OriginalTrainerName = OT;
                    pk.TrainerTID7 = (uint)((ConfiguredSettings.GenerateSID16 << 16) | ConfiguredSettings.GenerateTID16);
                    pk.Language = (int)ConfiguredSettings.GenerateLanguage;

                    // Recalculate PID for shiny Pokemon to maintain shiny status
                    if (pk.IsShiny)
                    {
                        var shinyXor = pk.ShinyXor;
                        pk.PID = (uint)((pk.TID16 ^ pk.SID16 ^ (pk.PID & 0xFFFF) ^ shinyXor) << 16) | (pk.PID & 0xFFFF);
                    }

                    pk.RefreshChecksum();
                }

                // CRITICAL FIX: Force shiny if requested but not generated
                // ALM in NET10 sometimes fails to generate shiny Pokemon even when requested
                // This affects SV, PLA, BDSP, SWSH, and LGPE - check and force shiny if needed
                if (set.Shiny && !pk.IsShiny)
                {
                    // Pokemon should be shiny but isn't - force it to be shiny
                    // Dynamax Adventures (Max Lair, MetLocation=244) REQUIRE Star shiny (XOR=1).
                    // This applies to native PK8 (SWSH save) AND to PK9 that originated in
                    // SWSH and were transferred to SV via HOME (Version=SW/SH, MetLocation=244).
                    bool isMaxLairOrigin = pk switch
                    {
                        PK8 { MetLocation: 244 } => true,
                        PK9 { MetLocation: 244 } when pk.Version == GameVersion.SW || pk.Version == GameVersion.SH => true,
                        _ => false,
                    };
                    var desiredXor = isMaxLairOrigin
                        ? 1  // Max Lair: always Star shiny (Square is invalid for Dynamax Adventures)
                        : pk is PK8 or PK9 ? 0 : 1;
                    pk.PID = (uint)((pk.TID16 ^ pk.SID16 ^ (pk.PID & 0xFFFF) ^ desiredXor) << 16) | (pk.PID & 0xFFFF);
                    pk.RefreshChecksum();
                }

                // Fix Square shiny at Max Lair: PKHeX requires Star shiny (ShinyXor 1-15) for MetLocation=244.
                // Covers native PK8 (SWSH save) and HOME-transferred PK9 with SWSH Max Lair origin.
                bool isMaxLairSquareShiny = pk.IsShiny && pk.ShinyXor == 0 && pk.MetLocation == 244 &&
                    (pk is PK8 || (pk is PK9 && (pk.Version == GameVersion.SW || pk.Version == GameVersion.SH)));
                if (isMaxLairSquareShiny)
                {
                    pk.PID ^= 0x10000u; // Flip bit 16: ShinyXor 0 → 1 (Square → Star)
                    pk.RefreshChecksum();
                }
            }

            return pk;
        }
        else
        {
            res = "Timeout";
            return null!; // Explicitly return null
        }
    }

    public static string GetLegalizationHint(IBattleTemplate set, ITrainerInfo sav, PKM pk) => set.SetAnalysis(sav, pk);
    public static PKM LegalizePokemon(this PKM pk) => pk.Legalize();
    public static RegenTemplate GetTemplate(ShowdownSet set) => new RegenTemplate(set);
}
