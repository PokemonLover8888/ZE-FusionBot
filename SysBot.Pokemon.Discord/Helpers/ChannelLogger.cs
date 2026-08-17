using Discord.WebSocket;
using SysBot.Base;

using System;

namespace SysBot.Pokemon.Discord;

public class ChannelLogger(ulong ChannelID, ISocketMessageChannel Channel) : ILogForwarder
{
    public ulong ChannelID { get; } = ChannelID;

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
