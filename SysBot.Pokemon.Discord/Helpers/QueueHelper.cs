using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using PKHeX.Drawing.PokeSprite;
using SysBot.Pokemon.Discord.Commands.Bots;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Color = System.Drawing.Color;
using DiscordColor = Discord.Color;

namespace SysBot.Pokemon.Discord;

public static class QueueHelper<T> where T : PKM, new()
{
    private const uint MaxTradeCode = 9999_9999;

    private static readonly Dictionary<int, string> MilestoneImages = new()
    {
        { 1, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/001.png" },
        { 50, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/050.png" },
        { 100, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/100.png" },
        { 150, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/150.png" },
        { 200, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/200.png" },
        { 250, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/250.png" },
        { 300, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/300.png" },
        { 350, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/350.png" },
        { 400, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/400.png" },
        { 450, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/450.png" },
        { 500, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/500.png" },
        { 550, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/550.png" },
        { 600, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/600.png" },
        { 650, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/650.png" },
        { 700, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/700.png" }
    };

    private static string GetMilestoneDescription(int tradeCount)
    {
        return tradeCount switch
        {
            1 => "Congratulations on your first trade!\n**Status:** Newbie Trainer.",
            50 => "You've reached 50 trades!\n**Status:** Novice Trainer.",
            100 => "You've reached 100 trades!\n**Status:** Pokémon Professor.",
            150 => "You've reached 150 trades!\n**Status:** Pokémon Specialist.",
            200 => "You've reached 200 trades!\n**Status:** Pokémon Champion.",
            250 => "You've reached 250 trades!\n**Status:** Pokémon Hero.",
            300 => "You've reached 300 trades!\n**Status:** Pokémon Elite.",
            350 => "You've reached 350 trades!\n**Status:** Pokémon Trader.",
            400 => "You've reached 400 trades!\n**Status:** Pokémon Sage.",
            450 => "You've reached 450 trades!\n**Status:** Pokémon Legend.",
            500 => "You've reached 500 trades!\n**Status:** Region Master.",
            550 => "You've reached 550 trades!\n**Status:** Trade Master.",
            600 => "You've reached 600 trades!\n**Status:** World Famous.",
            650 => "You've reached 650 trades!\n**Status:** Pokémon Master.",
            700 => "You've reached 700 trades!\n**Status:** Pokémon God.",
            _ => $"Congratulations on reaching {tradeCount} trades! Keep it going!"
        };
    }

    public static async Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, SocketUser trader, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1, bool isHiddenTrade = false, bool isMysteryEgg = false, List<Pictocodes>? lgcode = null, bool ignoreAutoOT = false, bool setEdited = false, bool isNonNative = false)
    {
        if ((uint)code > MaxTradeCode)
        {
            await context.Channel.SendMessageAsync("Trade code should be 00000000-99999999!").ConfigureAwait(false);
            return;
        }

        try
        {
            // Only send trade code for non-batch trades (batch container will handle its own)
            if (!isBatchTrade)
            {
                if (trade is PB7 && lgcode != null)
                {
                    var (thefile, lgcodeembed) = CreateLGLinkCodeSpriteEmbed(lgcode);
                    await trader.SendFileAsync(thefile, "Your trade code will be.", embed: lgcodeembed).ConfigureAwait(false);
                }
                else
                {
                    await EmbedHelper.SendTradeCodeEmbedAsync(trader, code).ConfigureAwait(false);
                }
            }

            var result = await AddToTradeQueue(context, trade, code, trainer, sig, routine, type, trader, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg, lgcode, ignoreAutoOT, setEdited, isNonNative).ConfigureAwait(false);
        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex).ConfigureAwait(false);
        }
    }

    public static Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, bool ignoreAutoOT = false)
    {
        return AddToQueueAsync(context, code, trainer, sig, trade, routine, type, context.User, ignoreAutoOT: ignoreAutoOT);
    }

    private static async Task<TradeQueueResult> AddToTradeQueue(SocketCommandContext context, T pk, int code, string trainerName,
        RequestSignificance sig, PokeRoutineType type, PokeTradeType t, SocketUser trader, bool isBatchTrade,
        int batchTradeNumber, int totalBatchTrades, bool isHiddenTrade, bool isMysteryEgg = false,
        List<Pictocodes>? lgcode = null, bool ignoreAutoOT = false, bool setEdited = false, bool isNonNative = false)
    {
        // Note: This method should only be called for individual trades now
        // Batch trades use AddBatchContainerToQueueAsync

        var user = trader;
        var userID = user.Id;
        var name = user.Username;
        var trainer = new PokeTradeTrainerInfo(trainerName, userID);
        var notifier = new DiscordTradeNotifier<T>(pk, trainer, code, trader, batchTradeNumber, totalBatchTrades,
            isMysteryEgg, lgcode: lgcode!);

        int uniqueTradeID = GenerateUniqueTradeID();

        var detail = new PokeTradeDetail<T>(pk, trainer, notifier, t, code, sig == RequestSignificance.Favored,
            lgcode, batchTradeNumber, totalBatchTrades, isMysteryEgg, isHiddenTrade, uniqueTradeID, ignoreAutoOT, setEdited);

        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.LinkTrade, name, uniqueTradeID);
        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var isSudo = sig == RequestSignificance.Owner;
        var added = Info.AddToTradeQueue(trade, userID, false, isSudo);

        // Start queue position updates for Discord notification
        if (added != QueueResultAdd.AlreadyInQueue && added != QueueResultAdd.NotAllowedItem && notifier is DiscordTradeNotifier<T> discordNotifier)
        {
            // IMPORTANT: Update the notifier's unique trade ID to match the one used in the queue
            // Otherwise the DM will check position with the wrong ID and return incorrect results
            discordNotifier.UpdateUniqueTradeID(uniqueTradeID);
            await discordNotifier.SendInitialQueueUpdate().ConfigureAwait(false);
        }

        int totalTradeCount = 0;
        TradeCodeStorage.TradeCodeDetails? tradeDetails = null;
        if (SysCord<T>.Runner.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            totalTradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            tradeDetails = tradeCodeStorage.GetTradeDetails(trader.Id);
        }

        if (added == QueueResultAdd.AlreadyInQueue)
        {
            await context.Channel.SendMessageAsync($"{trader.Mention} - You are already in the queue!").ConfigureAwait(false);
            return new TradeQueueResult(false);
        }

        if (added == QueueResultAdd.QueueFull)
        {
            var maxCount = SysCord<T>.Runner.Config.Queues.MaxQueueCount;
            var embed = new EmbedBuilder()
                .WithColor(DiscordColor.Red)
                .WithTitle("🚫 Queue Full")
                .WithDescription($"The queue is currently full ({maxCount}/{maxCount}). Please try again later when space becomes available.")
                .WithFooter("Queue will open up as trades are completed")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await context.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
            return new TradeQueueResult(false);
        }

        if (added == QueueResultAdd.NotAllowedItem)
        {
            var held = pk.HeldItem;
            var itemName = held > 0 ? PKHeX.Core.GameInfo.GetStrings("en").Item[held] : "(none)";
            await context.Channel.SendMessageAsync($"{trader.Mention} - Trade blocked: the held item '{itemName}' cannot be traded in this game.").ConfigureAwait(false);
            return new TradeQueueResult(false);
        }

        var embedData = DetailsExtractor<T>.ExtractPokemonDetails(
            pk, trader, isMysteryEgg, type == PokeRoutineType.Clone, type == PokeRoutineType.Dump,
            type == PokeRoutineType.FixOT, type == PokeRoutineType.SeedCheck, false, 1, 1
        );

        try
        {
            (string embedImageUrl, DiscordColor embedColor) = await PrepareEmbedDetails(pk);

            embedData.EmbedImageUrl = isMysteryEgg ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/mysteryegg3.png?raw=true&width=300&height=300" :
            type == PokeRoutineType.Dump ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Dumping.png?raw=true&width=300&height=300" :
            type == PokeRoutineType.Clone ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Cloning.png?raw=true&width=300&height=300" :
            type == PokeRoutineType.SeedCheck ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Seeding.png?raw=true&width=300&height=300" :
            type == PokeRoutineType.FixOT ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/FixOTing.png?raw=true&width=300&height=300" :
                                       embedImageUrl;

            embedData.HeldItemUrl = string.Empty;
            if (!string.IsNullOrWhiteSpace(embedData.HeldItem))
            {
                string heldItemName = embedData.HeldItem.ToLower().Replace(" ", "");
                embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
            }

            embedData.IsLocalFile = File.Exists(embedData.EmbedImageUrl);

            var position = Info.CheckPosition(userID, uniqueTradeID, type);
            var botct = Info.Hub.Bots.Count;
            var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;
            var etaMessage = $"Wait Estimate: {baseEta:F1} min(s) for trade.";
            string footerText = $"Current Queue Position: {(position.Position == -1 ? 1 : position.Position)}";
            string trainerMention = trader.Mention;
            string userDetailsText = DetailsExtractor<T>.GetUserDetails(totalTradeCount, tradeDetails, trainerMention);

            if (!string.IsNullOrEmpty(userDetailsText))
            {
                footerText += $"\n{userDetailsText}";
            }
            footerText += $"\n{etaMessage}";
            footerText += $"\nPKM UniverseBot {TradeBot.Version}";

            var embedBuilder = new EmbedBuilder()
                .WithColor(embedColor)
                .WithImageUrl(embedData.IsLocalFile ? $"attachment://{Path.GetFileName(embedData.EmbedImageUrl)}" : embedData.EmbedImageUrl)
                .WithFooter(footerText)
                .WithAuthor(new EmbedAuthorBuilder()
                    .WithName(embedData.AuthorName)
                    .WithIconUrl(trader.GetAvatarUrl() ?? trader.GetDefaultAvatarUrl())
                    .WithUrl("https://creator.pkm-universe.com/trade-hub-ultimate.html"));

            DetailsExtractor<T>.AddAdditionalText(embedBuilder);

            if (!isMysteryEgg && type != PokeRoutineType.Clone && type != PokeRoutineType.Dump && type != PokeRoutineType.FixOT && type != PokeRoutineType.SeedCheck)
            {
                DetailsExtractor<T>.AddNormalTradeFields(embedBuilder, embedData, trader.Mention, pk);
            }
            else
            {
                DetailsExtractor<T>.AddSpecialTradeFields(embedBuilder, isMysteryEgg, type == PokeRoutineType.SeedCheck, type == PokeRoutineType.Clone, type == PokeRoutineType.FixOT, trader.Mention);
            }

            // Check if the Pokemon is Non-Native and/or has a Home Tracker.
            // On Z-A (PA9) bots, append a hint pointing members to the SwSh bots for a
            // HOME-uploadable version of the same species — the Z-A bot can ship the file
            // for in-game use but the ZA → HOME transfer fails.
            bool isZABot = typeof(T) == typeof(PA9);
            // Pick the right "go request from" hint per species + form. Three buckets:
            //   SwSh-only  → Galarian birds (form 1), Xerneas/Yveltal (Kalos, not in BDSP),
            //                Zeraora (WC8 HOME Gift). Hint: Celebi-SWSH / Jirachi-SWSH.
            //   BDSP-only  → Dialga, Palkia, Phione, Manaphy, Darkrai, Shaymin (not in SwSh
            //                Max Lair). Hint: Rayquaza-BDSP / Giratina-BDSP.
            //   Both       → K/G/R, Kanto birds (form 0), Kanto beasts, Mewtwo, Lugia, Ho-Oh,
            //                Regis, Latias/Latios, Heatran, Regigigas, Giratina, Cresselia.
            //                Hint: all four bots.
            string swshHint = "";
            if (isZABot)
            {
                int route = GetHomeUploadRoute(pk.Species, pk.Form);
                swshHint = route switch
                {
                    1 => "\n\n*For a HOME-uploadable shiny, request from **Celebi-SWSH** or **Jirachi-SWSH** instead.*",
                    2 => "\n\n*For a HOME-uploadable shiny, request from **Rayquaza-BDSP** or **Giratina-BDSP** instead.*",
                    3 => "\n\n*For a HOME-uploadable shiny, request from **Celebi-SWSH**, **Jirachi-SWSH**, **Rayquaza-BDSP**, or **Giratina-BDSP** instead.*",
                    _ => "", // species we haven't explicitly classified → no redirect hint
                };
            }
            if (pk is IHomeTrack homeTrack)
            {
                if (homeTrack.HasTracker && isNonNative)
                {
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/exclamation.gif";
                    embedBuilder.AddField("**__Notice__**: **This Pokemon is Non-Native & Has Home Tracker.**", "*AutoOT not applied.*" + swshHint);
                }
                else if (homeTrack.HasTracker)
                {
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/exclamation.gif";
                    embedBuilder.AddField("**__Notice__**: **Home Tracker Detected.**", "*AutoOT not applied.*");
                }
                else if (isNonNative)
                {
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/exclamation.gif";
                    embedBuilder.AddField("**__Notice__**: **This Pokemon is Non-Native.**", "*Cannot enter HOME & AutoOT not applied.*" + swshHint);
                }
            }
            else if (isNonNative)
            {
                embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/exclamation.gif";
                embedBuilder.AddField("**__Notice__**: **This Pokemon is Non-Native.**", "*Cannot enter HOME & AutoOT not applied.*" + swshHint);
            }

            DetailsExtractor<T>.AddThumbnails(embedBuilder, type == PokeRoutineType.Clone, type == PokeRoutineType.SeedCheck, embedData.HeldItemUrl);

            if (!isHiddenTrade && SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseEmbeds)
            {
                var embed = embedBuilder.Build();
                if (embed == null)
                {
                    Console.WriteLine("Error: Embed is null.");
                    await context.Channel.SendMessageAsync("An error occurred while preparing the trade details.");
                    return new TradeQueueResult(false);
                }

                if (embedData.IsLocalFile)
                {
                    await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed);
                    await ScheduleFileDeletion(embedData.EmbedImageUrl, 0);
                }
                else
                {
                    await context.Channel.SendMessageAsync(embed: embed);
                }
            }
            else
            {
                 var message = $"▹𝗦𝗨𝗖𝗖𝗘𝗦𝗦𝗙𝗨𝗟𝗟𝗬 𝗔𝗗𝗗𝗘𝗗◃\n" +
                 $"//【𝐔𝐒𝐄𝐑: ||Owner Access Only||】\n" +
                 $"//【𝐏𝐎𝐒𝐈𝐓𝐈𝐎𝐍: {position.Position}】\n";

                if (embedData.SpeciesName != "---")
                {
                    message += $"//【𝐏𝐎𝐊𝐄𝐌𝐎𝐍: ||{embedData.SpeciesName}||】\n";
                }

                message += $"//【𝐄𝐓𝐀: {baseEta:F1} Min(s)】";
                await context.Channel.SendMessageAsync(message);
            }
        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex);
            return new TradeQueueResult(false);
        }

        if (SysCord<T>.Runner.Hub.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            int tradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            _ = SendMilestoneEmbed(tradeCount, context.Channel, trader);
        }

        return new TradeQueueResult(true);
    }

    public static async Task AddBatchContainerToQueueAsync(SocketCommandContext context, int code, string trainer, T firstTrade, List<T> allTrades, RequestSignificance sig, SocketUser trader, int totalBatchTrades)
    {
        var userID = trader.Id;
        var name = trader.Username;
        var trainer_info = new PokeTradeTrainerInfo(trainer, userID);
        var notifier = new DiscordTradeNotifier<T>(firstTrade, trainer_info, code, trader, 1, totalBatchTrades, false, lgcode: []);

        int uniqueTradeID = GenerateUniqueTradeID();

        var detail = new PokeTradeDetail<T>(firstTrade, trainer_info, notifier, PokeTradeType.Batch, code,
            sig == RequestSignificance.Favored, null, 1, totalBatchTrades, false)
        {
            BatchTrades = allTrades
        };

        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.Batch, name, uniqueTradeID: uniqueTradeID);
        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var added = Info.AddToTradeQueue(trade, userID, false, sig == RequestSignificance.Owner);

        // Send trade code once
        await EmbedHelper.SendTradeCodeEmbedAsync(trader, code).ConfigureAwait(false);

        // Start queue position updates for Discord notification
        if (added != QueueResultAdd.AlreadyInQueue && added != QueueResultAdd.NotAllowedItem && notifier is DiscordTradeNotifier<T> discordNotifier)
        {
            // IMPORTANT: Update the notifier's unique trade ID to match the one used in the queue
            // Otherwise the DM will check position with the wrong ID and return incorrect results
            discordNotifier.UpdateUniqueTradeID(uniqueTradeID);
            await discordNotifier.SendInitialQueueUpdate().ConfigureAwait(false);
        }

        // Handle the display
        if (added == QueueResultAdd.AlreadyInQueue)
        {
            await context.Channel.SendMessageAsync($"{trader.Mention} - You are already in the queue!").ConfigureAwait(false);
            return;
        }

        if (added == QueueResultAdd.QueueFull)
        {
            var maxCount = SysCord<T>.Runner.Config.Queues.MaxQueueCount;
            var embed = new EmbedBuilder()
                .WithColor(DiscordColor.Red)
                .WithTitle("🚫 Queue Full")
                .WithDescription($"The queue is currently full ({maxCount}/{maxCount}). Please try again later when space becomes available.")
                .WithFooter("Queue will open up as trades are completed")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await context.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
            return;
        }

        if (added == QueueResultAdd.NotAllowedItem)
        {
            var held = firstTrade.HeldItem;
            var itemName = held > 0 ? PKHeX.Core.GameInfo.GetStrings("en").Item[held] : "(none)";
            await context.Channel.SendMessageAsync($"{trader.Mention} - Trade blocked: the held item '{itemName}' cannot be traded in this game.").ConfigureAwait(false);
            return;
        }

        var position = Info.CheckPosition(userID, uniqueTradeID, PokeRoutineType.Batch);
        var botct = Info.Hub.Bots.Count;
        var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;

        // Get user trade details for footer
        int totalTradeCount = 0;
        TradeCodeStorage.TradeCodeDetails? tradeDetails = null;
        if (SysCord<T>.Runner.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            totalTradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            tradeDetails = tradeCodeStorage.GetTradeDetails(trader.Id);
        }

        // Send initial batch summary message
        await context.Channel.SendMessageAsync($"{trader.Mention} - Added batch trade with {totalBatchTrades} Pokémon to the queue! Position: {position.Position}. Estimated: {baseEta:F1} min(s).").ConfigureAwait(false);

        // Create and send embeds for each Pokémon in the batch
        if (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseEmbeds)
        {
            for (int i = 0; i < allTrades.Count; i++)
            {
                var pk = allTrades[i];
                var batchTradeNumber = i + 1;

                // Extract details for this Pokémon
                var embedData = DetailsExtractor<T>.ExtractPokemonDetails(
                    pk, trader, false, false, false, false, false, true, batchTradeNumber, totalBatchTrades
                );

                try
                {
                    // Prepare embed details
                    (string embedImageUrl, DiscordColor embedColor) = await PrepareEmbedDetails(pk);

                    embedData.EmbedImageUrl = embedImageUrl;
                    embedData.HeldItemUrl = string.Empty;
                    if (!string.IsNullOrWhiteSpace(embedData.HeldItem))
                    {
                        string heldItemName = embedData.HeldItem.ToLower().Replace(" ", "");
                        embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
                    }

                    embedData.IsLocalFile = File.Exists(embedData.EmbedImageUrl);

                    // Build footer text with batch info
                    string footerText = $"Batch Trade {batchTradeNumber} of {totalBatchTrades}";
                    if (i == 0) // Only show position and ETA on first embed
                    {
                        footerText += $" | Current Queue Position: {position.Position}";
                        string trainerMention = trader.Mention;
                        string userDetailsText = DetailsExtractor<T>.GetUserDetails(totalTradeCount, tradeDetails, trainerMention);

                        if (!string.IsNullOrEmpty(userDetailsText))
                        {
                            footerText += $"\n{userDetailsText}";
                        }
                        footerText += $"\nWait Estimate: {baseEta:F1} min(s) for batch";
                    }

                    // Create embed
                    var embedBuilder = new EmbedBuilder()
                        .WithColor(embedColor)
                        .WithImageUrl(embedData.IsLocalFile ? $"attachment://{Path.GetFileName(embedData.EmbedImageUrl)}" : embedData.EmbedImageUrl)
                        .WithFooter(footerText)
                        .WithAuthor(new EmbedAuthorBuilder()
                            .WithName(embedData.AuthorName)
                            .WithIconUrl(trader.GetAvatarUrl() ?? trader.GetDefaultAvatarUrl())
                            .WithUrl("https://creator.pkm-universe.com/trade-hub-ultimate.html"));

                    DetailsExtractor<T>.AddAdditionalText(embedBuilder);
                    DetailsExtractor<T>.AddNormalTradeFields(embedBuilder, embedData, trader.Mention, pk);

                    // Check for Non-Native and Home Tracker
                    if (pk is IHomeTrack homeTrack)
                    {
                        if (homeTrack.HasTracker)
                        {
                            embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/exclamation.gif";
                            embedBuilder.AddField("**__Notice__**: **Home Tracker Detected.**", "*AutoOT not applied.*");
                        }
                    }

                    DetailsExtractor<T>.AddThumbnails(embedBuilder, false, false, embedData.HeldItemUrl);

                    var embed = embedBuilder.Build();

                    // Send embed
                    if (embedData.IsLocalFile)
                    {
                        await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed);
                        await ScheduleFileDeletion(embedData.EmbedImageUrl, 0);
                    }
                    else
                    {
                        await context.Channel.SendMessageAsync(embed: embed);
                    }

                    // Small delay between embeds to avoid rate limiting
                    if (i < allTrades.Count - 1)
                    {
                        await Task.Delay(500);
                    }
                }
                catch (HttpException ex)
                {
                    await HandleDiscordExceptionAsync(context, trader, ex);
                }
            }
        }

        // Send milestone embed if applicable
        if (SysCord<T>.Runner.Hub.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            int tradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            _ = SendMilestoneEmbed(tradeCount, context.Channel, trader);
        }
    }

    private static int GenerateUniqueTradeID()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int randomValue = Random.Shared.Next(1000);
        return (int)((timestamp % int.MaxValue) * 1000 + randomValue);
    }

    private static string GetImageFolderPath()
    {
        string baseDirectory = Directory.GetCurrentDirectory();
        string imagesFolder = Path.Combine(baseDirectory, "Images");

        if (!Directory.Exists(imagesFolder))
        {
            Directory.CreateDirectory(imagesFolder);
        }

        return imagesFolder;
    }

    private static string SaveImageLocally(System.Drawing.Image image)
    {
        string imagesFolderPath = GetImageFolderPath();
        string filePath = Path.Combine(imagesFolderPath, $"image_{Guid.NewGuid()}.png");

#pragma warning disable CA1416 // Validate platform compatibility
        image.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
#pragma warning restore CA1416 // Validate platform compatibility

        return filePath;
    }

    public static async Task<(string, DiscordColor)> PrepareEmbedDetails(T pk)
    {
        string embedImageUrl;

        // Eggs: show an actual egg sprite, not the hatched Pokémon. PokeImg only varies the
        // image SIZE for eggs (it still returns the species capture), and the form-specific +
        // <5KB-fallback logic below would also resolve to a species sprite — so short-circuit
        // here and return the egg sprite directly with its own dominant color.
        //
        // Eggs:
        //   BDSP (PB8) → composite the species sprite centered on the branded egg so the embed
        //                shows the Pokémon "inside" the egg. Uses the PokeAPI home sprite (keyed
        //                purely by dex number) so it works for every species — PokeImg's gender
        //                suffix (md/fd vs mf) 404s for some species and broke the overlay.
        //   all others → plain branded egg (matches the website's pokemon-egg.webp).
        if (pk.IsEgg)
        {
            const string eggWebp = "https://creator.pkm-universe.com/assets/pokemon-egg.webp";

            if (pk is PB8)
            {
                const string eggBasePng = "https://creator.pkm-universe.com/assets/anime-eggs/default-egg.png"; // GDI+-decodable, same art
                try
                {
                    string speciesUrl = $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{pk.Species}.png";
                    using var composite = await OverlaySpeciesOnEgg(eggBasePng, speciesUrl);
                    string localPath = SaveImageLocally(composite);
                    var (cR, cG, cB) = await GetDominantColorAsync(localPath);
                    return (localPath, new DiscordColor(cR, cG, cB));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Egg composite failed, using plain egg: {ex.Message}");
                }
            }

            var (eR, eG, eB) = await GetDominantColorAsync(eggWebp);
            return (eggWebp, new DiscordColor(eR, eG, eB));
        }

        bool canGmax = pk is PK8 pk8g && pk8g.CanGigantamax;
        embedImageUrl = pk.IsEgg
            ? TradeExtensions<T>.PokeImg(pk, false, true, null)
            : TradeExtensions<T>.PokeImg(pk, canGmax, false, SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.PreferredImageSize);

        // For Pokemon with non-default forms, try form-specific sprite FIRST
        if (pk.Form > 0)
        {
            try
            {
                string formUrl = $"https://creator.pkm-universe.com/assets/sprites/{pk.Species}-{pk.Form}-v2.png";
                using var checkClient = new HttpClient();
                var formReq = new HttpRequestMessage(HttpMethod.Head, formUrl);
                var formResp = await checkClient.SendAsync(formReq);
                if (formResp.IsSuccessStatusCode)
                {
                    embedImageUrl = formUrl;
                }
            }
            catch { }
        }

        // Validate sprite — if ZE-FusionBot repo returns a placeholder (<5KB), fall back
        try
        {
            using var checkClient2 = new HttpClient();
            var headReq = new HttpRequestMessage(HttpMethod.Head, embedImageUrl);
            var headResp = await checkClient2.SendAsync(headReq);
            if (!headResp.IsSuccessStatusCode || (headResp.Content.Headers.ContentLength.HasValue && headResp.Content.Headers.ContentLength.Value < 5000))
            {
                embedImageUrl = $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{pk.Species}.png";
            }
        }
        catch { }

        (int R, int G, int B) = await GetDominantColorAsync(embedImageUrl);
        return (embedImageUrl, new DiscordColor(R, G, B));
    }

    private static async Task<(System.Drawing.Image?, bool)> OverlayBallOnSpecies(string speciesImageUrl, string ballImageUrl)
    {
        using var speciesImage = await LoadImageFromUrl(speciesImageUrl);
        if (speciesImage == null)
        {
            Console.WriteLine("Species image could not be loaded.");
            return (null, false);
        }

        var ballImage = await LoadImageFromUrl(ballImageUrl);
        if (ballImage == null)
        {
            Console.WriteLine($"Ball image could not be loaded: {ballImageUrl}");
#pragma warning disable CA1416 // Validate platform compatibility
            return ((System.Drawing.Image)speciesImage.Clone(), false);
#pragma warning restore CA1416 // Validate platform compatibility
        }

        using (ballImage)
        {
#pragma warning disable CA1416 // Validate platform compatibility
            using (var graphics = Graphics.FromImage(speciesImage))
            {
                var ballPosition = new Point(speciesImage.Width - ballImage.Width, speciesImage.Height - ballImage.Height);
                graphics.DrawImage(ballImage, ballPosition);
            }
#pragma warning restore CA1416 // Validate platform compatibility

#pragma warning disable CA1416 // Validate platform compatibility
            return ((System.Drawing.Image)speciesImage.Clone(), true);
#pragma warning restore CA1416 // Validate platform compatibility
        }
    }

    private static async Task<System.Drawing.Image> OverlaySpeciesOnEgg(string eggImageUrl, string speciesImageUrl)
    {
        System.Drawing.Image? eggImage = await LoadImageFromUrl(eggImageUrl);
        System.Drawing.Image? speciesImage = await LoadImageFromUrl(speciesImageUrl);

        if (eggImage == null || speciesImage == null)
        {
            throw new InvalidOperationException("Failed to load egg or species image.");
        }

#pragma warning disable CA1416 // Validate platform compatibility
        // Render at the egg's native resolution (no downscale to 128) for a crisp embed.
        var canvas = new Bitmap(eggImage.Width, eggImage.Height);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            // Draw the egg as the background.
            g.DrawImage(eggImage, 0, 0, eggImage.Width, eggImage.Height);

            // Scale the species sprite to ~55% of the egg's width and center it slightly
            // above middle so it reads as the Pokémon sitting "inside" the egg, not covering it.
            double targetW = eggImage.Width * 0.55;
            double spriteScale = targetW / speciesImage.Width;
            int sW = (int)(speciesImage.Width * spriteScale);
            int sH = (int)(speciesImage.Height * spriteScale);
            int sX = (eggImage.Width - sW) / 2;
            int sY = (int)((eggImage.Height - sH) / 2 - eggImage.Height * 0.04); // nudge up a touch
            g.DrawImage(speciesImage, sX, sY, sW, sH);
        }

        eggImage.Dispose();
        speciesImage.Dispose();
#pragma warning restore CA1416 // Validate platform compatibility
        return canvas;
    }

    private static async Task<System.Drawing.Image?> LoadImageFromUrl(string url)
    {
        try
        {
            using HttpClient client = new();
            HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to load image from {url}. Status code: {response.StatusCode}");
                return null;
            }

            byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
            if (imageBytes == null || imageBytes.Length == 0)
            {
                Console.WriteLine($"No data received from {url}");
                return null;
            }

            // Copy to MemoryStream so the Image doesn't depend on the HttpClient/response stream
            var ms = new MemoryStream(imageBytes);
#pragma warning disable CA1416
            return System.Drawing.Image.FromStream(ms);
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load image from {url}: {ex.Message}");
            return null;
        }
    }

    public static async Task ScheduleFileDeletion(string filePath, int delayInMilliseconds)
    {
        await Task.Delay(delayInMilliseconds);
        DeleteFile(filePath);
    }

    private static void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error deleting file: {ex.Message}");
            }
        }
    }

    private static async Task SendMilestoneEmbed(int tradeCount, ISocketMessageChannel channel, SocketUser user)
    {
        if (MilestoneImages.TryGetValue(tradeCount, out string? imageUrl))
        {
            var embed = new EmbedBuilder()
                .WithTitle($"{user.Username}'s Milestone Medal")
                .WithDescription(GetMilestoneDescription(tradeCount))
                .WithColor(new DiscordColor(255, 215, 0)) // Gold color
                .WithThumbnailUrl(imageUrl)
                .Build();

            await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }
    }

    public static async Task<(int R, int G, int B)> GetDominantColorAsync(string imagePath)
    {
        try
        {
            Bitmap image = await LoadImageAsync(imagePath);

            var colorCount = new Dictionary<Color, int>();
#pragma warning disable CA1416 // Validate platform compatibility
            await Task.Run(() =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixelColor = image.GetPixel(x, y);

                        if (pixelColor.A < 128 || pixelColor.GetBrightness() > 0.9) continue;

                        var brightnessFactor = (int)(pixelColor.GetBrightness() * 100);
                        var saturationFactor = (int)(pixelColor.GetSaturation() * 100);
                        var combinedFactor = brightnessFactor + saturationFactor;

                        var quantizedColor = Color.FromArgb(
                            pixelColor.R / 10 * 10,
                            pixelColor.G / 10 * 10,
                            pixelColor.B / 10 * 10
                        );

                        if (colorCount.ContainsKey(quantizedColor))
                        {
                            colorCount[quantizedColor] += combinedFactor;
                        }
                        else
                        {
                            colorCount[quantizedColor] = combinedFactor;
                        }
                    }
                }
            });

