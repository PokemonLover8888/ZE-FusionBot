using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PKHeX.Core;

// Verifies that every file in the HOME-Ready library is (1) fully legal and
// (2) carries a real HOME tracker. Report-only by default; pass --move to
// quarantine the failures into a dated subfolder.
class Verify
{
    static int Main(string[] args)
    {
        string dir = args.FirstOrDefault(a => !a.StartsWith("--"))
                     ?? @"C:\Users\ericr\OneDrive\Desktop\HOME-Ready-Files";
        bool move = args.Contains("--move");

        // init PKHeX strings/settings for legality reading
        GameInfo.GetStrings("en");

        Console.WriteLine("PKHeX.Core: " + typeof(PKM).Assembly.GetName().Version);
        Console.WriteLine("Folder:     " + dir);
        Console.WriteLine("Mode:       " + (move ? "MOVE failures to quarantine" : "REPORT ONLY (no changes)"));
        if (!Directory.Exists(dir)) { Console.WriteLine("Folder not found."); return 1; }

        var files = Directory.EnumerateFiles(dir).ToList(); // top-level only, skips _quarantine_* subfolders
        Console.WriteLine("Files:      " + files.Count);
        Console.WriteLine();

        bool diag = args.Contains("--diag");
        var samples = new Dictionary<string, List<string>>(); // key: ext|reason -> sample detail blocks

        int ok = 0, illegalOnly = 0, noTrackerOnly = 0, both = 0, unreadable = 0, i = 0;
        var failures = new List<string>();

        foreach (var f in files)
        {
            i++;
            if (i % 500 == 0) Console.WriteLine($"  ...{i}/{files.Count}");
            var name = Path.GetFileName(f);

            byte[] data;
            try { data = File.ReadAllBytes(f); }
            catch { unreadable++; failures.Add("UNREADABLE\t" + name); continue; }

            PKM pk = null;
            try { pk = EntityFormat.GetFromBytes(data); } catch { }
            if (pk == null) { unreadable++; failures.Add("UNREADABLE\t" + name); continue; }

            // HOME tracker (reflection, format-agnostic — matches hrv)
            bool tracked = false;
            try
            {
                var tp = pk.GetType().GetProperty("Tracker");
                tracked = tp != null && tp.GetValue(pk) is ulong tv && tv != 0;
            }
            catch { }

            // Legality
            bool legal = false;
            try { legal = new LegalityAnalysis(pk).Valid; } catch { }

            if (legal && tracked) { ok++; continue; }

            string reason;
            if (!legal && !tracked) { both++; reason = "ILLEGAL+NOTRACKER"; }
            else if (!legal)         { illegalOnly++; reason = "ILLEGAL"; }
            else                     { noTrackerOnly++; reason = "NOTRACKER"; }
            failures.Add(reason + "\t" + name);

            if (diag)
            {
                var key = Path.GetExtension(name) + "|" + reason;
                if (!samples.TryGetValue(key, out var list)) { list = new List<string>(); samples[key] = list; }
                if (list.Count < 3)
                {
                    string trk = "n/a";
                    try { var tp = pk.GetType().GetProperty("Tracker"); trk = tp == null ? "NO Tracker property on " + pk.GetType().Name : Convert.ToString(tp.GetValue(pk)); } catch { }
                    string rep = "";
                    try { rep = new LegalityAnalysis(pk).Report(); } catch (Exception ex) { rep = "report threw: " + ex.Message; }
                    list.Add($"  [{name}]  Tracker={trk}\n    " + rep.Replace("\n", "\n    "));
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("================ RESULTS ================");
        Console.WriteLine($"PASS  (legal + HOME-tracked): {ok}");
        Console.WriteLine($"FAIL  illegal only:           {illegalOnly}");
        Console.WriteLine($"FAIL  no tracker only:        {noTrackerOnly}");
        Console.WriteLine($"FAIL  illegal + no tracker:   {both}");
        Console.WriteLine($"FAIL  unreadable:             {unreadable}");
        Console.WriteLine($"------------------------------------------");
        Console.WriteLine($"TOTAL checked: {files.Count}   PASS: {ok}   FAIL: {failures.Count}");

        if (diag)
        {
            Console.WriteLine();
            Console.WriteLine("================ DIAGNOSTIC SAMPLES ================");
            foreach (var kv in samples.OrderBy(k => k.Key))
            {
                Console.WriteLine("### " + kv.Key + " ###");
                foreach (var block in kv.Value) Console.WriteLine(block);
                Console.WriteLine();
            }
        }

        var report = Path.Combine(dir, "_verify_report.txt");
        var header = new List<string>
        {
            "PKM HOME-Ready verification report",
            "PKHeX.Core " + typeof(PKM).Assembly.GetName().Version,
            "Generated for: " + dir,
            $"Checked {files.Count}  |  Pass {ok}  |  Fail {failures.Count} " +
            $"(illegal {illegalOnly}, no-tracker {noTrackerOnly}, both {both}, unreadable {unreadable})",
            new string('-', 60), ""
        };
        File.WriteAllLines(report, header.Concat(failures.OrderBy(x => x)));
        Console.WriteLine("Report written to: " + report);

        if (move && failures.Count > 0)
        {
            var q = Path.Combine(dir, "_quarantine_verify_" + DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(q);
            int moved = 0;
            foreach (var line in failures)
            {
                var name = line.Substring(line.IndexOf('\t') + 1);
                var src = Path.Combine(dir, name);
                if (File.Exists(src))
                {
                    try { File.Move(src, Path.Combine(q, name)); moved++; } catch { }
                }
            }
            Console.WriteLine($"Moved {moved} failing files to: {q}");
        }
        return 0;
    }
}
