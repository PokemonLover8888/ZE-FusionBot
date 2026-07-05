using System;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Pokemon;

// Confirm a CATEGORY gate (Legendary/SubLegendary/Mythical/Paradox) cleanly excludes every
// SV shiny-locked species that old deps would otherwise mis-validate as a shippable shiny —
// while leaving regular species (Spiritomb etc.) eligible for a native-shiny rebuild.
class CategoryGateProbe
{
    static readonly System.Collections.Generic.HashSet<ushort> Paradox = new()
    { 984,985,986,987,988,989,990,991,992,993,994,995,1005,1006,1009,1010,1020,1021,1022,1023 };

    static void Test(ITrainerInfo sav, ushort species, string name, byte form = 0)
    {
        bool leg = SpeciesCategory.IsLegendary(species);
        bool sub = SpeciesCategory.IsSubLegendary(species);
        bool myth = SpeciesCategory.IsMythical(species);
        bool para = Paradox.Contains(species);
        bool gated = leg || sub || myth || para;

        string set = $"{name}\nLevel: 60\nShiny: Yes";
        var template = AutoLegalityWrapper.GetTemplate(new ShowdownSet(set));
        var direct = sav.GetLegalNativeDirect(template);
        string built;
        if (direct == null) built = "NULL";
        else { var la = new LegalityAnalysis(direct); built = $"shiny={direct.IsShiny} valid={la.Valid}"; }

        string flag = gated ? "GATED " : "eligible";
        string risk = (!gated && direct != null && direct.IsShiny) ? "  <-- ships native shiny" : "";
        Console.WriteLine($"{name,-14}(#{species}) L={leg,-5} SL={sub,-5} M={myth,-5} P={para,-5} => {flag}  native:[{built}]{risk}");
    }

    static void Main()
    {
        Console.WriteLine($"PKHeX.Core = {System.Reflection.Assembly.GetAssembly(typeof(PKM))!.GetName().Version}");
        var cfg = new SysBot.Pokemon.LegalitySettings { EnableHOMETrackerCheck = false };
        AutoLegalityWrapper.EnsureInitialized(cfg);
        ParseSettings.Settings = new PKHeX.Core.LegalitySettings();
        APILegality.GameVersionPriority = GameVersionPriorityType.NativeOnly;
        ITrainerInfo sav = new SimpleTrainerInfo(GameVersion.SL)
        { OT = "Rex", TID16 = 24521, SID16 = 44321, Language = (int)LanguageID.English, Generation = 9 };

        Console.WriteLine("\n-- regular species (want: eligible, ships legit native shiny) --");
        Test(sav, 442, "Spiritomb");
        Test(sav, 246, "Larvitar");
        Test(sav, 443, "Gible");
        Test(sav, 197, "Umbreon");

        Console.WriteLine("\n-- SV shiny-LOCKED (want: GATED, or native NULL) --");
        Test(sav, 1007, "Koraidon");
        Test(sav, 1004, "Chi-Yu");
        Test(sav, 984,  "Great Tusk");
        Test(sav, 1006, "Iron Valiant");
        Test(sav, 1020, "Gouging Fire");
        Test(sav, 1024, "Terapagos");
        Test(sav, 1025, "Pecharunt");
        Test(sav, 1017, "Ogerpon");
        Test(sav, 1014, "Okidogi");
        Test(sav, 1015, "Munkidori");
        Test(sav, 1016, "Fezandipiti");
        Test(sav, 1001, "Wo-Chien");
        Test(sav, 901,  "Ursaluna-Bloodmoon", 1);
    }
}
