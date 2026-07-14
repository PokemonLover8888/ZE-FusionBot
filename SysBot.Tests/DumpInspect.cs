using PKHeX.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SysBot.Pokemon;
using Xunit;
using Xunit.Abstractions;

namespace SysBot.Tests;

public class DumpInspect
{
    private readonly ITestOutputHelper _out;
    public DumpInspect(ITestOutputHelper output) => _out = output;
    static DumpInspect() => AutoLegalityWrapper.EnsureInitialized(new Pokemon.LegalitySettings());

    private static EntityContext Ctx(string e) => e switch {
        ".pb8" => EntityContext.Gen8b, ".pk8" => EntityContext.Gen8, ".pk9" => EntityContext.Gen9,
        ".pa9" => EntityContext.Gen9a, ".pa8" => EntityContext.Gen8a, ".pb7" => EntityContext.Gen7b,
        _ => EntityContext.None };

    [Fact]
    public void EmitProtectedFileList()
    {
        var archiveOnly = File.ReadAllLines(@"C:\Users\ericr\AppData\Local\Temp\claude\C--Users-ericr\1228ff77-5588-4fe8-acda-5a5d237988c8\scratchpad\archive-only.txt")
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet();

        var protectedFiles = new SortedDictionary<string, string>();  // filename -> species label

        foreach (var f in Directory.GetFiles(@"C:\Users\ericr\OneDrive\Desktop\PKM-Archives", "*", SearchOption.AllDirectories))
        {
            var ctx = Ctx(Path.GetExtension(f).ToLowerInvariant());
            if (ctx == EntityContext.None) continue;
            try
            {
                var pk = EntityFormat.GetFromBytes(File.ReadAllBytes(f), ctx);
                if (pk is null || pk.Species == 0) continue;
                var head = ShowdownParsing.GetShowdownText(pk).Split('\n')[0];
                var at = head.IndexOf(" @ ", System.StringComparison.Ordinal);
                if (at >= 0) head = head[..at];
                head = head.Trim();
                if (archiveOnly.Contains(head))
                    protectedFiles[Path.GetFileName(f)] = head;
            }
            catch { }
        }

        var json = "{\n" + string.Join(",\n", protectedFiles.Select(kv =>
            $"  {System.Text.Json.JsonSerializer.Serialize(kv.Key)}: {System.Text.Json.JsonSerializer.Serialize(kv.Value)}")) + "\n}\n";

        var dest = @"C:\Users\ericr\OneDrive\Desktop\pkm-archives-bot\protected-files.json";
        File.WriteAllText(dest, json);
        _out.WriteLine($"Wrote {protectedFiles.Count} protected filenames to {dest}");
    }
}
