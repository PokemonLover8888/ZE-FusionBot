using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PKHeX.Core;

// Generates a verified (species*100 + form) -> PokeAPI home-sprite-id map by asking PKHeX for each
// species+form's English name, building the PokeAPI name, and looking it up in PokeAPI's real list.
class FormMap
{
    static string NormSpecies(string n)
    {
        n = n.ToLowerInvariant().Replace("♀", "-f").Replace("♂", "-m")
             .Replace("'", "").Replace("’", "").Replace(".", "").Replace(":", "").Replace("é", "e");
        n = new string(n.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        n = n.Replace(" ", "-");
        while (n.Contains("--")) n = n.Replace("--", "-");
        return n.Trim('-');
    }

    static string NormForm(string f)
    {
        if (string.IsNullOrWhiteSpace(f)) return "";
        switch (f)
        {
            case "Alolan": return "alola";
            case "Galarian": return "galar";
            case "Hisuian": return "hisui";
            case "Paldean": return "paldea";
        }
        f = f.ToLowerInvariant().Replace(" forme", "").Replace(" form", "").Trim()
             .Replace("'", "").Replace("’", "").Replace(".", "").Replace(":", "").Replace("%", "").Replace(" ", "-");
        while (f.Contains("--")) f = f.Replace("--", "-");
        f = f.Trim('-');
        // PKHeX gender-form names are just "F"/"M"; PokeAPI uses -female/-male
        if (f == "f") return "female";
        if (f == "m") return "male";
        return f;
    }

    // PKHeX form name -> PokeAPI key differs by a suffix/word for these distinct (non-cosmetic) forms.
    static readonly Dictionary<string, string> Remap = new()
    {
        {"tauros-paldea-combat","tauros-paldea-combat-breed"},
        {"tauros-paldea-blaze","tauros-paldea-blaze-breed"},
        {"tauros-paldea-aqua","tauros-paldea-aqua-breed"},
        {"darmanitan-galar","darmanitan-galar-standard"},
        {"basculin-white","basculin-white-striped"},
        {"necrozma-dusk-mane","necrozma-dusk"},
        {"necrozma-dawn-wings","necrozma-dawn"},
        {"eiscue-noice-face","eiscue-noice"},
        {"squawkabilly-blue","squawkabilly-blue-plumage"},
        {"squawkabilly-yellow","squawkabilly-yellow-plumage"},
        {"squawkabilly-white","squawkabilly-white-plumage"},
        {"ogerpon-wellspring","ogerpon-wellspring-mask"},
        {"ogerpon-hearthflame","ogerpon-hearthflame-mask"},
        {"ogerpon-cornerstone","ogerpon-cornerstone-mask"},
        {"maushold-four","maushold-family-of-four"},
    };

    static void Main()
    {
        GameInfo.GetStrings("en");
        var strings = GameInfo.Strings;
        var json = File.ReadAllText(@"C:\PKM-Work\pokeapi-names.json");
        var nameToId = JsonSerializer.Deserialize<Dictionary<string, int>>(json);

        var ctxs = new[] { EntityContext.Gen9, EntityContext.Gen8, EntityContext.Gen8a, EntityContext.Gen8b, EntityContext.Gen7, EntityContext.Gen6 };
        var result = new SortedDictionary<int, int>();
        int matched = 0, missed = 0;
        var misses = new List<string>();

        for (ushort sp = 1; sp <= 1025; sp++)
        {
            string spName = strings.Species[sp];
            if (string.IsNullOrWhiteSpace(spName)) continue;
            string spNorm = NormSpecies(spName);

            for (byte form = 1; form <= 40; form++)
            {
                int foundId = 0; string tried = "";
                foreach (var ctx in ctxs)
                {
                    string fName;
                    try { fName = ShowdownParsing.GetStringFromForm(form, strings, sp, ctx); }
                    catch { continue; }
                    if (string.IsNullOrWhiteSpace(fName)) continue;
                    string fNorm = NormForm(fName);
                    if (fNorm.Length == 0) continue;
                    if (fNorm.Contains("mega") || fNorm.Contains("primal")) continue; // no mega/primal images
                    string key = spNorm + "-" + fNorm;
                    if (Remap.TryGetValue(key, out var mapped)) key = mapped;
                    tried = key;
                    if (nameToId.TryGetValue(key, out int id) && id >= 10000) { foundId = id; break; }
                }
                if (foundId > 0) { result[sp * 100 + form] = foundId; matched++; }
                else if (tried.Length > 0 && tried.Contains('-')) { misses.Add($"{sp} f{form} -> {tried}"); }
            }
        }

        // emit C# dictionary lines (12 per row)
        var sb = new StringBuilder();
        int col = 0;
        foreach (var kv in result)
        {
            sb.Append($"{{{kv.Key},{kv.Value}}},");
            if (++col % 12 == 0) sb.AppendLine();
        }
        File.WriteAllText(@"C:\PKM-Work\formmap.txt", sb.ToString());
        File.WriteAllLines(@"C:\PKM-Work\formmap-misses.txt", misses);
        Console.WriteLine($"Matched {matched} forms -> C:\\PKM-Work\\formmap.txt   (misses {misses.Count} -> formmap-misses.txt)");
        Console.WriteLine("Spot check: Hoopa-Unbound=" + (result.TryGetValue(72002, out var h) ? h : 0)
            + " (also try 72001)=" + (result.TryGetValue(72001, out var h1) ? h1 : 0));
    }
}
