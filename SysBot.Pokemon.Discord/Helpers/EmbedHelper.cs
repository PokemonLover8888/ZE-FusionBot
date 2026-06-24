using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using PKHeX.Core;
using SysBot.Base;

namespace SysBot.Pokemon.Discord;

public static class EmbedHelper
{
    private static readonly SemaphoreSlim _dmRateLimiter = new SemaphoreSlim(1, 1);

    private static readonly ConcurrentDictionary<ulong, IDMChannel> _dmChannels = new ConcurrentDictionary<ulong, IDMChannel>();

    private static DateTime _lastDmTime = DateTime.MinValue;

    private const int MinDmDelayMs = 2000;

    private static readonly Color ColorTradeCode = new Color(139, 92, 246);

    private static readonly Color ColorInitializing = new Color(59, 130, 246);

    private static readonly Color ColorSearching = new Color(6, 182, 212);

    private static readonly Color ColorCompleted = new Color(16, 185, 129);

    private static readonly Color ColorCanceled = new Color(239, 68, 68);

    private static readonly Color ColorNotice = new Color(245, 158, 11);

    private static readonly Dictionary<int, (string Emoji, string Name)> TypeInfo = new Dictionary<int, (string, string)>
    {
        { 0, ("\ud83d\udfe4", "Normal") },       // 🟤
        { 1, ("\ud83e\udd4a", "Fighting") },      // 🥊
        { 2, ("\ud83d\udca8", "Flying") },         // 💨
        { 3, ("\u2620\ufe0f", "Poison") },         // ☠️
        { 4, ("\ud83c\udfdc\ufe0f", "Ground") },  // 🏜️
        { 5, ("\ud83e\udea8", "Rock") },           // 🪨
        { 6, ("\ud83d\udc1b", "Bug") },            // 🐛
        { 7, ("\ud83d\udc7b", "Ghost") },          // 👻
        { 8, ("\u2699\ufe0f", "Steel") },          // ⚙️
        { 9, ("\ud83d\udd25", "Fire") },           // 🔥
        { 10, ("\ud83d\udca7", "Water") },         // 💧
        { 11, ("\ud83c\udf3f", "Grass") },         // 🌿
        { 12, ("\u26a1", "Electric") },            // ⚡
        { 13, ("\ud83d\udd2e", "Psychic") },       // 🔮
        { 14, ("\u2744\ufe0f", "Ice") },           // ❄️
        { 15, ("\ud83d\udc09", "Dragon") },        // 🐉
        { 16, ("\ud83c\udf11", "Dark") },          // 🌑
        { 17, ("\ud83e\uddda", "Fairy") }          // 🧚
    };

    private const string HomeBase = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/";

    private const string FloatBase = "https://bots.pkm-universe.com/sprites/";

    private static string GetProgressBar(int currentStep)
    {
        int filled = currentStep * 4;
        string filledChar = "\ud83d\udfe9"; // 🟩
        string emptyChar = "\u2b1c";         // ⬜
        string bar = string.Concat(Enumerable.Repeat(filledChar, filled)) + string.Concat(Enumerable.Repeat(emptyChar, 16 - filled));
        string[] stages = new string[4] { "Queued", "Preparing", "Searching", "Complete" };
        var sb = new StringBuilder();
        for (int i = 0; i < stages.Length; i++)
        {
            bool isCurrent = i + 1 == currentStep;
            bool isPast = i + 1 < currentStep;
            bool isFinalComplete = i + 1 == 4 && currentStep == 4;
            string icon = isPast ? "\u2705" : (!isCurrent ? "\u26aa" : (isFinalComplete ? "\ud83c\udf89" : "\ud83d\udd35"));
            string label = isCurrent ? ($"**{stages[i]}{(isFinalComplete ? "!" : "")}**") : stages[i];
            sb.Append($"{icon} {label}");
            if (i < stages.Length - 1)
            {
                sb.Append("  >  ");
            }
        }
        return $"{bar}\n{sb}";
    }

