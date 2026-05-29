using PKHeX.Core;
using PKHeX.Core.AutoMod;

var folder = @"C:\Users\ericr\OneDrive\Desktop\PKM-Event-Files";
const string newOT = "Quilava156";
const ushort newTID16 = 480008 & 0xFFFF;
const ushort newSID16 = 1339;

// Init MGDB for legality checks
EncounterEvent.RefreshMGDB(ReadOnlySpan<string>.Empty);

int changed = 0, skipped = 0, errors = 0;
var changes = new List<string>();

foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
{
    var ext = Path.GetExtension(file).ToLowerInvariant();
    if (ext is not (".pk9" or ".pk8" or ".pb8" or ".pa8" or ".pa9" or ".pb7" or ".wa9"))
        continue;

    try
    {
        var data = File.ReadAllBytes(file);
        var pk = EntityFormat.GetFromBytes(data);
        if (pk == null) { errors++; continue; }

        var oldOT = pk.OriginalTrainerName;
        if (oldOT == newOT)
        {
            skipped++;
            continue;
        }

        var la = new LegalityAnalysis(pk);

        // Clear trash bytes and set new OT
        pk.OriginalTrainerTrash.Clear();
        pk.OriginalTrainerName = newOT;
        pk.TID16 = newTID16;
        pk.SID16 = newSID16;
        pk.RefreshChecksum();

        File.WriteAllBytes(file, pk.Data);
        changed++;
        changes.Add($"OK: {Path.GetFileName(file)} {oldOT} -> {newOT}");
    }
    catch (Exception ex)
    {
        errors++;
        changes.Add($"ERROR: {Path.GetFileName(file)} - {ex.Message}");
    }
}

Console.WriteLine($"Changed: {changed}");
Console.WriteLine($"Skipped (fixed OT or already correct): {skipped}");
Console.WriteLine($"Errors: {errors}");
Console.WriteLine();
foreach (var c in changes) Console.WriteLine(c);
