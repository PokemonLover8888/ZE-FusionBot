using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PKHeX.Core;
using SysBot.Pokemon;

// Batch-legalizes HOME-Ready files (fixes ribbons / dates / moves / encounters) while
// PRESERVING the existing HOME tracker. Writes fixed copies to a NEW folder — never
// touches originals. Uses the bot's exact AutoLegality init so behavior matches trades.
//
// args:  <sourceFolder>  [--ext=.pb8]  [--limit=N]  [--out=path]
class Legalize
{
    static int Main(string[] args)
    {
        string src = args.FirstOrDefault(a => !a.StartsWith("--"))
                     ?? @"C:\Users\ericr\OneDrive\Desktop\HOME-Ready-Files";
        string ext = Arg(args, "--ext") ?? "";                 // empty = all extensions
        string outDir = Arg(args, "--out") ?? src + "_legalized";
        int limit = int.TryParse(Arg(args, "--limit"), out var l) ? l : int.MaxValue;

        Console.WriteLine("PKHeX.Core: " + typeof(PKM).Assembly.GetName().Version);
        Console.WriteLine("Source: " + src);
        Console.WriteLine("Output: " + outDir);
        Console.WriteLine("Filter: " + (ext == "" ? "(all)" : ext) + "   Limit: " + (limit == int.MaxValue ? "none" : limit.ToString()));
        if (!Directory.Exists(src)) { Console.WriteLine("Source folder not found."); return 1; }
        Directory.CreateDirectory(outDir);

        // Initialize AutoLegality exactly like the bots, but don't require HOME trackers
        // (we preserve the original tracker ourselves).
        var cfg = new SysBot.Pokemon.LegalitySettings { EnableHOMETrackerCheck = false };
        AutoLegalityWrapper.EnsureInitialized(cfg);

        var files = Directory.EnumerateFiles(src)
            .Where(f => ext == "" || string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase))
            .Take(limit).ToList();
        Console.WriteLine("Files: " + files.Count);
        Console.WriteLine();

        int alreadyOk = 0, fixedOk = 0, fixedNoTracker = 0, unfixable = 0, errored = 0, i = 0;
        var stillBad = new List<string>();

        foreach (var f in files)
        {
            i++;
            if (i % 100 == 0) Console.WriteLine($"  ...{i}/{files.Count}  (fixed {fixedOk}, alreadyOk {alreadyOk}, unfixable {unfixable})");
            var name = Path.GetFileName(f);

            try
            {
                var data = File.ReadAllBytes(f);
                var pk = EntityFormat.GetFromBytes(data);
                if (pk == null) { errored++; stillBad.Add("UNREADABLE\t" + name); continue; }

                ulong origTracker = GetTracker(pk);
                bool wasLegal = SafeValid(pk);

                if (wasLegal && origTracker != 0) { File.WriteAllBytes(Path.Combine(outDir, name), data); alreadyOk++; continue; }

                // legalize a clone, then restore the original HOME tracker
                var fixedPk = pk.Clone().LegalizePokemon();
                if (origTracker != 0) SetTracker(fixedPk, origTracker);
                fixedPk.RefreshChecksum();

                bool nowLegal = SafeValid(fixedPk);
                ulong nowTracker = GetTracker(fixedPk);

                if (nowLegal && nowTracker != 0)
                {
                    File.WriteAllBytes(Path.Combine(outDir, name), fixedPk.Data);
                    fixedOk++;
                }
                else if (nowLegal) { fixedNoTracker++; stillBad.Add("LEGAL_BUT_NO_TRACKER\t" + name); }
                else { unfixable++; stillBad.Add("UNFIXABLE\t" + name); }
            }
            catch (Exception ex) { errored++; stillBad.Add("ERROR(" + ex.GetType().Name + ")\t" + name); }
        }

        Console.WriteLine();
        Console.WriteLine("================ LEGALIZE RESULTS ================");
        Console.WriteLine($"Already legal + tracked (copied): {alreadyOk}");
        Console.WriteLine($"REPAIRED to legal + tracked:      {fixedOk}");
        Console.WriteLine($"Legalized but tracker=0 (cannot): {fixedNoTracker}");
        Console.WriteLine($"Unfixable (no legal encounter):   {unfixable}");
        Console.WriteLine($"Errored:                          {errored}");
        Console.WriteLine("--------------------------------------------------");
        int recovered = alreadyOk + fixedOk;
        Console.WriteLine($"TOTAL usable (legal + tracked):   {recovered} / {files.Count}");
        Console.WriteLine("Output folder: " + outDir);

        var rpt = Path.Combine(outDir, "_legalize_failures.txt");
        File.WriteAllLines(rpt, new[] { $"Could not recover {stillBad.Count} files:", "" }.Concat(stillBad.OrderBy(x => x)));
        Console.WriteLine("Failure list: " + rpt);
        return 0;
    }

    static string Arg(string[] a, string key)
    {
        var hit = a.FirstOrDefault(x => x.StartsWith(key + "="));
        return hit?.Substring(key.Length + 1);
    }
    static bool SafeValid(PKM pk) { try { return new LegalityAnalysis(pk).Valid; } catch { return false; } }
    static ulong GetTracker(PKM pk)
    {
        try { var p = pk.GetType().GetProperty("Tracker"); return p != null && p.GetValue(pk) is ulong t ? t : 0UL; }
        catch { return 0UL; }
    }
    static void SetTracker(PKM pk, ulong v)
    {
        try { var p = pk.GetType().GetProperty("Tracker"); if (p != null && p.CanWrite) p.SetValue(pk, v); } catch { }
    }
}