    private static string GetTypeDisplay(int type1, int type2)
    {
        if (!TypeInfo.TryGetValue(type1, out var info1))
            return "";
        string text = $"{info1.Emoji} {info1.Name}";
        if (type2 != type1 && TypeInfo.TryGetValue(type2, out var info2))
        {
            text = $"{text} / {info2.Emoji} {info2.Name}";
        }
        return text;
    }

    private static string GetTimeGreeting()
    {
        return DateTime.Now.Hour switch
        {
            >= 5 and <= 11 => "Good morning",
            >= 12 and <= 16 => "Good afternoon",
            >= 17 and <= 20 => "Good evening",
            _ => "Good night",
        };
    }

    private static string GetRarityBadge(TradeEmbedData data)
    {
        if (data.IsMythical)
            return "\ud83c\udf1f Mythical";
        if (data.IsLegendary)
            return "\ud83d\udc51 Legendary";
        if (data.IsSubLegendary)
            return "\u2b50 Sub-Legendary";
        if (data.IsParadox)
            return "\ud83c\udf00 Paradox";
        if (data.IsAlpha)
            return "\ud83d\udd34 Alpha";
        return "";
    }

    private static string GetGenderIcon(int gender)
    {
        return gender switch
        {
            0 => " \u2642\ufe0f",
            1 => " \u2640\ufe0f",
            _ => "",
        };
    }

    private static (string Title, string Description) GetCelebrationContent(TradeEmbedData data)
    {
        bool isLegendaryOrMythical = data.IsLegendary || data.IsMythical;
        if (data.IsShiny && isLegendaryOrMythical)
        {
            return ("\ud83c\udf1f\u2728  ULTRA RARE Trade Complete!", $"An incredibly rare **Shiny {data.DisplayName}**! You're truly blessed, Trainer!");
        }
        if (data.IsShiny)
        {
            return ("\u2728\ud83c\udf1f  Shiny Trade Complete!", $"A beautiful **Shiny {data.DisplayName}**! It sparkles with pride!");
        }
        if (isLegendaryOrMythical)
        {
            return ("\ud83d\udc51\u2728  Legendary Trade Complete!", $"The mighty **{data.DisplayName}** has joined your team!");
        }
        return ("\u2728  Trade Complete!", $"Enjoy your new **{data.DisplayName}**, Trainer!");
    }

    private static string FormatIVSummary(TradeEmbedData data)
    {
        if (data.UsesAVs)
            return "";
        int[] ivs = data.IVs;
        int[] ordered = new int[6] { ivs[0], ivs[1], ivs[2], ivs[4], ivs[5], ivs[3] };
        if (ordered.Count(v => v == 31) == 6)
            return "**IVs:** 6IV (Perfect)";
        string[] labels = new string[6] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" };
        return "**IVs:** " + string.Join(" / ", ordered.Select((v, i) => $"{v} {labels[i]}"));
    }

    private static string FormatEVSummary(TradeEmbedData data)
    {
        if (data.UsesAVs)
            return "";
        int[] evs = data.EVs;
        int[] ordered = new int[6] { evs[0], evs[1], evs[2], evs[4], evs[5], evs[3] };
        string[] labels = new string[6] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" };
        var nonZero = ordered.Select((v, i) => v > 0 ? $"{v} {labels[i]}" : "").Where(s => s != "");
        if (!nonZero.Any())
            return "";
        return "**EVs:** " + string.Join(" / ", nonZero);
    }

    private static string FormatMovesList(TradeEmbedData data)
    {
        var moves = data.MoveNames.Where(m => !string.IsNullOrEmpty(m));
        if (!moves.Any())
            return "None";
        return string.Join(", ", moves);
    }

