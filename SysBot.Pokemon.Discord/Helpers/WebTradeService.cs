using PKHeX.Core;
using SysBot.Pokemon.Discord.Helpers.TradeModule;
using SysBot.Pokemon.Helpers;
using System;
using System.Threading.Tasks;
using static SysBot.Pokemon.Helpers.DetailedLegalityChecker;

namespace SysBot.Pokemon.Discord;

/// <summary>
/// Queues a trade requested from the web Trade Portal. Reuses the exact same legalize → queue path
/// as the /trade slash command, but with a <see cref="WebTradeNotifier{T}"/> (no Discord DMs — the
/// browser shows the code + polls position). Returns the link code + queue position for the page.
/// Generic per game; dispatched by <c>ProgramMode</c> at the host's web endpoint.
/// </summary>
public static class WebTradeService<T> where T : PKM, new()
{
    public static async Task<WebTradeResult> QueueAsync(string showdownSet, ulong userId, string username)
    {
        if (string.IsNullOrWhiteSpace(showdownSet))
            return WebTradeResult.Fail("No Showdown set was provided.");

        var runner = SysCord<T>.Runner;
        if (runner is null)
            return WebTradeResult.Fail("This bot is not ready yet — try again in a moment.");

        var Info = runner.Hub.Queues.Info;

        // Same AutoOT rule as the batch/text/slash paths.
        bool ignoreAutoOT = showdownSet.Contains("OT:") || showdownSet.Contains("TID:") || showdownSet.Contains("SID:");

        var processed = await Helpers<T>.ProcessShowdownSetAsync(showdownSet, ignoreAutoOT).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(processed.Error) || processed.Pokemon == null)
            return WebTradeResult.Fail(processed.Error ?? "That set could not be legalized.");

        var pk = processed.Pokemon;
        var displayName = GameInfo.Strings.Species[pk.Species];

        var commandPrefix = runner.Config.Discord.CommandPrefix;
        if (!DetailedLegalityChecker.IsLegalWithDetailedReport(pk, displayName, commandPrefix, out string? legalityError))
            return WebTradeResult.Fail("Illegal Pokémon: " + (legalityError ?? "failed the legality check."));

        var code = Info.GetRandomTradeCode(userId);
        var trainer = new PokeTradeTrainerInfo(username, userId);
        var notifier = new WebTradeNotifier<T>();

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);

        var detail = new PokeTradeDetail<T>(pk, trainer, notifier, PokeTradeType.Specific, code,
            false, null, 1, 1, false, false, uniqueTradeID, ignoreAutoOT: ignoreAutoOT);
        var trade = new TradeEntry<T>(detail, userId, PokeRoutineType.LinkTrade, username, uniqueTradeID);

        var added = Info.AddToTradeQueue(trade, userId, false, false);
        if (added == QueueResultAdd.AlreadyInQueue)
            return WebTradeResult.Fail("You already have a trade in this bot's queue.");
        if (added == QueueResultAdd.QueueFull)
            return WebTradeResult.Fail("This bot's queue is full — try again shortly.");

        var position = Info.CheckPosition(userId, uniqueTradeID, PokeRoutineType.LinkTrade);
        return WebTradeResult.Ok(code, uniqueTradeID, position.Position < 1 ? 1 : position.Position, displayName);
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
