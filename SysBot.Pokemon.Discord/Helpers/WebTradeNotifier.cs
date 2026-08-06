using PKHeX.Core;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

/// <summary>
/// Notifier for web-initiated trades (the browser Trade Portal). The web page shows the link code
/// and polls the queue for position, so this notifier stays intentionally minimal — no Discord DMs
/// or channel embeds. The trade still queues and executes exactly like a Discord trade; only the
/// user-facing notifications live on the web instead.
/// </summary>
public sealed class WebTradeNotifier<T> : IPokeTradeNotifier<T> where T : PKM, new()
{
    // The bot registers a cleanup callback here and expects it fired when the trade ends —
    // must invoke it in TradeFinished/TradeCanceled or the routine won't advance.
    public Action<PokeRoutineExecutor<T>>? OnFinish { private get; set; }

    public Task SendInitialQueueUpdate() => Task.CompletedTask;
    public void UpdateBatchProgress(int currentBatchNumber, T currentPokemon, int uniqueTradeID) { }
    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message) { }
    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message) { }
    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message) { }
    public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg) => OnFinish?.Invoke(routine);
    public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result) => OnFinish?.Invoke(routine);
    public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info) { }
    public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info) { }
}