    private static ComponentBuilder? BuildButtons(string phase, ulong userId, int uniqueTradeId)
    {
        var builder = new ComponentBuilder();
        switch (phase)
        {
            case "code":
                builder.WithButton("Dismiss", $"trade_close:{userId}", ButtonStyle.Secondary, new Emoji("\ud83d\uddd1\ufe0f"));
                break;
            case "preparing":
            case "searching":
                return null;
            case "complete":
                builder.WithButton("Trade Again", $"trade_again:{userId}:{uniqueTradeId}", ButtonStyle.Success, new Emoji("\ud83d\udd01"));
                builder.WithButton("Dismiss", $"trade_close:{userId}", ButtonStyle.Secondary, new Emoji("\ud83d\uddd1\ufe0f"));
                break;
            case "canceled":
                builder.WithButton("Re-queue", $"trade_requeue:{userId}:{uniqueTradeId}", ButtonStyle.Primary, new Emoji("\ud83d\udd04"));
                builder.WithButton("Dismiss", $"trade_close:{userId}", ButtonStyle.Secondary, new Emoji("\ud83d\uddd1\ufe0f"));
                break;
            case "notification":
                builder.WithButton("Dismiss", $"trade_close:{userId}", ButtonStyle.Secondary, new Emoji("\ud83d\uddd1\ufe0f"));
                break;
            default:
                return null;
        }
        return builder;
    }

