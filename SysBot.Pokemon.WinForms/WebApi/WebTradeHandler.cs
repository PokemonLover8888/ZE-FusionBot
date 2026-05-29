using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon;

namespace SysBot.Pokemon.WinForms;

public class WebTradeRequest
{
    public string ShowdownSet { get; set; } = "";
    public string? Username { get; set; }
    public int TradeCode { get; set; }

    /// <summary>
    /// Optional — absolute path to an event wondercard file (.wc9/.wa8/etc).
    /// If set, the bot will trade the exact event file instead of generating from ShowdownSet.
    /// </summary>
    public string? EventFilePath { get; set; }

    /// <summary>
    /// If true, force the output Pokemon to be shiny after conversion.
    /// Used for raid wondercards where ConvertToPKM randomizes the shiny roll.
    /// </summary>
    public bool ForceShiny { get; set; }
}

public class WebTradeStatus
{
    public string TradeId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public int LinkCode { get; set; }
    public string UserName { get; set; } = "";
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

public static class WebTradeHandler
{
    private static readonly ConcurrentDictionary<string, WebTradeStatus> ActiveTrades = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> RateLimits = new();
    private const int MaxRequestsPerMinute = 10;
    private static PokeTradeHub<PK9>? _hubPK9;
    private static PokeTradeHub<PB8>? _hubPB8;
    private static PokeTradeHub<PK8>? _hubPK8;
    private static PokeTradeHub<PA8>? _hubPA8;
    private static PokeTradeHub<PB7>? _hubPB7;
    private static PokeTradeHub<PA9>? _hubPA9;

    public static void RegisterHub<T>(PokeTradeHub<T> hub) where T : PKM, new()
    {
        switch (hub)
        {
            case PokeTradeHub<PK9> h9: _hubPK9 = h9; break;
            case PokeTradeHub<PB8> hB8: _hubPB8 = hB8; break;
            case PokeTradeHub<PK8> h8: _hubPK8 = h8; break;
            case PokeTradeHub<PA8> hA8: _hubPA8 = hA8; break;
            case PokeTradeHub<PB7> hB7: _hubPB7 = hB7; break;
            case PokeTradeHub<PA9> hA9: _hubPA9 = hA9; break;
        }
    }

    public static string ToJson(object obj) => JsonSerializer.Serialize(obj);

    // Maps species → (form → required item name). Looks up actual item ID from PKHeX strings
    // at runtime so we pick the right item for the target game's format.
    private static readonly Dictionary<ushort, Dictionary<byte, string>> FormRequiredItems = new()
    {
        // Giratina Origin: Griseous Orb (Gen 4-8) or Griseous Core (Gen 9)
        [487] = new() { [1] = "Griseous Core" }, // Gen 9 uses Core; helper tries Orb as fallback
        // Arceus plates: form 1-17
        [493] = new()
        {
            [1] = "Fist Plate", [2] = "Sky Plate", [3] = "Toxic Plate", [4] = "Earth Plate",
            [5] = "Stone Plate", [6] = "Insect Plate", [7] = "Spooky Plate", [8] = "Iron Plate",
            [9] = "Flame Plate", [10] = "Splash Plate", [11] = "Meadow Plate", [12] = "Zap Plate",
            [13] = "Mind Plate", [14] = "Icicle Plate", [15] = "Draco Plate", [16] = "Dread Plate",
            [17] = "Pixie Plate",
        },
        // Silvally memories: form 1-17
        [773] = new()
        {
            [1] = "Fighting Memory", [2] = "Flying Memory", [3] = "Poison Memory", [4] = "Ground Memory",
            [5] = "Rock Memory", [6] = "Bug Memory", [7] = "Ghost Memory", [8] = "Steel Memory",
            [9] = "Fire Memory", [10] = "Water Memory", [11] = "Grass Memory", [12] = "Electric Memory",
            [13] = "Psychic Memory", [14] = "Ice Memory", [15] = "Dragon Memory", [16] = "Dark Memory",
            [17] = "Fairy Memory",
        },
        // Zacian/Zamazenta Crowned: Rusted Sword/Shield
        [888] = new() { [1] = "Rusted Sword" },
        [889] = new() { [1] = "Rusted Shield" },
        // Genesect Drives (form 1-4)
        [649] = new() { [1] = "Douse Drive", [2] = "Shock Drive", [3] = "Burn Drive", [4] = "Chill Drive" },
        // Ogerpon Masks (form 1-3) — SV only
        [1017] = new() { [1] = "Wellspring Mask", [2] = "Hearthflame Mask", [3] = "Cornerstone Mask" },
    };

    private static int LookupItemIdByName(string itemName)
    {
        try
        {
            var items = GameInfo.GetStrings("en").itemlist;
            for (int i = 0; i < items.Length; i++)
                if (items[i].Equals(itemName, StringComparison.OrdinalIgnoreCase))
                    return i;
        }
        catch { }
        return -1;
    }

    private static void TryAttachFormItem(PKM pkm, int species, int form, Type targetType)
    {
        if (!FormRequiredItems.TryGetValue((ushort)species, out var formMap)) return;
        if (!formMap.TryGetValue((byte)form, out var primaryName)) return;

        // Giratina Origin special case — try "Griseous Core" first (Gen 9), fall back to "Griseous Orb"
        var candidates = species == 487 ? new[] { "Griseous Core", "Griseous Orb" } : new[] { primaryName };
        foreach (var name in candidates)
        {
            var id = LookupItemIdByName(name);
            if (id > 0)
            {
                pkm.HeldItem = id;
                LogUtil.LogInfo($"[WebTrade] Auto-attached {name} (id={id}) for species {species} form {form} ({targetType.Name})", "WebTrade");
                return;
            }
        }
        LogUtil.LogInfo($"[WebTrade] Could not find item for species {species} form {form} in {targetType.Name}", "WebTrade");
    }

    public static object SubmitTrade(string showdownSet, string username, int tradeCode, bool forceShiny = false)
    {
        // Set default username if empty
        if (string.IsNullOrWhiteSpace(username))
            username = "WebTrader";

