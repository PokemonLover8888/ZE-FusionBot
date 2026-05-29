using System;

namespace SysBot.Pokemon;

/// <summary>
/// Cross-assembly health signal store. SysCord (Discord layer) updates the
/// Discord connection fields when its gateway state changes; BotServer
/// (WebApi layer) reads them to expose <c>discordConnected</c> /
/// <c>discordLatencyMs</c> on <c>/api/bot/instances</c>.
///
/// This intentionally lives in SysBot.Pokemon (the common project) so both
/// the Discord and WinForms projects reference it without circular deps.
/// </summary>
public static class BotHealthReporter
{
    private static volatile bool _discordConnected;
    private static volatile int _discordLatencyMs = -1;
    private static DateTime _lastDiscordEvent = DateTime.MinValue;

    /// <summary>True iff Discord gateway is logged in AND connected.</summary>
    public static bool DiscordConnected => _discordConnected;

    /// <summary>Last reported Discord heartbeat latency, or -1 if no session.</summary>
    public static int DiscordLatencyMs => _discordConnected ? _discordLatencyMs : -1;

    /// <summary>Time of the last connect/disconnect event we received.</summary>
    public static DateTime LastDiscordEvent => _lastDiscordEvent;

    /// <summary>Called by SysCord on Ready / Disconnected / latency updates.</summary>
    public static void ReportDiscord(bool connected, int latencyMs = -1)
    {
        _discordConnected = connected;
        _discordLatencyMs = latencyMs;
        _lastDiscordEvent = DateTime.UtcNow;
    }
}