    public static async Task RemoveButtonsFromMessageAsync(IUser user, ulong messageId)
    {
        if (messageId == 0L)
            return;
        try
        {
            var dmChannel = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dmChannel == null)
                return;
            var msg = await dmChannel.GetMessageAsync(messageId).ConfigureAwait(false);
            if (msg is IUserMessage userMsg)
            {
                await userMsg.ModifyAsync(m =>
                {
                    m.Components = new ComponentBuilder().Build();
                }).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    private static string GetBatchInfo(TradeEmbedData data)
    {
        if (data.TotalBatchTrades <= 1)
            return "";
        return $"\ud83d\udce6 **Batch Trade:** {data.BatchTradeNumber} of {data.TotalBatchTrades}";
    }

    private static string GetGameName(string gameType)
    {
        return gameType switch
        {
            "PK9" => "Scarlet / Violet",
            "PA9" => "Legends: Z-A",
            "PA8" => "Legends: Arceus",
            "PB7" => "Let's Go Pikachu / Eevee",
            "PK8" => "Sword / Shield",
            "PB8" => "Brilliant Diamond / Shining Pearl",
            _ => "",
        };
    }

    private static string GetTip(string phase)
    {
        return phase switch
        {
            "code" => "\ud83d\udca1 Tip: Make sure you're connected to the internet before entering the code!",
            "preparing" => "\ud83d\udca1 Tip: Don't leave the trade screen or your trade will be canceled!",
            "searching" => "\ud83d\udca1 Tip: Make sure your in-game name matches your request!",
            "complete" => "",
            "canceled" => "\ud83d\udca1 Tip: Make sure you enter the Link Code quickly after it's sent!",
            "notification" => "\ud83d\udca1 Tip: You can rejoin the queue by requesting a new trade!",
            _ => "",
        };
    }

    private static string GetPhaseAuthorIcon(string phase)
    {
        return phase switch
        {
            "code" => "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/494.png",
            "preparing" => "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/720.png",
            "searching" => "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/251.png",
            "complete" => "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/494.png",
            "canceled" => "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/647.png",
            "notification" => "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/385.png",
            _ => "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/385.png",
        };
    }

    private static string GetFallbackThumbnail(string phase)
    {
        return phase switch
        {
            "code" => "https://bots.pkm-universe.com/sprites/victini_float.gif",
            "preparing" => "https://bots.pkm-universe.com/sprites/hoopa_float.gif",
            "searching" => "https://bots.pkm-universe.com/sprites/celebi_float.gif",
            "complete" => "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/6.png",
            "canceled" => "https://bots.pkm-universe.com/sprites/keldeo_float.gif",
            "notification" => "https://bots.pkm-universe.com/sprites/jirachi_float.gif",
            _ => "https://bots.pkm-universe.com/sprites/jirachi_float.gif",
        };
    }

    private static async Task<IDMChannel?> GetOrCreateDMAsync(IUser user)
    {
        try
        {
            if (_dmChannels.TryGetValue(user.Id, out IDMChannel? value))
                return value;

            TimeSpan elapsed = DateTime.Now - _lastDmTime;
            if (elapsed.TotalMilliseconds < 2000.0)
                await Task.Delay(2000 - (int)elapsed.TotalMilliseconds).ConfigureAwait(false);

            var dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
            _dmChannels[user.Id] = dmChannel;
            _lastDmTime = DateTime.Now;
            return dmChannel;
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && (int)ex.DiscordCode.Value == 40003)
        {
            LogUtil.LogError($"Opening DMs too fast when creating DM channel for user {user.Username} ({user.Id}). Waiting 5 seconds...", "GetOrCreateDMAsync");
            await Task.Delay(5000).ConfigureAwait(false);
            try
            {
                var dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
                _dmChannels[user.Id] = dmChannel;
                _lastDmTime = DateTime.Now;
                return dmChannel;
            }
            catch (Exception ex2)
            {
                LogUtil.LogError("Failed to create DM channel after retry: " + ex2.Message, "GetOrCreateDMAsync");
                return null;
            }
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client is disposed. Cannot create DM channel.", "GetOrCreateDMAsync");
            return null;
        }
        catch (Exception ex4)
        {
            LogUtil.LogError("Failed to create DM channel: " + ex4.Message, "GetOrCreateDMAsync");
            return null;
        }
    }

    public static async Task<ulong> SendTradeCodeEmbedAsync(IUser user, int code, TradeEmbedData data)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dmChannel = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dmChannel == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade code message.", "SendTradeCodeEmbedAsync");
                return 0uL;
            }

            var sb = new StringBuilder();
            sb.AppendLine(GetProgressBar(1));
            sb.AppendLine();
            sb.AppendLine($"{GetTimeGreeting()}, Trainer!");
            sb.AppendLine($"# {code:0000 0000}");

            if (!data.IsMysteryEgg && data.SpeciesId > 0)
            {
                string pokemon = data.DisplayName + GetGenderIcon(data.Gender);
                sb.AppendLine($"\n**Pokemon:** {pokemon}");

                string typeDisplay = GetTypeDisplay(data.Type1, data.Type2);
                if (!string.IsNullOrEmpty(typeDisplay))
                    sb.AppendLine($"**Type:** {typeDisplay}");

                string rarityBadge = GetRarityBadge(data);
                if (!string.IsNullOrEmpty(rarityBadge))
                    sb.AppendLine($"**Rarity:** {rarityBadge}");
            }
            else if (data.IsMysteryEgg)
            {
                sb.AppendLine("\n**Pokemon:** \ud83e\udd5a Mystery Egg");
            }

            string gameName = GetGameName(data.GameType);
            if (!string.IsNullOrEmpty(gameName))
                sb.AppendLine($"**Game:** {gameName}");

            if (!string.IsNullOrEmpty(data.BotName))
                sb.AppendLine($"**Handled By:** {data.BotName}");

            string batchInfo = GetBatchInfo(data);
            if (!string.IsNullOrEmpty(batchInfo))
                sb.AppendLine($"\n{batchInfo}");

            string footer = "PKM-Universe Trade System";
            string tip = GetTip("code");
            if (!string.IsNullOrEmpty(tip))
                footer = footer + "\n" + tip;

            var embed = new EmbedBuilder()
                .WithAuthor("PKM-Universe", GetPhaseAuthorIcon("code"))
                .WithTitle("\ud83d\udd10  Your Link Code")
                .WithDescription(sb.ToString())
                .WithTimestamp(DateTimeOffset.Now)
                .WithColor(ColorTradeCode)
                .WithFooter(footer);
            embed.WithThumbnailUrl(GetFallbackThumbnail("code"));

            var buttons = BuildButtons("code", data.UserId, data.UniqueTradeId);
            var msg = await dmChannel.SendMessageAsync(
                embed: embed.Build(),
                components: buttons?.Build()
            ).ConfigureAwait(false);

            _lastDmTime = DateTime.Now;
            return msg.Id;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade code embed.", "SendTradeCodeEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && (int)ex.DiscordCode.Value == 40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeCodeEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex3)
        {
            LogUtil.LogError("Error sending trade code embed: " + ex3.Message, "SendTradeCodeEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
        return 0uL;
    }

    public static async Task<ulong> SendTradeInitializingEmbedAsync(IUser user, TradeEmbedData data, string? message = null)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dmChannel = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dmChannel == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade initializing message.", "SendTradeInitializingEmbedAsync");
                return 0uL;
            }