            image.Dispose();
#pragma warning restore CA1416 // Validate platform compatibility

            if (colorCount.Count == 0)
                return (255, 255, 255);

            var dominantColor = colorCount.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
            return (dominantColor.R, dominantColor.G, dominantColor.B);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing image from {imagePath}. Error: {ex.Message}");
            return (255, 255, 255);
        }
    }

    private static async Task<Bitmap> LoadImageAsync(string imagePath)
    {
        if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(imagePath);
            await using var stream = await response.Content.ReadAsStreamAsync();
#pragma warning disable CA1416 // Validate platform compatibility
            return new Bitmap(stream);
#pragma warning restore CA1416 // Validate platform compatibility
        }
        else
        {
#pragma warning disable CA1416 // Validate platform compatibility
            return new Bitmap(imagePath);
#pragma warning restore CA1416 // Validate platform compatibility
        }
    }

    private static async Task HandleDiscordExceptionAsync(SocketCommandContext context, SocketUser trader, HttpException ex)
    {
        string message = string.Empty;
        switch (ex.DiscordCode)
        {
            case DiscordErrorCode.InsufficientPermissions or DiscordErrorCode.MissingPermissions:
                {
                    var permissions = context.Guild.CurrentUser.GetPermissions(context.Channel as IGuildChannel);
                    if (!permissions.SendMessages)
                    {
                        message = "You must grant me \"Send Messages\" permissions!";
                        Base.LogUtil.LogError("QueueHelper", message);
                        return;
                    }
                    if (!permissions.ManageMessages)
                    {
                        var app = await context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
                        var owner = app.Owner.Id;
                        message = $"<@{owner}> You must grant me \"Manage Messages\" permissions!";
                    }
                }
                break;

            case DiscordErrorCode.CannotSendMessageToUser:
                {
                    message = context.User == trader ? "You must enable private messages in order to be queued!" : "The mentioned user must enable private messages in order for them to be queued!";
                }
                break;

            default:
                {
                    message = ex.DiscordCode != null ? $"Discord error {(int)ex.DiscordCode}: {ex.Reason}" : $"Http error {(int)ex.HttpCode}: {ex.Message}";
                }
                break;
        }
        await context.Channel.SendMessageAsync(message).ConfigureAwait(false);
    }

    private static string GetEggTypeImageUrl(T pk)
    {
        var pi = pk.PersonalInfo;
        byte typeIndex = pi.Type1;

        string[] typeNames = [
            "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost",
            "Steel", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon",
            "Dark", "Fairy"
        ];

        string typeName = (typeIndex >= 0 && typeIndex < typeNames.Length)
            ? typeNames[typeIndex]
            : "Normal";

        return $"https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Eggs/Egg_{typeName}.png";
    }

    public static (string, Embed) CreateLGLinkCodeSpriteEmbed(List<Pictocodes> lgcode)
    {
        int codecount = 0;
        List<System.Drawing.Image> spritearray = [];
        foreach (Pictocodes cd in lgcode)
        {
            var showdown = new ShowdownSet(cd.ToString());
            var sav = BlankSaveFile.Get(EntityContext.Gen7b, "pip");
            PKM pk = sav.GetLegalFromSet(showdown).Created;
#pragma warning disable CA1416 // Validate platform compatibility
            System.Drawing.Image png = pk.Sprite();
            var destRect = new Rectangle(-40, -65, 137, 130);
            var destImage = new Bitmap(137, 130);

            destImage.SetResolution(png.HorizontalResolution, png.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(png, destRect, 0, 0, png.Width, png.Height, GraphicsUnit.Pixel);
            }
            png = destImage;
            spritearray.Add(png);
#pragma warning restore CA1416 // Validate platform compatibility
            codecount++;
        }

#pragma warning disable CA1416 // Validate platform compatibility
        int outputImageWidth = spritearray[0].Width + 20;
        int outputImageHeight = spritearray[0].Height - 65;

        Bitmap outputImage = new(outputImageWidth, outputImageHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(outputImage))
        {
            graphics.DrawImage(spritearray[0], new Rectangle(0, 0, spritearray[0].Width, spritearray[0].Height),
                new Rectangle(new Point(), spritearray[0].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[1], new Rectangle(50, 0, spritearray[1].Width, spritearray[1].Height),
                new Rectangle(new Point(), spritearray[1].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[2], new Rectangle(100, 0, spritearray[2].Width, spritearray[2].Height),
                new Rectangle(new Point(), spritearray[2].Size), GraphicsUnit.Pixel);
        }

        System.Drawing.Image finalembedpic = outputImage;
        var filename = $"{Directory.GetCurrentDirectory()}//finalcode.png";
        finalembedpic.Save(filename);
#pragma warning restore CA1416 // Validate platform compatibility

        filename = Path.GetFileName($"{Directory.GetCurrentDirectory()}//finalcode.png");
        Embed returnembed = new EmbedBuilder().WithTitle($"{lgcode[0]}, {lgcode[1]}, {lgcode[2]}").WithImageUrl($"attachment://{filename}").Build();
        return (filename, returnembed);
    }

    /// <summary>
    /// Maps a Z-A-bot-served species/form to the HOME-uploadable redirect destination:
    /// 1 = SwSh bots only (Celebi/Jirachi), 2 = BDSP bots only (Rayquaza-BDSP/Giratina-BDSP),
    /// 3 = either works (list all four).
    /// </summary>
    private static int GetHomeUploadRoute(ushort species, byte form)
    {
        // Galarian birds (form 1) are SwSh-only — BDSP has no Galarian forms.
        if ((species == 144 || species == 145 || species == 146) && form == 1)
            return 1;

        return species switch
        {
            // BDSP-only (not in SwSh Crown Tundra Max Lair encounter table)
            483 => 2, // Dialga     — Spear Pillar (BD exclusive)
            484 => 2, // Palkia     — Spear Pillar (SP exclusive)
            489 => 2, // Phione     — BDSP Mystery Gift / breeding only
            490 => 2, // Manaphy    — BDSP Pokémon Ranger / event only
            491 => 2, // Darkrai    — Newmoon Island (Ramanas Park)
            492 => 2, // Shaymin    — Flower Paradise (Ramanas Park)

            // SwSh-only (not in BDSP Ramanas Park encounter table)
            716 => 1, // Xerneas    — Crown Tundra DA only (Kalos legend, no BDSP route)
            717 => 1, // Yveltal    — same
            807 => 1, // Zeraora    — WC8 HOME Gift only

            // Both routes work — Max Lair eligible AND BDSP Ramanas Park
            144 => 3, // Articuno (Kanto form 0)
            145 => 3, // Zapdos (Kanto)
            146 => 3, // Moltres (Kanto)
            150 => 3, // Mewtwo
            243 => 3, // Raikou
            244 => 3, // Entei
            245 => 3, // Suicune
            249 => 3, // Lugia
            250 => 3, // Ho-Oh
            377 => 3, // Regirock
            378 => 3, // Regice
            379 => 3, // Registeel
            380 => 3, // Latias
            381 => 3, // Latios
            382 => 3, // Kyogre
            383 => 3, // Groudon
            384 => 3, // Rayquaza
            485 => 3, // Heatran
            486 => 3, // Regigigas
            487 => 3, // Giratina
            488 => 3, // Cresselia

            // Default: assume SwSh route (safest — covers most untracked legendaries)
            _ => 1,
        };
    }
}