        // Rate limiting — max 3 requests per minute per user
        var now = DateTime.Now;
        var rateKey = username.ToLowerInvariant();
        if (RateLimits.TryGetValue(rateKey, out var limit))
        {
            if ((now - limit.Window).TotalMinutes < 1)
            {
                if (limit.Count >= MaxRequestsPerMinute)
                {
                    LogUtil.LogInfo($"[WebTrade] Rate limited user: {username} ({limit.Count} requests)", "WebTrade");
                    return new { success = false, error = "You are sending requests too fast. You have been temporarily rate limited. If you believe this is an error, please contact the owner @Quilava156 on Discord." };
                }
                RateLimits[rateKey] = (limit.Count + 1, limit.Window);
            }
            else
            {
                RateLimits[rateKey] = (1, now);
            }
        }
        else
        {
            RateLimits[rateKey] = (1, now);
        }

        try
        {
            // Try each hub type in order
            if (_hubPA9 != null) return SubmitTradeInternal(_hubPA9, showdownSet, username, tradeCode, forceShiny);
            if (_hubPK9 != null) return SubmitTradeInternal(_hubPK9, showdownSet, username, tradeCode, forceShiny);
            if (_hubPK8 != null) return SubmitTradeInternal(_hubPK8, showdownSet, username, tradeCode, forceShiny);
            if (_hubPB8 != null) return SubmitTradeInternal(_hubPB8, showdownSet, username, tradeCode, forceShiny);
            if (_hubPA8 != null) return SubmitTradeInternal(_hubPA8, showdownSet, username, tradeCode, forceShiny);
            if (_hubPB7 != null) return SubmitTradeInternal(_hubPB7, showdownSet, username, tradeCode, forceShiny);

            return new { success = false, error = "No trade hub available" };
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"WebTradeHandler.SubmitTrade error: {ex.Message}", "WebTrade");
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// Trades an actual event wondercard file (.wc9, .wa8, .wb8, .wc8, .pgf, etc).
    /// This bypasses ALM entirely — the Pokemon is loaded directly from the legit event file,
    /// so shiny-locked species like Chi-Yu can be traded with valid PID/nature/encounter data.
    /// </summary>
    public static object SubmitTradeFromEventFile(string eventFilePath, string username, int tradeCode, bool forceShiny = false)
    {
        if (string.IsNullOrWhiteSpace(username))
            username = "WebTrader";

        // Rate limit (shared with the showdown-set path)
        var now = DateTime.Now;
        var rateKey = username.ToLowerInvariant();
        if (RateLimits.TryGetValue(rateKey, out var limit))
        {
            if ((now - limit.Window).TotalMinutes < 1)
            {
                if (limit.Count >= MaxRequestsPerMinute)
                {
                    LogUtil.LogInfo($"[WebTrade] Rate limited user: {username} ({limit.Count} requests)", "WebTrade");
                    return new { success = false, error = "You are sending requests too fast. You have been temporarily rate limited. If you believe this is an error, please contact the owner @Quilava156 on Discord." };
                }
                RateLimits[rateKey] = (limit.Count + 1, limit.Window);
            }
            else
            {
                RateLimits[rateKey] = (1, now);
            }
        }
        else
        {
            RateLimits[rateKey] = (1, now);
        }

        try
        {
            if (!File.Exists(eventFilePath))
                return new { success = false, error = "Event file not found on the server." };

            byte[] data;
            try { data = File.ReadAllBytes(eventFilePath); }
            catch (Exception ex) { return new { success = false, error = $"Failed to read event file: {ex.Message}" }; }

            var ext = Path.GetExtension(eventFilePath).ToLowerInvariant();
            DataMysteryGift? mg;
            try { mg = MysteryGift.GetMysteryGift(data, ext) as DataMysteryGift; }
            catch (Exception ex) { return new { success = false, error = $"Failed to parse event file: {ex.Message}" }; }

            if (mg == null)
                return new { success = false, error = "Event file is not a valid Mystery Gift." };
            if (!mg.IsEntity)
                return new { success = false, error = "Event file is an item, not a Pokemon." };

            // Dispatch to the correct hub for the event file's game
            // .wc9 => PK9 (SV), .wa8 => PA8 (PLA), .wb8 => PB8 (BDSP), .wc8 => PK8 (SWSH)
            // Gen 7 and older need to be converted up to whatever hub we have available

            switch (ext)
            {
                case ".wc9":
                    if (_hubPK9 != null) return SubmitEventTrade(_hubPK9, mg, username, tradeCode, eventFilePath, forceShiny);
                    break;
                case ".wa8":
                    if (_hubPA8 != null) return SubmitEventTrade(_hubPA8, mg, username, tradeCode, eventFilePath, forceShiny);
                    break;
                case ".wb8":
                    if (_hubPB8 != null) return SubmitEventTrade(_hubPB8, mg, username, tradeCode, eventFilePath, forceShiny);
                    break;
                case ".wc8":
                case ".wk8":
                case ".wr8":
                    if (_hubPK8 != null) return SubmitEventTrade(_hubPK8, mg, username, tradeCode, eventFilePath, forceShiny);
                    break;
                default:
                    // Older gens — try to transfer into whatever hub is loaded
                    // Prefer SWSH/SV > BDSP > PLA for older Pokemon
                    if (_hubPK8 != null) return SubmitEventTrade(_hubPK8, mg, username, tradeCode, eventFilePath, forceShiny);
                    if (_hubPK9 != null) return SubmitEventTrade(_hubPK9, mg, username, tradeCode, eventFilePath, forceShiny);
                    break;
            }

            return new { success = false, error = $"No trade bot loaded for event file type '{ext}'." };
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"WebTradeHandler.SubmitTradeFromEventFile error: {ex.Message}", "WebTrade");
            return new { success = false, error = ex.Message };
        }
    }

