using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    public static object SubmitTrade(string showdownSet, string username, int tradeCode)
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
            if (_hubPA9 != null) return SubmitTradeInternal(_hubPA9, showdownSet, username, tradeCode);
            if (_hubPK9 != null) return SubmitTradeInternal(_hubPK9, showdownSet, username, tradeCode);
            if (_hubPK8 != null) return SubmitTradeInternal(_hubPK8, showdownSet, username, tradeCode);
            if (_hubPB8 != null) return SubmitTradeInternal(_hubPB8, showdownSet, username, tradeCode);
            if (_hubPA8 != null) return SubmitTradeInternal(_hubPA8, showdownSet, username, tradeCode);
            if (_hubPB7 != null) return SubmitTradeInternal(_hubPB7, showdownSet, username, tradeCode);

            return new { success = false, error = "No trade hub available" };
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"WebTradeHandler.SubmitTrade error: {ex.Message}", "WebTrade");
            return new { success = false, error = ex.Message };
        }
    }

    private static object SubmitTradeInternal<T>(PokeTradeHub<T> hub, string showdownSet, string username, int tradeCode) where T : PKM, new()
    {
        try
        {
            // Parse the showdown set
            if (!ShowdownParsing.TryParseAnyLanguage(showdownSet, out var set) || set == null || set.Species == 0)
            {
                return new { success = false, error = "Could not parse the Pokemon set. Please check the format." };
            }

            // Generate the template
            var template = AutoLegalityWrapper.GetTemplate(set);
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();

            PKM pkm;
            string result;

            // Vivillon form workaround for ZA: generate as Meadow, fix form later
            if (template.Species == 666 && template.Form != 6 && typeof(T) == typeof(PA9))
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
                pkm = sav.GetLegal(template, out result);
            }

            if (pkm == null)
            {
                return new { success = false, error = "Could not create a legal Pokemon from that set." };
            }

            // Species mismatch check — ALM sometimes generates wrong species
            if (pkm.Species != template.Species)
            {
                LogUtil.LogInfo($"[WebTrade] Species mismatch: requested {template.Species} but got {pkm.Species}, retrying...", "WebTrade");
                // Try once more
                pkm = sav.GetLegal(template, out result);
                if (pkm == null || pkm.Species != template.Species)
                {
                    return new { success = false, error = $"Could not generate the correct Pokemon. Please try again." };
                }
            }

            // Clean up Pokemon name and trash bytes
            pkm.ClearNickname();
            pkm.RefreshChecksum();

            // Vivillon form fix — force the requested form
            if (pkm.Species == 666 && set.Form != pkm.Form)
            {
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
            var trainerInfo = new PokeTradeTrainerInfo(username, 0);
            var notifier = new WebTradeNotifier<T>(tradeId);
            var detail = new PokeTradeDetail<T>(pk, trainerInfo, notifier, PokeTradeType.Specific, tradeCode, false);

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);
            var entry = new TradeEntry<T>(detail, 0, PokeRoutineType.LinkTrade, username, uniqueTradeID);

            // Add to queue
            var added = hub.Queues.Info.AddToTradeQueue(entry, 0, false, false);

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
