using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PKHeX.Core;

namespace EventIndexer;

internal static class Program
{
    // Tradeable wondercard extensions
    private static readonly string[] SupportedExtensions =
    {
        ".wc9", ".wa8", ".wb8", ".wc8", ".wc7full", ".wc7", ".wc6full", ".wc6",
        ".pgf", ".pgt", ".pcd", ".wc5full", ".wc5", ".wc4", ".wk8", ".wr8",
    };

    // Gen -> game code mapping for event files
    private static readonly Dictionary<string, string> ExtToGame = new()
    {
        [".wc9"] = "SV",
        [".wa8"] = "PLA",
        [".wb8"] = "BDSP",
        [".wc8"] = "SWSH",
        [".wk8"] = "SWSH", // Gigantamax
        [".wr8"] = "SWSH", // Raid
        [".wc7full"] = "USUM",
        [".wc7"] = "SM",
        [".wc6full"] = "ORAS",
        [".wc6"] = "XY",
        [".pgf"] = "B2W2",
        [".pgt"] = "DPP",
        [".pcd"] = "DPP",
        [".wc5full"] = "B2W2",
        [".wc5"] = "BW",
        [".wc4"] = "DPP",
    };

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: EventIndexer <input-folder> <output-json>");
            Console.Error.WriteLine("Example: EventIndexer \"C:/Users/ericr/OneDrive/Desktop/PKHeX-ALL-IN-ONE/mgdb/EventsGallery-master/Released\" \"C:/Users/ericr/OneDrive/Desktop/ericscyndaquil/public/api/events-index.json\"");
            return 1;
        }

        var root = args[0];
        var outPath = args[1];

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"Input folder not found: {root}");
            return 2;
        }

        Console.WriteLine($"Scanning: {root}");
        var files = EnumerateEventFiles(root).ToList();
        Console.WriteLine($"Found {files.Count} event files.");

        var entries = new List<EventEntry>();
        int ok = 0, fail = 0, skip = 0;

        foreach (var file in files)
        {
            try
            {
                var entry = TryParseFile(file, root);
                if (entry != null)
                {
                    entries.Add(entry);
                    ok++;
                }
                else
                {
                    skip++;
                }
            }
            catch (Exception ex)
            {
                fail++;
                Console.Error.WriteLine($"FAIL: {Path.GetFileName(file)} — {ex.Message}");
            }

            if ((ok + skip + fail) % 500 == 0)
                Console.WriteLine($"  Progress: {ok + skip + fail}/{files.Count}  (ok={ok} skip={skip} fail={fail})");
        }

        Console.WriteLine($"Done. OK={ok}, Skipped={skip}, Failed={fail}");

        // Sort by gen descending then species ascending (newest gen first)
        entries = entries.OrderByDescending(e => e.Gen).ThenBy(e => e.Species).ThenBy(e => e.EventName).ToList();

        // Write output
        var outDir = Path.GetDirectoryName(outPath)!;
        if (!Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(outPath, json);
        Console.WriteLine($"Wrote {entries.Count} entries to {outPath}");
        Console.WriteLine($"Output size: {new FileInfo(outPath).Length / 1024}KB");

        return 0;
    }

    private static IEnumerable<string> EnumerateEventFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
    }

    private static EventEntry? TryParseFile(string filePath, string rootFolder)
    {
        var fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var relPath = Path.GetRelativePath(rootFolder, filePath).Replace('\\', '/');
        var fileSize = new FileInfo(filePath).Length;

        byte[] data;
        try
        {
            data = File.ReadAllBytes(filePath);
        }
        catch
        {
            return null;
        }

        // Parse as MysteryGift
        DataMysteryGift? mg = null;
        try
        {
            mg = MysteryGift.GetMysteryGift(data, ext) as DataMysteryGift;
        }
        catch
        {
            return null;
        }

        if (mg == null)
            return null;

        // Only index Pokemon gifts, not items
        if (!mg.IsEntity)
            return null;

        // Convert to PKM to extract details
        PKM? pk = null;
        try
        {
            // Use a default trainer for conversion
            ITrainerInfo trainer = new SimpleTrainerInfo(GetGameVersion(ext))
            {
                OT = "Indexer",
                TID16 = 12345,
                SID16 = 54321,
                Language = (byte)LanguageID.English,
            };
            pk = mg.ConvertToPKM(trainer);
        }
        catch
        {
            return null;
        }

        if (pk == null || pk.Species == 0)
            return null;

        // Extract metadata
        var species = pk.Species;
        var speciesName = SpeciesName.GetSpeciesName(species, 2);
        var form = pk.Form;
        var isShiny = pk.IsShiny;
        var level = pk.CurrentLevel;
        var ability = GetAbilityName(pk.Ability);
        var nature = pk.Nature.ToString();
        var ball = GameInfo.GetStrings("en").balllist.ElementAtOrDefault(pk.Ball) ?? "";
        var ot = pk.OriginalTrainerName;
        var tid = pk.TID16;
        var sid = pk.SID16;

        var moves = new[]
        {
            GetMoveName(pk.Move1),
            GetMoveName(pk.Move2),
            GetMoveName(pk.Move3),
            GetMoveName(pk.Move4),
        }.Where(m => !string.IsNullOrEmpty(m)).ToArray();

        // Derive event name from filename (strip number prefix, extension)
        var eventName = DeriveEventName(fileName);

        // Derive year from file timestamp or filename
        int? year = TryExtractYear(fileName);

        // Game and gen
        var game = ExtToGame.GetValueOrDefault(ext, "?");
        var gen = GetGenFromExt(ext);

        return new EventEntry
        {
            Id = $"{game.ToLower()}-{Path.GetFileNameWithoutExtension(fileName).Substring(0, Math.Min(6, Path.GetFileNameWithoutExtension(fileName).Length)).Replace(" ", "-")}",
            Species = species,
            SpeciesName = speciesName,
            Form = form,
            Shiny = isShiny,
            Level = level,
            Game = game,
            Gen = gen,
            EventName = eventName,
            Year = year,
            OT = ot,
            TID = tid,
            SID = sid,
            Nature = nature,
            Ability = ability,
            Ball = ball,
            Moves = moves,
            FileRelPath = relPath,
            FileExt = ext,
            FileSize = fileSize,
        };
    }

    private static string DeriveEventName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        // Strip leading numbers and game code prefix like "1548 SV - "
        var stripped = System.Text.RegularExpressions.Regex.Replace(name, @"^\d+\s*(SV|SWSH|BDSP|PLA|XY|ORAS|SM|USUM|BW|B2W2|DPP|Gen\s*\d+)?\s*-\s*", "");
        return string.IsNullOrWhiteSpace(stripped) ? name : stripped;
    }

    private static int? TryExtractYear(string fileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(19|20)\d{2}");
        if (match.Success && int.TryParse(match.Value, out var year) && year >= 1999 && year <= 2030)
            return year;
        return null;
    }

    private static GameVersion GetGameVersion(string ext) => ext switch
    {
        ".wc9" => GameVersion.SL,
        ".wa8" => GameVersion.PLA,
        ".wb8" => GameVersion.BD,
        ".wc8" => GameVersion.SW,
        ".wk8" => GameVersion.SW,
        ".wr8" => GameVersion.SW,
        ".wc7full" => GameVersion.US,
        ".wc7" => GameVersion.SN,
        ".wc6full" => GameVersion.OR,
        ".wc6" => GameVersion.X,
        ".pgf" => GameVersion.B2,
        ".pgt" => GameVersion.D,
        ".pcd" => GameVersion.D,
        ".wc5full" => GameVersion.B2,
        ".wc5" => GameVersion.B,
        ".wc4" => GameVersion.D,
        _ => GameVersion.Any,
    };

    private static int GetGenFromExt(string ext) => ext switch
    {
        ".wc9" => 9,
        ".wa8" or ".wb8" or ".wc8" or ".wk8" or ".wr8" => 8,
        ".wc7" or ".wc7full" => 7,
        ".wc6" or ".wc6full" => 6,
        ".pgf" or ".wc5" or ".wc5full" => 5,
        ".pgt" or ".pcd" or ".wc4" => 4,
        _ => 0,
    };

    private static string GetMoveName(ushort move)
    {
        try
        {
            var list = GameInfo.GetStrings("en").movelist;
            if (move < list.Length)
                return list[move];
        }
        catch { }
        return "";
    }

    private static string GetAbilityName(int ability)
    {
        try
        {
            var list = GameInfo.GetStrings("en").abilitylist;
            if (ability >= 0 && ability < list.Length)
                return list[ability];
        }
        catch { }
        return "";
    }
}

public sealed class EventEntry
{
    public string Id { get; set; } = "";
    public int Species { get; set; }
    public string SpeciesName { get; set; } = "";
    public byte Form { get; set; }
    public bool Shiny { get; set; }
    public int Level { get; set; }
    public string Game { get; set; } = "";
    public int Gen { get; set; }
    public string EventName { get; set; } = "";
    public int? Year { get; set; }
    public string OT { get; set; } = "";
    public int TID { get; set; }
    public int SID { get; set; }
    public string Nature { get; set; } = "";
    public string Ability { get; set; } = "";
    public string Ball { get; set; } = "";
    public string[] Moves { get; set; } = Array.Empty<string>();
    public string FileRelPath { get; set; } = "";
    public string FileExt { get; set; } = "";
    public long FileSize { get; set; }
}
