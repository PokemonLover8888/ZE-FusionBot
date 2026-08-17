using PKHeX.Core;
using SysBot.Pokemon.Discord.Helpers.TradeModule;
using SysBot.Pokemon.Helpers;
using System;
using System.Threading.Tasks;
using static SysBot.Pokemon.Helpers.DetailedLegalityChecker;

namespace SysBot.Pokemon.Discord;

/// <summary>
/// Queues a trade requested from the web (the browser Trade Portal, via the trade-bridge). Reuses
/// the exact legalize → queue path as /trade but with a <see cref="WebTradeNotifier{T}"/> (no Discord
/// DMs — the page shows the code + polls position). Matches the WinForms BotServer /api/trade
/// contract so the existing frontend + bridge work unchanged. Generic per game; dispatched by mode.
/// </summary>
public static class WebTradeService<T> where T : PKM, new()
{
    /// <param name="discordUserId">The signed-in user's Discord ID, or 0 to derive a stable id from the name.</param>
    /// <param name="tradeCode">The link code to use, or 0/less to generate one.</param>
    public static async Task<WebTradeResult> QueueAsync(string showdownSet, string username, ulong discordUserId, int tradeCode, bool forceShiny)
    {
        if (string.IsNullOrWhiteSpace(showdownSet))
            return WebTradeResult.Fail("No Showdown set was provided.");

        var runner = SysCord<T>.Runner;
        if (runner is null)
            return WebTradeResult.Fail("This bot is not ready yet — try again in a moment.");

        var Info = runner.Hub.Queues.Info;

        if (forceShiny && showdownSet.IndexOf("Shiny:", StringComparison.OrdinalIgnoreCase) < 0)
            showdownSet += "\nShiny: Yes";

        ulong userId = discordUserId != 0 ? discordUserId : SyntheticId(username);
        bool ignoreAutoOT = showdownSet.Contains("OT:") || showdownSet.Contains("TID:") || showdownSet.Contains("SID:");

        var processed = await Helpers<T>.ProcessShowdownSetAsync(showdownSet, ignoreAutoOT).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(processed.Error) || processed.Pokemon == null)
            return WebTradeResult.Fail(processed.Error ?? "That set could not be legalized.");

        var pk = processed.Pokemon;
        var displayName = GameInfo.Strings.Species[pk.Species];

        var commandPrefix = runner.Config.Discord.CommandPrefix;
        if (!DetailedLegalityChecker.IsLegalWithDetailedReport(pk, displayName, commandPrefix, out string? legalityError))
            return WebTradeResult.Fail("Illegal Pokémon: " + (legalityError ?? "failed the legality check."));