            var sb = new StringBuilder();
            sb.AppendLine(GetProgressBar(2));
            sb.AppendLine();

            string pokemon = data.IsMysteryEgg ? "\ud83e\udd5a **Mystery Egg**" : ($"**{data.DisplayName}**{GetGenderIcon(data.Gender)}");
            sb.AppendLine($"**Pokemon:** {pokemon}");
            sb.AppendLine($"**Trade Code:** {data.TradeCode:0000 0000}");

            if (!data.IsMysteryEgg && data.SpeciesId > 0)
            {
                string typeDisplay = GetTypeDisplay(data.Type1, data.Type2);
                if (!string.IsNullOrEmpty(typeDisplay))
                    sb.AppendLine($"**Type:** {typeDisplay}");

                string rarityBadge = GetRarityBadge(data);
                if (!string.IsNullOrEmpty(rarityBadge))
                    sb.AppendLine($"**Rarity:** {rarityBadge}");
            }

            string gameName = GetGameName(data.GameType);
            if (!string.IsNullOrEmpty(gameName))
                sb.AppendLine($"**Game:** {gameName}");

            if (!string.IsNullOrEmpty(data.BotName))
                sb.AppendLine($"**Handled By:** {data.BotName}");

            sb.AppendLine("\nOpening the trade menu now!");

            string batchInfo = GetBatchInfo(data);
            if (!string.IsNullOrEmpty(batchInfo))
                sb.AppendLine($"\n{batchInfo}");

            if (!string.IsNullOrEmpty(message))
                sb.AppendLine($"\n{message}");

            string footer = "PKM-Universe Trade System";
            string tip = GetTip("preparing");
            if (!string.IsNullOrEmpty(tip))
                footer = footer + "\n" + tip;

            var embed = new EmbedBuilder()
                .WithAuthor("PKM-Universe", GetPhaseAuthorIcon("preparing"))
                .WithTitle("\ud83d\udce1  Preparing Your Trade...")
                .WithDescription(sb.ToString())
                .WithTimestamp(DateTimeOffset.Now)
                .WithColor(ColorInitializing)
                .WithFooter(footer);
            embed.WithThumbnailUrl(GetFallbackThumbnail("preparing"));

            var buttons = BuildButtons("preparing", data.UserId, data.UniqueTradeId);
            var msg = await dmChannel.SendMessageAsync(
                embed: embed.Build(),
                components: buttons?.Build()
            ).ConfigureAwait(false);