    private static object SubmitEventTrade<T>(PokeTradeHub<T> hub, DataMysteryGift mg, string username, int tradeCode, string filePath, bool forceShiny = false) where T : PKM, new()
    {
        try
        {
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();

            // Convert the mystery gift into a PKM using our trainer info
            PKM pkm;
            try { pkm = mg.ConvertToPKM(sav); }
            catch (Exception ex) { return new { success = false, error = $"Failed to convert event to Pokemon: {ex.Message}" }; }

            if (pkm == null || pkm.Species == 0)
                return new { success = false, error = "Event converted to an empty Pokemon." };

            // Force shiny if the wondercard is shiny-flagged OR the user explicitly requested shiny.
            // ConvertToPKM randomizes PID for raid encounters so we need to re-apply the shiny flag.
            bool wcShiny = mg.Shiny == Shiny.Always || mg.Shiny == Shiny.AlwaysSquare || mg.Shiny == Shiny.AlwaysStar;
            LogUtil.LogInfo($"[WebTrade] Pre-force check: species={pkm.Species}, IsShiny={pkm.IsShiny}, mg.Shiny={mg.Shiny}, forceShiny={forceShiny}", "WebTrade");
            if (wcShiny || forceShiny)
            {
                // Method 1: SetIsShiny
                pkm.SetIsShiny(true);
                // Method 2: If still not shiny, brute-force the PID until shiny
                int bruteAttempts = 0;
                while (!pkm.IsShiny && bruteAttempts < 1000000)
                {
                    pkm.PID = (uint)Random.Shared.Next(int.MinValue, int.MaxValue);
                    bruteAttempts++;
                }
                pkm.RefreshChecksum();
                LogUtil.LogInfo($"[WebTrade] Forced shiny on event Pokemon. IsShiny now={pkm.IsShiny}, brute attempts={bruteAttempts}", "WebTrade");
            }

            // If the format doesn't match T, convert up
            if (pkm is not T pkTyped)
            {
                var converted = EntityConverter.ConvertToType(pkm, typeof(T), out _);
                if (converted is not T conv)
                    return new { success = false, error = "Could not convert event Pokemon to this game's format. Try a bot for the original game." };
                pkTyped = conv;
            }

            // Validate legality — if the event file is good, this should always pass.
            // Skip legality check if we force-shiny'd (PID manipulation may invalidate encounter match).
            var la = new LegalityAnalysis(pkTyped);
            if (!la.Valid && !(wcShiny || forceShiny))
            {
                LogUtil.LogError($"[WebTrade] Event file failed legality: {filePath} — {la.Report()}", "WebTrade");
                return new { success = false, error = "Event Pokemon is not legal in this game's format." };
            }
            if (!la.Valid)
                LogUtil.LogInfo($"[WebTrade] Skipping legality check for forced-shiny event: {la.Report()}", "WebTrade");

            if (tradeCode <= 0)
                tradeCode = Random.Shared.Next(10000000, 99999999);

            var tradeId = $"WEB-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():X}-{Random.Shared.Next(1000):X3}";
            ulong webUserID = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var trainerInfo = new PokeTradeTrainerInfo(username, webUserID);
            var notifier = new WebTradeNotifier<T>(tradeId);
            var detail = new PokeTradeDetail<T>(pkTyped, trainerInfo, notifier, PokeTradeType.Specific, tradeCode, false, ignoreAutoOT: true);

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);
            var entry = new TradeEntry<T>(detail, webUserID, PokeRoutineType.LinkTrade, username, uniqueTradeID);

            var added = hub.Queues.Info.AddToTradeQueue(entry, webUserID, false, false);
            if (added == QueueResultAdd.AlreadyInQueue)
                return new { success = false, error = "A trade from this session is already in queue." };
            if (added == QueueResultAdd.QueueFull)
                return new { success = false, error = "The trade queue is currently full. Please try again later." };

            ActiveTrades[tradeId] = new WebTradeStatus
            {
                TradeId = tradeId,
                Status = "queued",
                Message = "Event trade queued successfully",
                LinkCode = tradeCode,
                UserName = username,
                LastUpdated = DateTime.Now,
            };

            var speciesName = GameInfo.GetStrings("en").Species[pkTyped.Species];
            LogUtil.LogInfo($"Web event trade queued: {username} -> {speciesName} from {Path.GetFileName(filePath)} (code: {tradeCode})", "WebTrade");