        int code = tradeCode > 0 ? tradeCode : Info.GetRandomTradeCode(userId);
        var trainer = new PokeTradeTrainerInfo(username, userId);
        var notifier = new WebTradeNotifier<T>();

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);

        // LGPE (PB7) link trades use a 3-Pokémon "Pictocode", NOT an 8-digit number. Derive it
        // deterministically from the last 3 digits of the numeric code (each digit 0-9 maps 1:1 to
        // the 10 Pictocodes) so the bot and the website agree on the code. Without this the bot falls
        // back to Pikachu/Pikachu/Pikachu, the member can never connect, and the page hangs at 14%.
        System.Collections.Generic.List<Pictocodes>? lgcode = null;
        if (typeof(T) == typeof(PB7))
        {
            int c = Math.Abs(code);
            lgcode = new System.Collections.Generic.List<Pictocodes>
            {
                (Pictocodes)((c / 100) % 10),
                (Pictocodes)((c / 10) % 10),
                (Pictocodes)(c % 10),
            };
        }

        var detail = new PokeTradeDetail<T>(pk, trainer, notifier, PokeTradeType.Specific, code,
            false, lgcode, 1, 1, false, false, uniqueTradeID, ignoreAutoOT: ignoreAutoOT);
        var trade = new TradeEntry<T>(detail, userId, PokeRoutineType.LinkTrade, username, uniqueTradeID);

        var added = Info.AddToTradeQueue(trade, userId, false, false);
        if (added == QueueResultAdd.AlreadyInQueue)
            return WebTradeResult.Fail("You already have a trade in this bot's queue.");
        if (added == QueueResultAdd.QueueFull)
            return WebTradeResult.Fail("This bot's queue is full — try again shortly.");

        var position = Info.CheckPosition(userId, uniqueTradeID, PokeRoutineType.LinkTrade);
        return WebTradeResult.Ok(code, uniqueTradeID, position.Position < 1 ? 1 : position.Position, displayName);
    }

    /// <summary>
    /// Queues a whole BOX of Pokémon as a single batch trade under ONE link code (the web version of
    /// the Discord $bt batch). Legalizes each set, skips any that can't be made legal, and hands the
    /// legal ones to the bot as one PokeTradeType.Batch container so the member does a single in-game
    /// trade session for all of them. Returns the shared code + how many were queued.
    /// </summary>
    public static async Task<WebTradeResult> QueueBatchAsync(System.Collections.Generic.List<string> showdownSets, string username, ulong discordUserId, int tradeCode, bool forceShiny)
    {
        if (showdownSets == null || showdownSets.Count == 0)
            return WebTradeResult.Fail("No Pokémon were provided.");

        var runner = SysCord<T>.Runner;
        if (runner is null)
            return WebTradeResult.Fail("This bot is not ready yet — try again in a moment.");

        var Info = runner.Hub.Queues.Info;
        ulong userId = discordUserId != 0 ? discordUserId : SyntheticId(username);
        var commandPrefix = runner.Config.Discord.CommandPrefix;

        var pkms = new System.Collections.Generic.List<T>();
        bool allHaveOT = true;
        foreach (var raw in showdownSets)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var set = raw;
            if (forceShiny && set.IndexOf("Shiny:", StringComparison.OrdinalIgnoreCase) < 0)
                set += "\nShiny: Yes";
            bool hasOT = set.Contains("OT:") || set.Contains("TID:") || set.Contains("SID:");
            if (!hasOT) allHaveOT = false;

            var processed = await Helpers<T>.ProcessShowdownSetAsync(set, hasOT).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(processed.Error) || processed.Pokemon == null)
                continue; // skip anything that can't be legalized (the web cart already Forge-gates these)
            var pk = processed.Pokemon;
            var name = GameInfo.Strings.Species[pk.Species];
            if (!DetailedLegalityChecker.IsLegalWithDetailedReport(pk, name, commandPrefix, out _))
                continue;
            pkms.Add(pk);
        }

        if (pkms.Count == 0)
            return WebTradeResult.Fail("None of the Pokémon could be made legal.");

        int code = tradeCode > 0 ? tradeCode : Info.GetRandomTradeCode(userId);
        var trainer = new PokeTradeTrainerInfo(username, userId);
        var notifier = new WebTradeNotifier<T>();
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);
        bool ignoreAutoOT = allHaveOT;

        var detail = new PokeTradeDetail<T>(pkms[0], trainer, notifier, PokeTradeType.Batch, code,
            false, null, 1, pkms.Count, false, ignoreAutoOT: ignoreAutoOT)
        {
            BatchTrades = pkms
        };
        var trade = new TradeEntry<T>(detail, userId, PokeRoutineType.Batch, username, uniqueTradeID);

        var added = Info.AddToTradeQueue(trade, userId, false, false);
        if (added == QueueResultAdd.AlreadyInQueue)
            return WebTradeResult.Fail("You already have a trade in this bot's queue.");
        if (added == QueueResultAdd.QueueFull)
            return WebTradeResult.Fail("This bot's queue is full — try again shortly.");

        var position = Info.CheckPosition(userId, uniqueTradeID, PokeRoutineType.Batch);
        return WebTradeResult.Ok(code, uniqueTradeID, position.Position < 1 ? 1 : position.Position, $"{pkms.Count} Pokémon");
    }

    /// <summary>
    /// Validates a Showdown set WITHOUT queuing a trade: legalizes it, runs the full legality report,
    /// and reports whether the result is HOME-ready on THIS bot (native) or needs a different game's
    /// bot. This is the live "preview" the web builder (The Forge) shows before ordering — legality
    /// intelligence no vending-machine trade site can match.
    /// </summary>
    public static async Task<WebValidateResult> ValidateAsync(string showdownSet)
    {
        if (string.IsNullOrWhiteSpace(showdownSet))
            return WebValidateResult.Fail("No Showdown set was provided.");

        var runner = SysCord<T>.Runner;
        if (runner is null)
            return WebValidateResult.Fail("This bot is not ready yet — try again in a moment.");

        bool ignoreAutoOT = showdownSet.Contains("OT:") || showdownSet.Contains("TID:") || showdownSet.Contains("SID:");
        var processed = await Helpers<T>.ProcessShowdownSetAsync(showdownSet, ignoreAutoOT).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(processed.Error) || processed.Pokemon == null)
            return new WebValidateResult { Legal = false, Issues = new[] { processed.Error ?? "That set could not be legalized." } };

        var pk = processed.Pokemon;
        var strings = GameInfo.Strings;
        var displayName = strings.Species[pk.Species];
        var commandPrefix = runner.Config.Discord.CommandPrefix;

        bool legal = DetailedLegalityChecker.IsLegalWithDetailedReport(pk, displayName, commandPrefix, out string? legalityError);
        bool native = HomeOriginAdvisor.IsNativeToBot(pk);
        string? homeAdvice = native ? null : HomeOriginAdvisor.BuildDeclineMessage(pk, displayName, ThisGameName());

        return new WebValidateResult
        {
            Legal = legal,
            HomeReady = native,
            Species = displayName,
            Shiny = pk.IsShiny,
            Ball = (pk.Ball >= 0 && pk.Ball < strings.balllist.Length) ? strings.balllist[pk.Ball] : pk.Ball.ToString(),
            Level = pk.CurrentLevel,
            Issues = legal ? Array.Empty<string>() : new[] { legalityError ?? "Failed the legality check." },
            HomeAdvice = homeAdvice,
        };
    }

    private static string ThisGameName()
    {
        var t = typeof(T);
        if (t == typeof(PK9)) return "Scarlet/Violet";
        if (t == typeof(PK8)) return "Sword/Shield";
        if (t == typeof(PB8)) return "BDSP";
        if (t == typeof(PA8)) return "Legends: Arceus";
        if (t == typeof(PA9)) return "Legends: Z-A";
        if (t == typeof(PB7)) return "Let's Go";
        return "this";
    }

    /// <summary>
    /// This bot's current trade queue as JSON, matching the WinForms BotServer /api/bot/queue/list
    /// shape { success, queueCount, queue:[{position,id,tradeCode,type,username,pokemon}] }. The
    /// trade-bridge polls this so the website can find a web trade by its code, advance the progress
    /// bar as it moves up the queue, and mark it complete once it leaves the queue. Without this the
    /// aggregated /queue is empty and every web trade sits stuck at the first step (14%).
    /// </summary>
    public static string GetQueueListJson()
    {
        var runner = SysCord<T>.Runner;
        if (runner is null)
            return "{\"success\":false,\"queueCount\":0,\"queue\":[]}";
        try
        {
            var Info = runner.Hub.Queues.Info;
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"success\":true,\"queue\":[");
            int pos = 0;
            // {0}=Trade.ID {1}=Trade.Code(link code) {2}=Type {3}=Username {4}=Species name
            foreach (var line in Info.GetUserList("{0}|{1}|{2}|{3}|{4}"))
            {
                var p = line.Split('|');
                if (p.Length < 5)
                    continue;
                if (pos > 0)
                    sb.Append(',');
                pos++;
                sb.Append("{\"position\":").Append(pos)
                  .Append(",\"id\":\"").Append(Esc(p[0])).Append('"')
                  .Append(",\"tradeCode\":\"").Append(Esc(p[1])).Append('"')
                  .Append(",\"type\":\"").Append(Esc(p[2])).Append('"')
                  .Append(",\"username\":\"").Append(Esc(p[3])).Append('"')
                  .Append(",\"pokemon\":\"").Append(Esc(p[4])).Append("\"}");
            }
            sb.Append("],\"queueCount\":").Append(pos).Append('}');
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return "{\"success\":false,\"queueCount\":0,\"queue\":[],\"error\":\"" + Esc(ex.Message) + "\"}";
        }
    }

    private static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

    // Stable non-zero id from a username (FNV-1a) so anonymous web users still get per-user
    // queue de-dup + a consistent trade-code seed when no Discord id is supplied.
    private static ulong SyntheticId(string? username)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in username ?? "web") { h ^= c; h *= 1099511628211UL; }
            // Clamp to a valid Discord snowflake (<= long.MaxValue) so the trade-start embed's
            // user lookup doesn't get a 50035 "snowflake too big". A non-existent snowflake just
            // resolves to null, so the "Up Next" embed harmlessly skips for anonymous web users.
            h &= 0x7FFFFFFFFFFFFFFFUL;
            return h == 0 ? 1UL : h;
        }
    }
}

/// <summary>Result of a web trade submission, serialized back to the browser.</summary>
public sealed class WebTradeResult
{
    public bool Success { get; init; }
    public int Code { get; init; }
    public int TradeId { get; init; }
    public int Position { get; init; }
    public string? Species { get; init; }
    public string? Error { get; init; }

    public static WebTradeResult Ok(int code, int tradeId, int position, string species)
        => new() { Success = true, Code = code, TradeId = tradeId, Position = position, Species = species };
    public static WebTradeResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>Live legality/HOME-ready preview for a Showdown set — The Forge's engine. No trade queued.</summary>
public sealed class WebValidateResult
{
    public bool Legal { get; init; }
    public bool HomeReady { get; init; }
    public string? Species { get; init; }
    public bool Shiny { get; init; }
    public string? Ball { get; init; }
    public int Level { get; init; }
    public string[] Issues { get; init; } = Array.Empty<string>();
    public string? HomeAdvice { get; init; }
    public string? Error { get; init; }

    public static WebValidateResult Fail(string error) => new() { Legal = false, Error = error };
}