            _lastDmTime = DateTime.Now;
            return msg.Id;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade initializing embed.", "SendTradeInitializingEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && (int)ex.DiscordCode.Value == 40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeInitializingEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex3)
        {
            LogUtil.LogError("Error sending trade initializing embed: " + ex3.Message, "SendTradeInitializingEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
        return 0uL;
    }

    public static async Task<ulong> SendTradeSearchingEmbedAsync(IUser user, string trainerName, string inGameName, TradeEmbedData data, string? message = null)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dmChannel = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dmChannel == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade searching message.", "SendTradeSearchingEmbedAsync");
                return 0uL;
            }

            var sb = new StringBuilder();
            sb.AppendLine(GetProgressBar(3));
            sb.AppendLine();
            sb.AppendLine($"{GetTimeGreeting()}, Trainer!");
            sb.AppendLine();
            sb.AppendLine($"**Waiting For:** {trainerName}");
            sb.AppendLine($"**My IGN:** {inGameName}");

            string gameName = GetGameName(data.GameType);
            if (!string.IsNullOrEmpty(gameName))
                sb.AppendLine($"**Game:** {gameName}");

            if (!string.IsNullOrEmpty(data.BotName))
                sb.AppendLine($"**Handled By:** {data.BotName}");

            sb.AppendLine("\n**Enter your Link Code now!**");

            string batchInfo = GetBatchInfo(data);
            if (!string.IsNullOrEmpty(batchInfo))
                sb.AppendLine($"\n{batchInfo}");

            if (!string.IsNullOrEmpty(message))
                sb.AppendLine($"\n{message}");

            string footer = "PKM-Universe Trade System";
            string tip = GetTip("searching");
            if (!string.IsNullOrEmpty(tip))
                footer = footer + "\n" + tip;

            var embed = new EmbedBuilder()
                .WithAuthor("PKM-Universe", GetPhaseAuthorIcon("searching"))
                .WithTitle("\ud83d\udd0d  Now Searching!")
                .WithDescription(sb.ToString())
                .WithTimestamp(DateTimeOffset.Now)
                .WithColor(ColorSearching)
                .WithFooter(footer);
            embed.WithThumbnailUrl(GetFallbackThumbnail("searching"));

            var buttons = BuildButtons("searching", data.UserId, data.UniqueTradeId);
            var msg = await dmChannel.SendMessageAsync(
                embed: embed.Build(),
                components: buttons?.Build()
            ).ConfigureAwait(false);

            _lastDmTime = DateTime.Now;
            return msg.Id;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade searching embed.", "SendTradeSearchingEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && (int)ex.DiscordCode.Value == 40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeSearchingEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex3)
        {
            LogUtil.LogError("Error sending trade searching embed: " + ex3.Message, "SendTradeSearchingEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
        return 0uL;
    }

    public static async Task SendTradeFinishedEmbedAsync(IUser user, TradeEmbedData data)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dmChannel = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dmChannel == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade finished message.", "SendTradeFinishedEmbedAsync");
                return;
            }

            var (title, description) = GetCelebrationContent(data);

            var sb = new StringBuilder();
            sb.AppendLine(GetProgressBar(4));
            sb.AppendLine();
            sb.AppendLine(description);

            if (!data.IsMysteryEgg && data.SpeciesId > 0)
            {
                sb.AppendLine();
                string typeDisplay = GetTypeDisplay(data.Type1, data.Type2);
                if (!string.IsNullOrEmpty(typeDisplay))
                    sb.AppendLine($"**Type:** {typeDisplay}");

                string rarityBadge = GetRarityBadge(data);
                if (!string.IsNullOrEmpty(rarityBadge))
                    sb.AppendLine($"**Rarity:** {rarityBadge}");
            }

            string gameName = GetGameName(data.GameType);
            if (!string.IsNullOrEmpty(gameName))
                sb.AppendLine($"**Game:** {gameName}");

            if (!string.IsNullOrEmpty(data.BotName))
                sb.AppendLine($"**Handled By:** {data.BotName}");

            if (!string.IsNullOrEmpty(data.HeldItem))
                sb.AppendLine($"**Held Item:** {data.HeldItem}");

            string batchInfo = GetBatchInfo(data);
            if (!string.IsNullOrEmpty(batchInfo))
                sb.AppendLine($"\n{batchInfo}");

            string footer = "Thank you for using PKM-Universe!";
            if (data.TotalBatchTrades > 1)
                footer += $"\nTrade {data.BatchTradeNumber} of {data.TotalBatchTrades}";

            string authorIcon = !string.IsNullOrEmpty(data.ThumbnailUrl) ? data.ThumbnailUrl : GetPhaseAuthorIcon("complete");

            var embed = new EmbedBuilder()
                .WithAuthor("PKM-Universe", authorIcon)
                .WithTitle(title)
                .WithDescription(sb.ToString())
                .WithTimestamp(DateTimeOffset.Now)
                .WithColor(ColorCompleted)
                .WithFooter(footer);

            if (!string.IsNullOrEmpty(data.ThumbnailUrl))
                embed.WithImageUrl(data.ThumbnailUrl);

            if (!data.IsMysteryEgg && data.SpeciesId > 0)
            {
                var detailsSb = new StringBuilder();

                if (!string.IsNullOrEmpty(data.OriginalTrainer))
                    detailsSb.AppendLine($"**OT:** {data.OriginalTrainer}");

                detailsSb.AppendLine($"**Nature:** {data.NatureName}");

                if (!string.IsNullOrEmpty(data.AbilityName))
                    detailsSb.AppendLine($"**Ability:** {data.AbilityName}");

                detailsSb.AppendLine($"**Level:** {data.Level}");

                string ivSummary = FormatIVSummary(data);
                if (!string.IsNullOrEmpty(ivSummary))
                    detailsSb.AppendLine(ivSummary);

                string evSummary = FormatEVSummary(data);
                if (!string.IsNullOrEmpty(evSummary))
                    detailsSb.AppendLine(evSummary);

                embed.AddField($"**{data.DisplayName}{GetGenderIcon(data.Gender)}**{(data.IsShiny ? " \u2728" : "")}", detailsSb.ToString(), true);
                embed.AddField("\u200b", "\u200b", true);

                string movesList = FormatMovesList(data);
                embed.AddField("**Moves**", movesList, true);
            }

            var buttons = BuildButtons("complete", data.UserId, data.UniqueTradeId);
            await dmChannel.SendMessageAsync(
                embed: embed.Build(),
                components: buttons?.Build()
            ).ConfigureAwait(false);

            _lastDmTime = DateTime.Now;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade finished embed.", "SendTradeFinishedEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && (int)ex.DiscordCode.Value == 40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeFinishedEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex3)
        {
            LogUtil.LogError("Error sending trade finished embed: " + ex3.Message, "SendTradeFinishedEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }

    public static async Task SendTradeCanceledEmbedAsync(IUser user, string reason, TradeEmbedData? data)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dmChannel = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dmChannel == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade canceled message.", "SendTradeCanceledEmbedAsync");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Your trade could not be completed.");
            sb.AppendLine($"\n**Reason:** {reason}");

            if (data != null && !data.IsMysteryEgg && data.SpeciesId > 0)
            {
                sb.AppendLine($"\n**Pokemon:** {data.DisplayName}{GetGenderIcon(data.Gender)}");

                string typeDisplay = GetTypeDisplay(data.Type1, data.Type2);
                if (!string.IsNullOrEmpty(typeDisplay))
                    sb.AppendLine($"**Type:** {typeDisplay}");
            }

            if (data != null)
            {
                string gameName = GetGameName(data.GameType);
                if (!string.IsNullOrEmpty(gameName))
                    sb.AppendLine($"**Game:** {gameName}");

                if (!string.IsNullOrEmpty(data.BotName))
                    sb.AppendLine($"**Handled By:** {data.BotName}");

                string batchInfo = GetBatchInfo(data);
                if (!string.IsNullOrEmpty(batchInfo))
                    sb.AppendLine($"\n{batchInfo}");
            }

            string footer = "PKM-Universe Trade System";
            string tip = GetTip("canceled");
            if (!string.IsNullOrEmpty(tip))
                footer = footer + "\n" + tip;

            var embed = new EmbedBuilder()
                .WithAuthor("PKM-Universe", GetPhaseAuthorIcon("canceled"))
                .WithTitle("\u274c  Trade Canceled")
                .WithDescription(sb.ToString())
                .WithTimestamp(DateTimeOffset.Now)
                .WithColor(ColorCanceled)
                .WithFooter(footer);
            embed.WithThumbnailUrl(GetFallbackThumbnail("canceled"));

            ComponentBuilder? buttons = null;
            if (data != null)
                buttons = BuildButtons("canceled", data.UserId, data.UniqueTradeId);

            await dmChannel.SendMessageAsync(
                embed: embed.Build(),
                components: buttons?.Build()
            ).ConfigureAwait(false);

            _lastDmTime = DateTime.Now;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade canceled embed.", "SendTradeCanceledEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && (int)ex.DiscordCode.Value == 40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeCanceledEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex3)
        {
            LogUtil.LogError("Error sending trade canceled embed: " + ex3.Message, "SendTradeCanceledEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }

    public static async Task SendNotificationEmbedAsync(IUser user, string message, TradeEmbedData? data = null)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dmChannel = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dmChannel == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping notification.", "SendNotificationEmbedAsync");
                return;
            }

            string footer = "PKM-Universe Trade System";
            string tip = GetTip("notification");
            if (!string.IsNullOrEmpty(tip))
                footer = footer + "\n" + tip;

            var embedBuilder = new EmbedBuilder()
                .WithAuthor("PKM-Universe", GetPhaseAuthorIcon("notification"))
                .WithTitle("\u26a0\ufe0f  Heads Up!")
                .WithDescription(message)
                .WithTimestamp(DateTimeOffset.Now)
                .WithColor(ColorNotice)
                .WithFooter(footer);
            embedBuilder.WithThumbnailUrl(GetFallbackThumbnail("notification"));

            var embed = embedBuilder.Build();

            ComponentBuilder? buttons = null;
            if (data != null)
                buttons = BuildButtons("notification", data.UserId, data.UniqueTradeId);

            await dmChannel.SendMessageAsync(
                embed: embed,
                components: buttons?.Build()
            ).ConfigureAwait(false);

            _lastDmTime = DateTime.Now;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending notification embed.", "SendNotificationEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && (int)ex.DiscordCode.Value == 40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendNotificationEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex3)
        {
            LogUtil.LogError("Error sending notification embed: " + ex3.Message, "SendNotificationEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }

    // Legacy overloads for backward compatibility

    public static Task SendTradeCodeEmbedAsync(IUser user, int code)
    {
        var data = new TradeEmbedData
        {
            TradeCode = code,
            UserId = user.Id
        };
        return SendTradeCodeEmbedAsync(user, code, data);
    }

    public static Task SendTradeInitializingEmbedAsync(IUser user, string speciesName, int code, bool isMysteryEgg, string? message = null)
    {
        var data = new TradeEmbedData
        {
            SpeciesName = speciesName,
            TradeCode = code,
            IsMysteryEgg = isMysteryEgg,
            UserId = user.Id
        };
        return SendTradeInitializingEmbedAsync(user, data, message);
    }

    public static Task SendTradeSearchingEmbedAsync(IUser user, string trainerName, string inGameName, string? message = null)
    {
        var data = new TradeEmbedData
        {
            UserId = user.Id
        };
        return SendTradeSearchingEmbedAsync(user, trainerName, inGameName, data, message);
    }

    public static async Task SendTradeFinishedEmbedAsync<T>(IUser user, string message, T pk, bool isMysteryEgg) where T : PKM, new()
    {
        try
        {
            var data = TradeEmbedDataBuilder.Build(pk, 0, user.Id, 0, 1, 1, isMysteryEgg);
            await SendTradeFinishedEmbedAsync(user, data).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogError("Error in backward-compatible SendTradeFinishedEmbedAsync: " + ex.Message, "SendTradeFinishedEmbedAsync");
        }
    }

    public static Task SendTradeCanceledEmbedAsync(IUser user, string reason)
    {
        var data = new TradeEmbedData { UserId = user.Id };
        return SendTradeCanceledEmbedAsync(user, reason, data);
    }
}