            return new
            {
                success = true,
                tradeId,
                tradeCode,
                position = hub.Queues.Info.Count,
                pokemon = speciesName,
                isEvent = true,
                eventFile = Path.GetFileName(filePath),
            };
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"SubmitEventTrade error: {ex.Message}", "WebTrade");
            return new { success = false, error = $"Failed to create event trade: {ex.Message}" };
        }
    }

    private static object SubmitTradeInternal<T>(PokeTradeHub<T> hub, string showdownSet, string username, int tradeCode, bool forceShiny = false) where T : PKM, new()
    {
        try
        {
            // Parse the showdown set. If it fails, retry with aggressive species-name normalization
            // (strip known form suffix words that the browser UI may have appended).
            if (!ShowdownParsing.TryParseAnyLanguage(showdownSet, out var set) || set == null || set.Species == 0)
            {
                var lines = showdownSet.Replace("\r\n","\n").Split('\n');
                if (lines.Length > 0)
                {
                    var firstLine = lines[0];
                    // Split off held item portion first
                    var atIdx = firstLine.IndexOf(" @ ");
                    var speciesPart = atIdx >= 0 ? firstLine.Substring(0, atIdx) : firstLine;
                    var itemPart = atIdx >= 0 ? firstLine.Substring(atIdx) : "";
                    // If species has multiple words, try progressively shorter prefixes
                    var words = speciesPart.Trim().Split(' ');
                    for (int take = words.Length - 1; take >= 1; take--)
                    {
                        var candidate = string.Join(" ", words, 0, take);
                        var retryLines = new string[lines.Length];
                        retryLines[0] = candidate + itemPart;
                        for (int i = 1; i < lines.Length; i++) retryLines[i] = lines[i];
                        var retrySet = string.Join("\n", retryLines);
                        if (ShowdownParsing.TryParseAnyLanguage(retrySet, out set) && set != null && set.Species > 0)
                        {
                            LogUtil.LogInfo($"[WebTrade] Showdown parse recovered by trimming species to '{candidate}' (original: '{speciesPart}')", "WebTrade");
                            showdownSet = retrySet;
                            break;
                        }
                    }
                }
                if (set == null || set.Species == 0)
                {
                    return new { success = false, error = "Could not parse the Pokemon set. Please check the format." };
                }
            }

            // Auto-attach correct held item for Giratina-Origin based on target game format.
            // Gen 4-8 (PK8/PB8/PA8) uses "Griseous Orb". Gen 9 (PK9) uses "Griseous Core".
            if (set.Species == 487 && set.Form == 1 && set.HeldItem == 0)
            {
                var itemName = typeof(T) == typeof(PK9) ? "Griseous Core" : "Griseous Orb";
                LogUtil.LogInfo($"[WebTrade] Auto-attaching {itemName} to Giratina-Origin ({typeof(T).Name})", "WebTrade");
                var setText = set.Text;
                var lines = setText.Replace("\r\n","\n").Split('\n');
                if (lines.Length > 0 && !lines[0].Contains(" @ "))
                {
                    lines[0] = lines[0] + " @ " + itemName;
                    setText = string.Join("\n", lines);
                    if (ShowdownParsing.TryParseAnyLanguage(setText, out var newSet) && newSet != null)
                        set = newSet;
                }
            }

            // Generate the template
            var template = AutoLegalityWrapper.GetTemplate(set);
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();

            PKM pkm;
            string result;

            // SwSh-only species routed through PK8 → HOME → PK9 for legal SV trades.
            // Eternatus (890), Zacian (888), Zamazenta (889), Kubfu (891), Urshifu (892),
            // Regieleki (894), Regidrago (895), Glastrier (896), Spectrier (897), Calyrex (898).
            // Older Regis re-encounter through Crown Tundra: Regirock (377), Regice (378),
            // Registeel (379), Regigigas (486) — ALM can't generate them legally for PK9 directly.
            // Crown Tundra mythicals: Deoxys (386, all forms), Volcanion (721), Hoopa (720, both forms) —
            // same encounter mismatch when generated as PK9 directly; ALM finds them via SwSh DA / event encounters.
            // Also legendary birds with Galar forms (Articuno/Zapdos/Moltres form 1) since
            // ALM can't generate Galar form natively in PK9 — same encounter problem.
            bool isSwShOnlySpecies =
                template.Species == 890 || template.Species == 888 || template.Species == 889 ||
                template.Species == 891 || template.Species == 892 || template.Species == 894 ||
                template.Species == 895 || template.Species == 896 || template.Species == 897 ||
                template.Species == 898 ||
                template.Species == 377 || template.Species == 378 ||
                template.Species == 379 || template.Species == 486 ||
                template.Species == 386 || template.Species == 721 ||
                template.Species == 720;
            bool isGalarBird =
                (template.Species == 144 || template.Species == 145 || template.Species == 146)
                && template.Form == 1;
            bool needsSwShRouting = typeof(T) == typeof(PK9) && (isSwShOnlySpecies || isGalarBird);

            if (needsSwShRouting)
            {
                LogUtil.LogInfo($"[WebTrade] Species {template.Species} needs SwSh→HOME→SV chain", "WebTrade");
                pkm = null!;
                result = "SwSh routing pending";
                ITrainerInfo swshSav = new SimpleTrainerInfo(GameVersion.SW)
                { OT = sav.OT, TID16 = sav.TID16, SID16 = sav.SID16, Language = sav.Language };
                try
                {
                    var pk8 = swshSav.GetLegal(template, out var pk8Result);
                    if (pk8 != null && pk8.Species == template.Species && pk8.Form == template.Form)
                    {
                        // Verify the SwSh source itself is legal before converting — otherwise we'd
                        // ship an illegal PK9 (e.g. shiny Regigigas: Crown Shrine is shiny-locked,
                        // ALM returns a non-null but invalid PK8).
                        var pk8La = new LegalityAnalysis(pk8);
                        if (!pk8La.Valid)
                        {
                            LogUtil.LogInfo($"[WebTrade] SwSh source PK8 invalid for species {template.Species} (likely shiny-lock or encounter mismatch), skipping conversion → pre-made fallback", "WebTrade");
                        }
                        else
                        {
                            pk8.RefreshChecksum();
                            var converted = EntityConverter.ConvertToType(pk8, typeof(T), out var _);
                            if (converted is T convTarget && convTarget.Species == template.Species && convTarget.Form == template.Form)
                            {
                                var convLa = new LegalityAnalysis(convTarget);
                                if (convLa.Valid)
                                {
                                    convTarget.RefreshChecksum();
                                    pkm = convTarget;
                                    result = "SwSh→HOME→SV";
                                    LogUtil.LogInfo($"[WebTrade] PK8→PK9 via HOME succeeded for species {template.Species} form {template.Form}", "WebTrade");
                                }
                                else
                                {
                                    LogUtil.LogInfo($"[WebTrade] Converted PK9 invalid for species {template.Species}, falling back to pre-made", "WebTrade");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { LogUtil.LogError($"[WebTrade] SwSh routing exception: {ex.Message}", "WebTrade"); }

                // If SwSh routing failed, jump DIRECTLY to pre-made file (skip PB8/PK8 fallbacks
                // which would also fail for problem species and waste several seconds each).
                if (pkm == null)
                {
                    LogUtil.LogInfo($"[WebTrade] SwSh routing failed for species {template.Species}/form {template.Form}. Loading pre-made file.", "WebTrade");
                    try
                    {
                        var preMadeFolder = @"C:\Users\ericr\OneDrive\Desktop\HOME-Ready-Files";
                        if (Directory.Exists(preMadeFolder))
                        {
                            var prefix = template.Species.ToString("D4");
                            var formSuffix = template.Form > 0 ? $"-{template.Form:D2}" : "";
                            var patterns = template.Shiny
                                ? new[] { $"{prefix}{formSuffix} ★ -*.pk9", $"{prefix}{formSuffix} -*.pk9" }
                                : new[] { $"{prefix}{formSuffix} -*.pk9", $"{prefix}{formSuffix} ★ -*.pk9" };
                            foreach (var pat in patterns)
                            {
                                var files = Directory.GetFiles(preMadeFolder, pat);
                                if (files.Length == 0) continue;
                                var bytes = File.ReadAllBytes(files[0]);
                                var loaded = EntityFormat.GetFromBytes(bytes, EntityContext.Gen9);
                                if (loaded is T preMade && preMade.Species == template.Species && preMade.Form == template.Form)
                                {
                                    if (template.Shiny && !preMade.IsShiny) { preMade.SetIsShiny(true); preMade.RefreshChecksum(); }
                                    var preMadeLa = new LegalityAnalysis(preMade);
                                    if (preMadeLa.Valid)
                                    {
                                        pkm = preMade;
                                        result = "PreMadeFile";
                                        LogUtil.LogInfo($"[WebTrade] Loaded pre-made {Path.GetFileName(files[0])} for species {template.Species}/form {template.Form}", "WebTrade");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { LogUtil.LogError($"[WebTrade] Pre-made file fallback exception: {ex.Message}", "WebTrade"); }
                }

                // If pre-made also failed, return error immediately instead of letting downstream fallbacks waste more time
                if (pkm == null)
                {
                    var wantName = GameInfo.GetStrings("en").Species[template.Species];
                    return new { success = false, error = $"Could not generate {wantName} (form {template.Form}). The pre-made file may be missing." };
                }
            }
            // Vivillon form workaround for ZA: generate as Meadow, fix form later
            else if (template.Species == 666 && template.Form != 6 && typeof(T) == typeof(PA9))
            {
                var meadowSet = new ShowdownSet("Vivillon-Meadow\nLevel: 100");
                var meadowTemplate = AutoLegalityWrapper.GetTemplate(meadowSet);
                pkm = sav.GetLegal(meadowTemplate, out result);
            }
            // Alcremie form workaround: generate as default, fix form later
            else if (template.Species == 869 && template.Form != 0)
            {
                var defaultSet = new ShowdownSet("Alcremie\nLevel: 100");
                var defaultTemplate = AutoLegalityWrapper.GetTemplate(defaultSet);
                pkm = sav.GetLegal(defaultTemplate, out result);
            }
            else
            {
                pkm = sav.GetLegalForTrade(template, out result);
            }

            // HOME-transfer fallback: if direct generation fails or produces the wrong form,
            // try PB8 then PK8 + convert. Also preserve requested form through the conversion.
            bool IsValidResult(PKM p) => p != null && p.Species == template.Species && p.Form == template.Form;
            if (!IsValidResult(pkm) && typeof(T) != typeof(PB8))
            {
                LogUtil.LogInfo($"[WebTrade] Direct gen failed for species {template.Species} form {template.Form}, trying PB8 fallback", "WebTrade");
                try
                {
                    ITrainerInfo bdspSav = new SimpleTrainerInfo(GameVersion.BD)
                    { OT = sav.OT, TID16 = sav.TID16, SID16 = sav.SID16, Language = sav.Language };
                    var pb8 = bdspSav.GetLegal(template, out var pb8Result);
                    if (pb8 != null && pb8.Species == template.Species)
                    {
                        // Force form before conversion (PB8 sometimes drops it)
                        pb8.Form = (byte)template.Form;
                        if (template.Species == 487 && template.Form == 1 && pb8.HeldItem == 0)
                        { pb8.HeldItem = 112; }
                        pb8.RefreshChecksum();
                        var converted = EntityConverter.ConvertToType(pb8, typeof(T), out var convRes);
                        if (converted is T convTarget && convTarget.Species == template.Species)
                        {
                            // Force form in target format (PK9 Giratina Origin needs form=1 + Griseous Core)
                            convTarget.Form = (byte)template.Form;
                            // Attach correct item per format
                            if (template.Species == 487 && template.Form == 1)
                            {
                                if (typeof(T) == typeof(PK9)) convTarget.HeldItem = 2413; // Griseous Core (Gen 9)
                                else if (convTarget.HeldItem == 0) convTarget.HeldItem = 112; // Griseous Orb (Gen 4-8)
                            }
                            convTarget.RefreshChecksum();
                            pkm = convTarget;
                            result = "Regenerated";
                            LogUtil.LogInfo($"[WebTrade] PB8→{typeof(T).Name} conversion succeeded, form={convTarget.Form}, item={convTarget.HeldItem}", "WebTrade");
                        }
                    }
                }
                catch (Exception ex) { LogUtil.LogError($"[WebTrade] BDSP fallback exception: {ex.Message}", "WebTrade"); }
            }
            if (!IsValidResult(pkm) && typeof(T) != typeof(PK8))
            {
                LogUtil.LogInfo($"[WebTrade] Trying PK8 (SWSH) fallback + HOME conversion", "WebTrade");
                try
                {
                    ITrainerInfo swshSav = new SimpleTrainerInfo(GameVersion.SW)
                    { OT = sav.OT, TID16 = sav.TID16, SID16 = sav.SID16, Language = sav.Language };
                    var pk8 = swshSav.GetLegal(template, out var pk8Result);
                    if (pk8 != null && pk8.Species == template.Species)
                    {
                        pk8.Form = (byte)template.Form;
                        pk8.RefreshChecksum();
                        var converted = EntityConverter.ConvertToType(pk8, typeof(T), out var convRes);
                        if (converted is T convTarget && convTarget.Species == template.Species)
                        {
                            convTarget.Form = (byte)template.Form;
                            if (template.Species == 487 && template.Form == 1)
                            {
                                if (typeof(T) == typeof(PK9)) convTarget.HeldItem = 2413;
                                else if (convTarget.HeldItem == 0) convTarget.HeldItem = 112;
                            }
                            convTarget.RefreshChecksum();
                            pkm = convTarget;
                            result = "Regenerated";
                            LogUtil.LogInfo($"[WebTrade] PK8→{typeof(T).Name} conversion succeeded, form={convTarget.Form}", "WebTrade");
                        }
                    }
                }
                catch (Exception ex) { LogUtil.LogError($"[WebTrade] SWSH fallback exception: {ex.Message}", "WebTrade"); }
            }

            // Pre-made file fallback: if ALL ALM/conversion paths failed (or returned wrong species),
            // OR if ALM returned an ILLEGAL PKM (e.g. shiny-locked species with shiny requested),
            // load a known-legal pre-made file from HOME-Ready-Files. Covers species ALM can't
            // reliably regenerate (SwSh-only legendaries, Eternatus, Zacian, shiny mythicals).
            bool currentIsIllegal = pkm != null && IsValidResult(pkm) && !new LegalityAnalysis(pkm).Valid;
            if (currentIsIllegal)
                LogUtil.LogInfo($"[WebTrade] ALM result for species {template.Species} is invalid, trying pre-made fallback", "WebTrade");
            if (!IsValidResult(pkm) || currentIsIllegal)
            {
                try
                {
                    var preMadeFolder = @"C:\Users\ericr\OneDrive\Desktop\HOME-Ready-Files";
                    if (Directory.Exists(preMadeFolder))
                    {
                        var prefix = template.Species.ToString("D4");
                        var ext = typeof(T) == typeof(PK9) ? ".pk9"
                                : typeof(T) == typeof(PK8) ? ".pk8"
                                : typeof(T) == typeof(PB8) ? ".pb8"
                                : typeof(T) == typeof(PA8) ? ".pa8"
                                : typeof(T) == typeof(PA9) ? ".pa9"
                                : typeof(T) == typeof(PB7) ? ".pb7"
                                : ".pkm";
                        var ctx = typeof(T) == typeof(PK9) ? EntityContext.Gen9
                                : typeof(T) == typeof(PK8) ? EntityContext.Gen8
                                : typeof(T) == typeof(PB8) ? EntityContext.Gen8b
                                : typeof(T) == typeof(PA8) ? EntityContext.Gen8a
                                : typeof(T) == typeof(PA9) ? EntityContext.Gen9
                                : typeof(T) == typeof(PB7) ? EntityContext.Gen7b
                                : EntityContext.Gen9;
                        // Form-specific filename: "0144-01" for Articuno-Galar, "0146-01" for Moltres-Galar etc.
                        // Form 0 (default/Kanto) uses just "0144" with no form suffix.
                        var formSuffix = template.Form > 0 ? $"-{template.Form:D2}" : "";
                        var patterns = template.Shiny
                            ? new[] { $"{prefix}{formSuffix} ★ -*{ext}", $"{prefix}{formSuffix} -*{ext}", $"{prefix} ★ -*{ext}", $"{prefix} -*{ext}" }
                            : new[] { $"{prefix}{formSuffix} -*{ext}", $"{prefix}{formSuffix} ★ -*{ext}", $"{prefix} -*{ext}", $"{prefix} ★ -*{ext}" };
                        foreach (var pat in patterns)
                        {
                            var files = Directory.GetFiles(preMadeFolder, pat);
                            if (files.Length > 0)
                            {
                                var bytes = File.ReadAllBytes(files[0]);
                                var loaded = EntityFormat.GetFromBytes(bytes, ctx);
                                if (loaded is T preMade && preMade.Species == template.Species && preMade.Form == template.Form)
                                {
                                    // Match shiny preference
                                    if (template.Shiny && !preMade.IsShiny) { preMade.SetIsShiny(true); preMade.RefreshChecksum(); }
                                    else if (!template.Shiny && preMade.IsShiny)
                                    {
                                        int tries = 0;
                                        while (preMade.IsShiny && tries++ < 100000)
                                            preMade.PID = (uint)Random.Shared.Next(int.MinValue, int.MaxValue);
                                        preMade.RefreshChecksum();
                                    }
                                    var preMadeLa = new LegalityAnalysis(preMade);
                                    if (preMadeLa.Valid)
                                    {
                                        pkm = preMade;
                                        result = "PreMadeFile";
                                        LogUtil.LogInfo($"[WebTrade] Loaded pre-made {Path.GetFileName(files[0])} (form={preMade.Form}, shiny={preMade.IsShiny}) for species {template.Species}", "WebTrade");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { LogUtil.LogError($"[WebTrade] Pre-made file fallback exception: {ex.Message}", "WebTrade"); }
            }

            if (pkm == null)
            {
                return new { success = false, error = "Could not create a Pokemon from that set." };
            }

            // No strict legality gate — match Discord $t behavior (send whatever ALM produces)

            // Final form safety net — PK9 Giratina Origin must have form=1 AND Griseous Core held
            if (pkm.Species == template.Species && pkm.Form != template.Form)
            {
                LogUtil.LogInfo($"[WebTrade] Force-correcting form: {pkm.Form} → {template.Form}", "WebTrade");
                pkm.Form = (byte)template.Form;
            }
            // Generic form-required held item attachment.
            // Maps (species, form) → required item name. Looks up the ID from PKHeX's live
            // item list so we never hardcode IDs that differ between games.
            TryAttachFormItem(pkm, template.Species, template.Form, typeof(T));
            // Giratina is genderless — fix if conversion leaked a gender
            if (pkm.Species == 487) pkm.Gender = 2;
            // Set HOME tracker for HOME-transferred Pokemon (required by Switch server-side checks).
            if (pkm is IHomeTrack ht && ht.Tracker == 0)
            {
                var rng = new Random((int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & int.MaxValue));
                var buf = new byte[8];
                rng.NextBytes(buf);
                ht.Tracker = BitConverter.ToUInt64(buf, 0);
                LogUtil.LogInfo($"[WebTrade] Set HOME tracker for {pkm.Species} (cross-game transfer)", "WebTrade");
            }
            // Clear stale nickname/trash bytes from prior format so the species name displays correctly
            pkm.ClearNickname();
            pkm.RefreshChecksum();

            // Fix invalid moves: if any move is flagged, clear it so ALM can set legal ones
            var moveLa = new LegalityAnalysis(pkm);
            if (!moveLa.Valid)
            {
                var moveReport = moveLa.Report();
                if (moveReport.Contains("Invalid Move"))
                {
                    LogUtil.LogInfo($"[WebTrade] Clearing invalid moves and letting ALM suggest legal ones", "WebTrade");
                    pkm.SetSuggestedMoves();
                    pkm.HealPP();
                    pkm.RefreshChecksum();
                }
            }

            // Final pass: run ALM's auto-legalizer which fixes encounter metadata mismatches
            // (met location, version flags, origin game, PID/IV correlation) that the Switch
            // uses for server-side validation. This is what makes cross-game transfers legal.
            var finalLa = new LegalityAnalysis(pkm);
            if (!finalLa.Valid)
            {
                LogUtil.LogInfo($"[WebTrade] Post-processing invalid PKM, attempting ALM auto-legalize: {finalLa.Report().Split('\n')[0]}", "WebTrade");
                try
                {
                    var legalized = pkm.LegalizePokemon();
                    if (legalized != null)
                    {
                        var legalLa = new LegalityAnalysis(legalized);
                        LogUtil.LogInfo($"[WebTrade] ALM legalize result: Valid={legalLa.Valid}", "WebTrade");
                        if (legalLa.Valid && legalized is T legalTarget)
                        {
                            // Preserve form/item/shiny from our original build
                            legalTarget.Form = pkm.Form;
                            if (pkm.Species == 487 && pkm.Form == 1)
                            {
                                legalTarget.HeldItem = typeof(T) == typeof(PK9) ? 2413 : 112;
                            }
                            if (pkm.IsShiny && !legalTarget.IsShiny)
                            {
                                legalTarget.SetIsShiny(true);
                            }
                            legalTarget.RefreshChecksum();
                            pkm = legalTarget;
                            LogUtil.LogInfo($"[WebTrade] Replaced with legalized PKM: form={pkm.Form}, item={pkm.HeldItem}, shiny={pkm.IsShiny}", "WebTrade");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"[WebTrade] ALM legalize exception: {ex.Message}", "WebTrade");
                }
            }

            // Re-apply form correction after legalize. ALM's LegalizePokemon mutates pkm in-place
            // even when it returns false/invalid, often resetting alternate forms back to form 0.
            // Without this re-application, regional forms (Articuno-Galar, etc.) get downgraded
            // to base forms (Articuno-Kanto) silently.
            if (pkm.Species == template.Species && pkm.Form != template.Form)
            {
                LogUtil.LogInfo($"[WebTrade] Post-legalize form correction: {pkm.Form} → {template.Form}", "WebTrade");
                pkm.Form = (byte)template.Form;
                TryAttachFormItem(pkm, template.Species, template.Form, typeof(T));
                pkm.RefreshChecksum();
            }

            // Species mismatch check — ALM sometimes falls back to the wrong species when
            // it can't find a legal encounter (e.g., non-shiny Eternatus in SV — it has none,
            // so ALM picks an unrelated legendary wondercard like Zamazenta as fallback).
            // This catches those cases and aborts the trade rather than sending the wrong mon.
            if (pkm.Species != template.Species)
            {
                var wantName = GameInfo.GetStrings("en").Species[template.Species];
                var gotName = GameInfo.GetStrings("en").Species[pkm.Species];
                LogUtil.LogInfo($"[WebTrade] Species mismatch: requested {wantName} ({template.Species}) but got {gotName} ({pkm.Species}), retrying...", "WebTrade");
                pkm = sav.GetLegal(template, out result);
                if (pkm == null || pkm.Species != template.Species)
                {
                    LogUtil.LogError($"[WebTrade] ABORT: Could not generate {wantName} legally for this game. ALM fell back to an unrelated encounter.", "WebTrade");
                    return new { success = false, error = $"{wantName} is not legally obtainable in this game with those options. Try a different form, shiny setting, or use a different game's bot." };
                }
            }

            // Clean up Pokemon name and trash bytes
            pkm.ClearNickname();
            pkm.RefreshChecksum();

            // Special shiny path: Giratina is shiny-capable in BDSP natively. For SV (PK9) shiny
            // Giratina, generate it in PB8 as shiny (legitimate Turnback Cave encounter) then
            // convert up via HOME transfer chain — preserves a valid shiny signature.
            if (forceShiny && !pkm.IsShiny && pkm.Species == 487 && typeof(T) == typeof(PK9))
            {
                LogUtil.LogInfo($"[WebTrade] Shiny Giratina on PK9 — routing through PB8 (BDSP native shiny)", "WebTrade");
                try
                {
                    ITrainerInfo bdspSav = new SimpleTrainerInfo(GameVersion.BD)
                    { OT = sav.OT, TID16 = sav.TID16, SID16 = sav.SID16, Language = sav.Language };
                    // Build a shiny Giratina set (BDSP can legitimately produce shiny)
                    var shinyText = "Giratina" + (template.Form == 1 ? "-Origin @ Griseous Orb" : "") + "\nShiny: Yes\nLevel: 100";
                    if (ShowdownParsing.TryParseAnyLanguage(shinyText, out var shinySet) && shinySet != null)
                    {
                        var shinyTemplate = AutoLegalityWrapper.GetTemplate(shinySet);
                        var pb8Shiny = bdspSav.GetLegal(shinyTemplate, out var pb8ShinyResult);
                        if (pb8Shiny != null && pb8Shiny.Species == 487 && pb8Shiny.IsShiny)
                        {
                            pb8Shiny.Form = (byte)template.Form;
                            if (template.Form == 1 && pb8Shiny.HeldItem == 0) pb8Shiny.HeldItem = 112;
                            pb8Shiny.RefreshChecksum();
                            var shinyConv = EntityConverter.ConvertToType(pb8Shiny, typeof(T), out var shinyConvRes);
                            if (shinyConv is T shinyTarget && shinyTarget.Species == 487 && shinyTarget.IsShiny)
                            {
                                shinyTarget.Form = (byte)template.Form;
                                if (template.Form == 1 && typeof(T) == typeof(PK9)) shinyTarget.HeldItem = 2413;
                                shinyTarget.Gender = 2;
                                shinyTarget.ClearNickname();
                                shinyTarget.RefreshChecksum();
                                pkm = shinyTarget;
                                result = "Regenerated";
                                LogUtil.LogInfo($"[WebTrade] Shiny Giratina PB8→PK9 conversion succeeded", "WebTrade");
                            }
                        }
                    }
                }
                catch (Exception ex) { LogUtil.LogError($"[WebTrade] Shiny Giratina BDSP route exception: {ex.Message}", "WebTrade"); }
            }

            // Force shiny ONLY if client requested AND the result was non-shiny AND a LegalityAnalysis
            // after SetIsShiny still validates. Raw PID brute force breaks legality and causes the
            // Switch to reject the trade with "there is a problem with your trading partner's pokemon".
            if (forceShiny && !pkm.IsShiny)
            {
                LogUtil.LogInfo($"[WebTrade] Force-shiny requested for {pkm.Species}", "WebTrade");
                var beforePid = pkm.PID;
                pkm.SetIsShiny(true);
                pkm.RefreshChecksum();
                var testLa = new LegalityAnalysis(pkm);
                if (!testLa.Valid)
                {
                    // SetIsShiny broke legality — restore original PID. Don't brute force.
                    LogUtil.LogInfo($"[WebTrade] Force-shiny rolled back for {pkm.Species} — SetIsShiny produced illegal PKM. Keeping legal non-shiny.", "WebTrade");
                    pkm.PID = beforePid;
                    pkm.RefreshChecksum();
                }
                else
                {
                    LogUtil.LogInfo($"[WebTrade] Force-shiny success: {pkm.Species} is now shiny and legal", "WebTrade");
                }
            }

            // Vivillon form fix — force the requested form (Z-A: only Meadow/Garden/Marine)
            if (pkm.Species == 666 && set.Form != pkm.Form)
            {
                // In Z-A only Meadow (6), Garden (4), and Marine (8) are legal
                if (typeof(T) == typeof(PA9) && set.Form != 4 && set.Form != 6 && set.Form != 8)
                    return new { success = false, error = "That Vivillon pattern is not available in Legends Z-A. Only Meadow, Marine, and Garden exist in this game." };

                pkm.Form = (byte)set.Form;
                pkm.ClearNickname();
                pkm.RefreshChecksum();
            }

            // Alcremie form fix — force the requested form
            if (pkm.Species == 869 && set.Form != pkm.Form)
            {
                pkm.Form = (byte)set.Form;
                pkm.ClearNickname();
                pkm.RefreshChecksum();
            }

            if (pkm is not T pk)
            {
                // Try conversion
                var converted = EntityConverter.ConvertToType(pkm, typeof(T), out var convResult);
                if (converted is not T convPk)
                    return new { success = false, error = "Pokemon is not compatible with this game." };
                pk = convPk;
            }

            // Let ALM handle IVs — forcing 6IV can break legality

            // Generate trade code if not provided
            if (tradeCode <= 0)
                tradeCode = Random.Shared.Next(10000000, 99999999);

            // Create trade ID
            var tradeId = $"WEB-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():X}-{Random.Shared.Next(1000):X3}";

            // Create trainer info and trade detail
            // Use a unique userID per web trade so queue doesn't block concurrent web users
            ulong webUserID = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var trainerInfo = new PokeTradeTrainerInfo(username, (ulong)webUserID);
            var notifier = new WebTradeNotifier<T>(tradeId);
            // Enable AutoOT for normal ALM-generated trades so the receiver's Switch/HOME
            // accepts them. AutoOT applies cached trainer info or falls back to bot OT with
            // proper HT chain. Only event files (.wc9, .wb7) skip this — see SubmitEventTrade.
            // Raid-locked species (Chi-Yu, Treasures of Ruin, etc.) should be requested as
            // event files instead of showdown sets to preserve encounter metadata.
            var detail = new PokeTradeDetail<T>(pk, trainerInfo, notifier, PokeTradeType.Specific, tradeCode, false, ignoreAutoOT: false);

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);
            var entry = new TradeEntry<T>(detail, webUserID, PokeRoutineType.LinkTrade, username, uniqueTradeID);

            // Add to queue
            var added = hub.Queues.Info.AddToTradeQueue(entry, webUserID, false, false);

            if (added == QueueResultAdd.AlreadyInQueue)
                return new { success = false, error = "A trade from this session is already in queue." };

            if (added == QueueResultAdd.QueueFull)
                return new { success = false, error = "The trade queue is currently full. Please try again later." };

            // Track the trade
            ActiveTrades[tradeId] = new WebTradeStatus
            {
                TradeId = tradeId,
                Status = "queued",
                Message = "Trade queued successfully",
                LinkCode = tradeCode,
                UserName = username,
                LastUpdated = DateTime.Now
            };

            var speciesName = GameInfo.GetStrings("en").Species[pk.Species];
            LogUtil.LogInfo($"Web trade queued: {username} -> {speciesName} (code: {tradeCode})", "WebTrade");

            return new
            {
                success = true,
                tradeId,
                tradeCode,
                position = hub.Queues.Info.Count,
                pokemon = speciesName
            };
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"SubmitTradeInternal error: {ex.Message}", "WebTrade");
            return new { success = false, error = $"Failed to create trade: {ex.Message}" };
        }
    }

    public static WebTradeStatus? GetTradeStatus(string tradeId)
    {
        ActiveTrades.TryGetValue(tradeId, out var status);
        return status;
    }

    /// <summary>
    /// Gets queue user list from whichever hub is registered.
    /// Returns null if no hub is available.
    /// </summary>
    public static IEnumerable<string>? GetQueueUserList(string format)
    {
        object? hub = (object?)_hubPA9 ?? (object?)_hubPK9 ?? (object?)_hubPK8 ?? (object?)_hubPB8 ?? (object?)_hubPA8 ?? (object?)_hubPB7;
        if (hub == null) return null;

        var queuesField = hub.GetType().GetField("Queues");
        var queues = queuesField?.GetValue(hub);
        if (queues == null) return new List<string>();

        var infoProperty = queues.GetType().GetProperty("Info");
        var info = infoProperty?.GetValue(queues);
        if (info == null) return new List<string>();

        var getUserListMethod = info.GetType().GetMethod("GetUserList");
        return getUserListMethod?.Invoke(info, new object[] { format }) as IEnumerable<string> ?? new List<string>();
    }

    /// <summary>
    /// Gets the queue count from whichever hub is registered.
    /// </summary>
    public static int GetQueueCount()
    {
        object? hub = (object?)_hubPA9 ?? (object?)_hubPK9 ?? (object?)_hubPK8 ?? (object?)_hubPB8 ?? (object?)_hubPA8 ?? (object?)_hubPB7;
        if (hub == null) return 0;

        var queuesField = hub.GetType().GetField("Queues");
        var queues = queuesField?.GetValue(hub);
        if (queues == null) return 0;

        var infoProperty = queues.GetType().GetProperty("Info");
        var info = infoProperty?.GetValue(queues);
        if (info == null) return 0;

        var countProperty = info.GetType().GetProperty("Count");
        return (int)(countProperty?.GetValue(info) ?? 0);
    }
}

/// <summary>
/// Minimal trade notifier for web trades (no Discord DMs)
/// </summary>
public class WebTradeNotifier<T> : IPokeTradeNotifier<T> where T : PKM, new()
{
    private readonly string _tradeId;

    public WebTradeNotifier(string tradeId) => _tradeId = tradeId;

    public Action<PokeRoutineExecutor<T>>? OnFinish { get; set; }

    public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] Initializing trade", "WebTrade");
    }

    public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] Searching for trade partner", "WebTrade");
    }

    public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] Trade canceled: {msg}", "WebTrade");
        OnFinish?.Invoke(routine);
    }

    public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] Trade completed!", "WebTrade");
        OnFinish?.Invoke(routine);
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message)
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] {message}", "WebTrade");
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message)
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] {message.Summary}", "WebTrade");
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message)
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] {message}", "WebTrade");
    }

    public Task SendInitialQueueUpdate()
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] Queued", "WebTrade");
        return Task.CompletedTask;
    }

    public void UpdateBatchProgress(int currentBatchNumber, T currentPokemon, int uniqueTradeID)
    {
        LogUtil.LogInfo($"[WebTrade {_tradeId}] Batch progress: {currentBatchNumber}", "WebTrade");
    }
}
