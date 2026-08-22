using Discord.WebSocket;
using SysBot.Base;

using System;

namespace SysBot.Pokemon.Discord;

public class ChannelLogger(ulong ChannelID, ISocketMessageChannel Channel, string? OwnerDataDir = null) : ILogForwarder
{
    public ulong ChannelID { get; } = ChannelID;

    // The DataFolder of the bot that owns this log channel (null for single-bot processes / manually
    // added channels). Used to isolate logs when multiple bots share one process.
    private string? OwnerDataDir { get; } = OwnerDataDir;

    public string ChannelName => Channel.Name;

    // Internal diagnostic traces that are useful in the file/console log but are pure noise in the
    // member-facing Discord log channel (they look like errors but aren't). Never forwarded.
    private static readonly string[] NoiseTags =
    {
        "[LANGUAGE TRACE]", "DIAG mythical-gate", "Z-A native", "Pre-made bypass",
        "Requested nature", "REVERTING to almGeneratedLanguage", "PrepareForTrade",
        "Fixed-OT check", "[EGG DEBUG]", "[PKHeX Moves]",
    };

    private static bool IsNoise(string message, string identity)
    {
        if (string.Equals(identity, "NatureLegality", StringComparison.Ordinal))
            return true;
        foreach (var tag in NoiseTags)
        {
            if (message.Contains(tag, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public void Forward(string message, string identity)
    {
        // Multi-tenant isolation: bots sharing one process share the global LogUtil.Forwarders, so a
        // log fired from bot A's routine must NOT fan out to bot B's log channel. Each bot sets an
        // AsyncLocal (its DataFolder) at MainLoop start. Only skip when we KNOW this channel's owner
        // AND a current bot context exists AND they differ. If either is unset (a single-bot process,
        // or a log fired outside any bot's async flow) we keep the old behavior and forward it.
        var current = SysBot.Pokemon.TradeCodeStorage.CurrentDataDir;
        if (!string.IsNullOrEmpty(OwnerDataDir) && !string.IsNullOrEmpty(current)
            && !string.Equals(current, OwnerDataDir, StringComparison.OrdinalIgnoreCase))
            return;

        if (IsNoise(message, identity))
            return; // keep internal debug traces out of the Discord log channel
        try
        {
            var text = GetMessage(message, identity);
            Channel.SendMessageAsync(text);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, identity);
        }
    }

    private static string GetMessage(ReadOnlySpan<char> msg, string identity)
        => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";
}
