using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Discord.Helpers.TradeModule;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DiscordColor = Discord.Color;
using static SysBot.Pokemon.Helpers.DetailedLegalityChecker;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

/// <summary>
/// Slash-command trade entry point (Phase 2 of the privileged-intents removal).
/// `/trade` opens a modal for a raw Showdown set and queues the trade straight from the
/// interaction — needs NO Message Content intent. The queue/embed body below mirrors
/// <see cref="CreatePokemonHelper.ExecuteCreatePokemonAsync{T}"/>; kept self-contained on purpose
/// so the proven /create-* path is untouched (can be de-duplicated into a shared helper later).
/// </summary>
public class TradeSlashModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("trade", "Trade a Pokémon — paste a Showdown set into the popup.")]
    public async Task TradeAsync()
    {
        if (Context.Guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await Context.Interaction.RespondWithModalAsync<TradeShowdownModal>("trade_modal").ConfigureAwait(false);
    }

    [ModalInteraction("trade_modal")]
    public async Task TradeModalSubmittedAsync(TradeShowdownModal modal)
    {
        if (Context.Guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: false).ConfigureAwait(false);

        var content = modal.ShowdownSet?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            await FollowupAsync("❌ You didn't enter a Showdown set.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            // Same AutoOT rule as the batch/text path: an explicit OT/TID/SID means the member
            // wants THAT trainer info, so AutoOT must not overwrite it.
            bool ignoreAutoOT = content.Contains("OT:") || content.Contains("TID:") || content.Contains("SID:");

            var processed = await Helpers<T>.ProcessShowdownSetAsync(content, ignoreAutoOT).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(processed.Error) || processed.Pokemon == null)
            {
                await FollowupAsync($"❌ {processed.Error ?? "That set could not be legalized."}", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var displayName = GameInfo.Strings.Species[processed.Pokemon.Species];
            await QueueTradeAsync(processed.Pokemon, processed.LgCode, ignoreAutoOT, displayName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ An error occurred: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }

    // Final legality check -> queue -> DM the code -> post the channel embed.
    private async Task QueueTradeAsync(T pk, List<Pictocodes>? lgcode, bool ignoreAutoOT, string displayName)
    {
        var context = Context;
        var Info = SysCord<T>.Runner.Hub.Queues.Info;

        var commandPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;
        if (!DetailedLegalityChecker.IsLegalWithDetailedReport(pk, displayName, commandPrefix, out string? legalityError))
        {
            await context.Interaction.FollowupAsync($"❌ **Illegal Pokemon Detected**\n\n{legalityError}", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var code = Info.GetRandomTradeCode(context.User.Id);
        var sig = RequestSignificance.None;
        var userID = context.User.Id;

        var trainer_info = new PokeTradeTrainerInfo(context.User.Username, userID);
        var notifier = new DiscordTradeNotifier<T>(pk, trainer_info, code, context.User, 1, 1, false, lgcode: lgcode ?? []);

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);

        var detail = new PokeTradeDetail<T>(pk, trainer_info, notifier, PokeTradeType.Specific, code,
            sig == RequestSignificance.Favored, null, 1, 1, false, false, uniqueTradeID, ignoreAutoOT: ignoreAutoOT);

        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.LinkTrade, context.User.Username, uniqueTradeID);

        var added = Info.AddToTradeQueue(trade, userID, false, sig == RequestSignificance.Owner);
        if (added == QueueResultAdd.AlreadyInQueue)
        {
            await context.Interaction.FollowupAsync("❌ You are already in the queue!", ephemeral: true).ConfigureAwait(false);
            return;
        }
        if (added == QueueResultAdd.QueueFull)
        {
            await context.Interaction.FollowupAsync("❌ The queue is currently full. Please try again later.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await EmbedHelper.SendTradeCodeEmbedAsync(context.User, code).ConfigureAwait(false);

        var embedData = DetailsExtractor<T>.ExtractPokemonDetails(pk, context.User, false, false, false, false, false, false, 1, 1);
        (string embedImageUrl, DiscordColor embedColor) = await QueueHelper<T>.PrepareEmbedDetails(pk).ConfigureAwait(false);
        embedData.EmbedImageUrl = embedImageUrl;

        embedData.HeldItemUrl = string.Empty;
        if (!string.IsNullOrWhiteSpace(embedData.HeldItem))
        {
            string heldItemName = embedData.HeldItem.ToLower().Replace(" ", "");
            embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
        }

        embedData.IsLocalFile = System.IO.File.Exists(embedData.EmbedImageUrl);

        var position = Info.CheckPosition(userID, uniqueTradeID, PokeRoutineType.LinkTrade);
        var botct = Info.Hub.Bots.Count;
        var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;
        var etaMessage = $"Wait Estimate: {baseEta:F1} min(s) for trade.";
        string footerText = $"Current Queue Position: {(position.Position == -1 ? 1 : position.Position)}";
        footerText += $"\n{etaMessage}";
        footerText += $"\nPKM UniverseBot {TradeBot.Version}";

        var embedBuilder = new EmbedBuilder()
            .WithColor(embedColor)
            .WithImageUrl(embedData.IsLocalFile ? $"attachment://{System.IO.Path.GetFileName(embedData.EmbedImageUrl)}" : embedData.EmbedImageUrl)
            .WithFooter(footerText)
            .WithAuthor(new EmbedAuthorBuilder()
                .WithName(embedData.AuthorName)
                .WithIconUrl(context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl()));

        DetailsExtractor<T>.AddAdditionalText(embedBuilder);
        DetailsExtractor<T>.AddNormalTradeFields(embedBuilder, embedData, context.User.Mention, pk);
        DetailsExtractor<T>.AddThumbnails(embedBuilder, false, false, embedData.HeldItemUrl);

        var embed = embedBuilder.Build();

        if (embedData.IsLocalFile)
        {
            await context.Interaction.FollowupAsync("✅ Added to the queue! Check your DMs for the trade code.", ephemeral: true).ConfigureAwait(false);
            await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed).ConfigureAwait(false);
            await QueueHelper<T>.ScheduleFileDeletion(embedData.EmbedImageUrl, 0).ConfigureAwait(false);
        }
        else
        {
            await context.Interaction.FollowupAsync(embed: embed).ConfigureAwait(false);
        }
    }
}

/// <summary>The popup shown by /trade — one multi-line box for a Showdown set.</summary>
public class TradeShowdownModal : IModal
{
    public string Title => "Trade a Pokémon";

    [InputLabel("Paste a Showdown set")]
    // NOTE: Discord caps this placeholder at 100 characters — keep it short.
    [ModalTextInput("showdown_set", TextInputStyle.Paragraph,
        placeholder: "Garchomp @ Life Orb\nJolly Nature\n- Earthquake\n- Dragon Claw",
        maxLength: 1500)]
    public string ShowdownSet { get; set; } = string.Empty;
}
