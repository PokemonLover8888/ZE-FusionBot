using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static SysBot.Pokemon.TradeSettings.TradeSettingsCategory;

namespace SysBot.Pokemon.Discord;

public static class Helpers<T> where T : PKM, new()
{
    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

    // BDSP canonical legendary location IDs resolved on first use via PKHeX's
    // own GetLocationName lookup (the bot's hardcoded dictionary had wrong IDs).
    private static ushort _lakeVerityId, _lakeValorId, _lakeAcuityId;
    private static ushort _fullmoonIslandId, _newmoonIslandId;
    private static ushort _spearPillarId, _turnbackCaveId, _starkMountainId, _flowerParadiseId;
    private static bool _bdspLocIdsResolved;
    private static readonly object _bdspLocIdLock = new();

    // SV Paradox species — shiny-LOCKED in Scarlet/Violet. The SV bots' older PKHeX
    // (v26.4.11.0) does NOT enforce their shiny-lock (it builds a "valid" native shiny with the
    // encounter's Shiny flag wrongly reported as Random), so the SV native safety net must never
    // rebuild a SHINY of these — see IsSvShinyLocked.
    private static readonly System.Collections.Generic.HashSet<ushort> SvParadoxSpecies = new()
    {
        984, 985, 986, 987, 988, 989, 990, 991, 992, 993,
        994, 995, 1005, 1006, 1009, 1010, 1020, 1021, 1022, 1023,
    };

    // True when a SHINY of this species can't be legally caught native in SV. Covers every SV
    // shiny-locked species (box legends, Treasures of Ruin, Loyal Three, Ogerpon, Terapagos,
    // Pecharunt via the category flags; Paradox via the explicit set). Verified via probe against
    // v26.4.11.0: gated species either fail this test or GetLegalNativeDirect returns null, so a
    // native shiny is never shipped for them — while regular species (Spiritomb, Larvitar, …) pass.
    private static bool IsSvShinyLocked(ushort species) =>
        SpeciesCategory.IsLegendary(species) || SpeciesCategory.IsSubLegendary(species)
        || SpeciesCategory.IsMythical(species) || SvParadoxSpecies.Contains(species);

    private static void ResolveBDSPLocationIds()
    {
        if (_bdspLocIdsResolved) return;
        lock (_bdspLocIdLock)
        {
            if (_bdspLocIdsResolved) return;
            var strings = GameInfo.Strings;
            for (ushort id = 1; id < 700; id++)
            {
                string name;
                try { name = strings.GetLocationName(false, id, 8, 8, GameVersion.BD); }
                catch { continue; }
                if (string.IsNullOrEmpty(name)) continue;
                // Take only the first match for names that have "-2" duplicates in PKHeX's table.
                if (_lakeVerityId == 0 && name == "Lake Verity") _lakeVerityId = id;
                else if (_lakeValorId == 0 && name == "Lake Valor") _lakeValorId = id;
                else if (_lakeAcuityId == 0 && name == "Lake Acuity") _lakeAcuityId = id;
                else if (_fullmoonIslandId == 0 && name == "Fullmoon Island") _fullmoonIslandId = id;
                else if (_newmoonIslandId == 0 && name == "Newmoon Island") _newmoonIslandId = id;
                else if (_spearPillarId == 0 && name == "Spear Pillar") _spearPillarId = id;
                else if (_turnbackCaveId == 0 && name == "Turnback Cave") _turnbackCaveId = id;
                else if (_starkMountainId == 0 && name == "Stark Mountain") _starkMountainId = id;
                else if (_flowerParadiseId == 0 && name == "Flower Paradise") _flowerParadiseId = id;
            }
            LogUtil.LogInfo($"[BDSP-LOC-RESOLVE] Verity={_lakeVerityId} Valor={_lakeValorId} Acuity={_lakeAcuityId} Fullmoon={_fullmoonIslandId} Newmoon={_newmoonIslandId} Spear={_spearPillarId} Turnback={_turnbackCaveId} Stark={_starkMountainId} FlowerP={_flowerParadiseId}", "Helpers");
            _bdspLocIdsResolved = true;
        }
    }

    public static Task<bool> EnsureUserNotInQueueAsync(ulong userID, int deleteDelay = 2)
    {
        if (!Info.IsUserInQueue(userID))
            return Task.FromResult(true);

        var existingTrades = Info.GetIsUserQueued(x => x.UserID == userID);
        foreach (var trade in existingTrades)
        {
            trade.Trade.IsProcessing = false;
        }

        var clearResult = Info.ClearTrade(userID);
        if (clearResult == QueueResultRemove.CurrentlyProcessing || clearResult == QueueResultRemove.NotInQueue)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public static async Task ReplyAndDeleteAsync(SocketCommandContext context, string message, int delaySeconds, IMessage? messageToDelete = null)
    {
        try
        {
            var sentMessage = await context.Channel.SendMessageAsync(message).ConfigureAwait(false);

            // Check if message deletion is enabled in settings
            if (!Info.Hub.Config.Discord.MessageDeletionEnabled)
                return;

            // Use configured delay from settings instead of hardcoded value
            var configuredDelay = Info.Hub.Config.Discord.ErrorMessageDeleteDelaySeconds;

            // Determine which user message to delete based on settings
            IMessage? userMessageToDelete = null;
            if (Info.Hub.Config.Discord.DeleteUserCommandMessages)
            {
                userMessageToDelete = messageToDelete ?? context.Message;
            }

            _ = DeleteMessagesAfterDelayAsync(sentMessage, userMessageToDelete, configuredDelay);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TradeModule<T>));
        }
    }

    public static async Task DeleteMessagesAfterDelayAsync(IMessage? sentMessage, IMessage? messageToDelete, int delaySeconds)
    {
        try
        {
            // Check if message deletion is enabled in settings
            if (!Info.Hub.Config.Discord.MessageDeletionEnabled)
                return;

            // Use configured delay from settings
            var configuredDelay = Info.Hub.Config.Discord.ErrorMessageDeleteDelaySeconds;
            await Task.Delay(configuredDelay * 1000);

            var tasks = new List<Task>();

            // Check if sentMessage is a bot message or user message
            // In some places, user messages are passed as the first parameter
            if (sentMessage != null)
            {
                // If it's a user message and DeleteUserCommandMessages is false, skip it
                if (sentMessage is IUserMessage userMsg && userMsg.Author.IsBot == false)
                {
                    if (Info.Hub.Config.Discord.DeleteUserCommandMessages)
                        tasks.Add(TryDeleteMessageAsync(sentMessage));
                }
                else
                {
                    // It's a bot message, always delete it
                    tasks.Add(TryDeleteMessageAsync(sentMessage));
                }
            }

            // Only delete user message if setting is enabled
            if (messageToDelete != null && Info.Hub.Config.Discord.DeleteUserCommandMessages)
                tasks.Add(TryDeleteMessageAsync(messageToDelete));

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TradeModule<T>));
        }
    }

    private static async Task TryDeleteMessageAsync(IMessage message)
    {
        try
        {
            await message.DeleteAsync();
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            // Ignore Unknown Message exception
        }
    }

    public static Task<ProcessedPokemonResult<T>> ProcessShowdownSetAsync(string content, bool ignoreAutoOT = false)
    {
        content = ReusableActions.StripCodeBlock(content);
        bool isEgg = TradeExtensions<T>.IsEggCheck(content);

        // CRITICAL FIX: Extract language BEFORE parsing ShowdownSet
        // If we let PKHeX see "Language: German", it includes it in the template
        // and ALM fails to find encounters with that language
        byte finalLanguage = LanguageHelper.GetFinalLanguage(
            content, null,
            (byte)Info.Hub.Config.Legality.GenerateLanguage,
            TradeExtensions<T>.DetectShowdownLanguage
        );

        // Remove Language: line from content before parsing
        // This prevents PKHeX from including it in the template
        var contentLines = content.Split('\n');
        var filteredLines = contentLines.Where(line =>
            !line.TrimStart().StartsWith("Language:", StringComparison.OrdinalIgnoreCase)
        ).ToArray();
        var contentWithoutLanguage = string.Join('\n', filteredLines);

        // Detect user-specified Tera Type (for SV non-native override fix)
        MoveType? userSpecifiedTeraType = null;
        var teraTypeLine = contentLines.FirstOrDefault(l => l.TrimStart().StartsWith("Tera Type:", StringComparison.OrdinalIgnoreCase));
        if (teraTypeLine != null)
        {
            var teraValue = teraTypeLine.Split(':').ElementAtOrDefault(1)?.Trim();
            if (!string.IsNullOrEmpty(teraValue))
            {
                if (teraValue.Equals("Stellar", StringComparison.OrdinalIgnoreCase))
                    userSpecifiedTeraType = (MoveType)TeraTypeUtil.Stellar;
                else if (Enum.TryParse<MoveType>(teraValue, true, out var parsedTera))
                    userSpecifiedTeraType = parsedTera;
            }
        }

        // Detect if user explicitly specified IVs (for 6IV default enforcement)
        bool userSpecifiedIVs = contentLines.Any(l => l.TrimStart().StartsWith("IVs:", StringComparison.OrdinalIgnoreCase));

        // The Mightiest Mark ("The Unrivaled") is exclusive to 7-star Tera Raids. The host
        // disables HOME-tracker checks, which makes wild/egg cross-origin encounters legal
        // and (since Slot/Egg outrank Mystery in the priority list) makes ALM pick a wild
        // encounter the mark can't attach to. When the mark is requested, force raid-first
        // encounter priority so ALM picks the raid encounter instead.
        bool wantsRaidMark = System.Text.RegularExpressions.Regex.IsMatch(
            contentWithoutLanguage, @"RibbonMarkMightiest\s*=\s*(true|1|yes)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Now parse the ShowdownSet without the Language line
        if (!ShowdownParsing.TryParseAnyLanguage(contentWithoutLanguage, out ShowdownSet? set) || set == null || set.Species == 0)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = "Unable to parse Showdown set. Could not identify the Pokémon species.",
                ShowdownSet = set
            });
        }

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Block shiny requests for species that have NEVER had a legitimate shiny
        // distribution in any game — GO, HOME events, past wondercards, anywhere.
        // Without this guard the pre-made fallback would load a non-shiny file and
        // force-flip the PID to shiny, shipping a file that's illegal at every level
        // (e.g. Cherish Ball Tohoku Pokemon Center Victini as "shiny").
        if (template.Shiny && IsTrulyShinyLocked(template.Species))
        {
            var speciesName = GameInfo.Strings.Species[template.Species];
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = $"**{speciesName} cannot be Shiny.** {speciesName} is shiny-locked in every main series Pokémon game. There is no legal way to obtain a shiny {speciesName}.",
                ShowdownSet = set
            });
        }

        // Block shiny Crown Tundra legendaries on Z-A (PA9) bots — same root cause
        // as the SV/BDSP shiny Deoxys block. Z-A native encounters for these species
        // are shiny-locked (Hyperspace Sky Pillar / Lysandre Labs / etc.). We can
        // route through SwSh Max Lair (shiny-eligible Crown Tundra DA encounter) and
        // convert PK8 → PA9, BUT the resulting file needs a HOME tracker that HOME
        // recognizes. Random/bot-generated trackers fail HOME's server-side validation;
        // the file ships fine to the recipient's game but the game flags it
        // "Non-Native, cannot enter HOME." Block at request time with a clear reason.
        if (template.Shiny && typeof(T) == typeof(PA9)
            && IsHomeRejectingShinyZALegendary(template.Species))
        {
            var speciesName = GameInfo.Strings.Species[template.Species];
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = $"**Shiny {speciesName} can't be delivered via a Legends: Z-A bot in a HOME-compatible way.** {speciesName}'s Z-A encounter is shiny-locked, so no legitimate shiny PA9 exists, and HOME's server-side validation rejects any file with a fabricated tracker.\n\n**Working path:** request shiny {speciesName} from **Celebi-SWSH** or **Jirachi-SWSH** instead — they deliver a Max Lair shiny .pk8 to your SwSh save (Crown Tundra Dynamax Adventure encounter, shiny-eligible). Upload from SwSh → HOME (HOME assigns a fresh real tracker), then you can pull it down into Legends: Z-A through HOME if you want it in your Z-A save.",
                ShowdownSet = set
            });
        }

        // Block shiny requests for species whose only legitimate shiny path is GO →
        // HOME transfer with a real HOME-issued tracker. Bot pre-made files are
        // tool-generated, not extracted from real GO accounts, so HOME's server-side
        // validation rejects them (error 10015 for GO-origin, 999 for LG-origin) even
        // when PKHeX considers the file legal. Restrict to SV (PK9) and BDSP (PB8)
        // where this has been empirically confirmed.
        if (template.Shiny && (typeof(T) == typeof(PK9) || typeof(T) == typeof(PB8))
            && IsHomeRejectingShinyMythical(template.Species))
        {
            var speciesName = GameInfo.Strings.Species[template.Species];
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = $"**Shiny {speciesName} cannot be traded on this bot.** Shiny {speciesName} only legitimately exists via Pokémon GO → HOME transfer with a real HOME tracker. Bot-generated files fail Pokémon HOME's server-side validation (error 10015 on deposit). Request a non-shiny instead.",
                ShowdownSet = set
            });
        }

        // Block shiny requests for species that are shiny-locked in Pokemon Legends: Z-A.
        // These species may have legal shinies in OTHER games (GO transfers, past events,
        // BDSP eggs) but Z-A specifically locks them. Without this check ALM picks a
        // non-Z-A encounter source and ships the Pokemon as Non-Native — which is exactly
        // what we don't want on a Z-A bot.
        if (typeof(T) == typeof(PA9) && template.Shiny && IsZALockedShiny(template.Species))
        {
            var speciesName = GameInfo.Strings.Species[template.Species];
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = $"**{speciesName} cannot be Shiny in Pokemon Legends: Z-A.** This species is shiny-locked in Z-A. Even though shiny variants exist via GO transfers or past events, they cannot be legally traded into Z-A as shiny.\n\nUse `/guide shinylock` for the full per-game list.",
                ShowdownSet = set
            });
        }

        // Block requests for species/form combos where Z-A's native encounter is locked
        // to a specific form. ALM picks an older-gen encounter for the requested form,
        // which produces a PA9 the receiving Z-A game rejects at the Link Trade screen
        // with "This trade can't be completed because there is a problem with your
        // trade partner's Pokémon."
        if (typeof(T) == typeof(PA9))
        {
            var formBlockReason = GetZAFormBlockReason(template.Species, template.Form);
            if (formBlockReason != null)
            {
                var speciesName = GameInfo.Strings.Species[template.Species];
                return Task.FromResult(new ProcessedPokemonResult<T>
                {
                    Error = $"**This {speciesName} form cannot be traded on Pokémon Legends: Z-A.** {formBlockReason}",
                    ShowdownSet = set
                });
            }

            // Z-A native Zygarde uses Power Construct ability, not Aura Break. ALM
            // produces form 0/1 + Aura Break which Z-A's encounter signature rejects.
            // We post-fix the PA9 after ALM in the generation block below, but flag
            // the case here for log clarity. (set.Form is read-only on ShowdownSet,
            // so we can't pre-fix the template — handled post-ALM instead.)
        }

        // Filter out batch commands (.) and filters (~) from invalid lines - these are handled by ALM
        // Also filter out custom fields like Language: and Alpha: which are ALM-specific
        var actualInvalidLines = set.InvalidLines.Where(line =>
        {
            var text = line.Value?.Trim();
            if (string.IsNullOrEmpty(text))
                return false;

            // Skip batch commands and filters
            if (text.StartsWith('.') || text.StartsWith('~'))
                return false;

            // Skip custom ALM fields
            if (text.StartsWith("Language:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Alpha:", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }).ToList();

        if (actualInvalidLines.Count != 0)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = $"Unable to parse Showdown Set:\n{string.Join("\n", actualInvalidLines.Select(l => l.Value))}",
                ShowdownSet = set
            });
        }

        // DON'T use language-specific trainer! It causes encounter errors.
        // Generate with normal trainer (English), then set language after generation.
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();

        PKM? pkm = null;
        string result = "";

        // Generate egg or normal pokemon based on isEgg flag
        if (isEgg)
        {
            // Create a proper RegenTemplate from the ShowdownSet
            var regenTemplate = new RegenTemplate(set);

            // Generate egg using ALM
            pkm = sav.GenerateEgg(regenTemplate, out var eggResult);
            result = eggResult.ToString();
        }
        else
        {
            // SwSh-only species requested for SV → adjust template to match a legal
            // SwSh encounter, then route through PK8 → HOME → PK9. ALM can't legally
            // generate these directly as PK9. Each species has fixed valid encounters:
            //   Non-shiny = story level; Shiny = level 100 wondercard.
            var swshEnc = GetSwShLegalEncounter(template.Species, template.Shiny);
            bool isSwShOnly = swshEnc.HasValue;
            bool needsSwShRouting = typeof(T) == typeof(PK9) && isSwShOnly;

            // Z-A shiny Crown Tundra DA legendaries: Z-A native encounters at Hyperspace
            // locations are shiny-locked. ALM's natural choice falls back to BDSP or other
            // games with met locations like "Crystal Cavern" that Z-A flags as Non-Native
            // (can't enter HOME). Force SwSh Max Lair (level 70, shiny-eligible Dynamax
            // Adventure encounter) then convert PK8 → PA9 via HOME so the file gets a
            // valid HOME tracker and Max Lair met location.
            bool needsSwShToPa9 = typeof(T) == typeof(PA9) && template.Shiny
                && IsCrownTundraDAShinyForZA(template.Species);
            if (needsSwShToPa9)
            {
                swshEnc = (70, true, (byte?)null); // Max Lair: level 70, shiny-eligible
                needsSwShRouting = true;
                LogUtil.LogInfo($"[TradeModule] Z-A shiny {template.Species}: forcing SwSh Max Lair → HOME → PA9 routing", "Helpers");
            }

            if (needsSwShRouting)
            {
                var (legalLevel, legalShiny, legalForm) = swshEnc!.Value;
                LogUtil.LogInfo($"[TradeModule] Species {template.Species}: forcing SwSh encounter (level={legalLevel}, shiny={legalShiny}) then routing through HOME", "Helpers");

                // Rebuild the showdown set text with legal encounter constraints.
                // Strip any existing Level/Shiny lines and replace with valid ones.
                var origText = set.Text;
                var lines = origText.Split('\n')
                    .Where(l => !l.TrimStart().StartsWith("Level:", StringComparison.OrdinalIgnoreCase)
                             && !l.TrimStart().StartsWith("Shiny:", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                lines.Add($"Level: {legalLevel}");
                if (legalShiny) lines.Add("Shiny: Yes");
                var adjusted = new ShowdownSet(string.Join("\n", lines));
                var adjustedTemplate = AutoLegalityWrapper.GetTemplate(adjusted);

                ITrainerInfo swshSav = new SimpleTrainerInfo(GameVersion.SW)
                {
                    OT = sav.OT, TID16 = sav.TID16, SID16 = sav.SID16, Language = sav.Language,
                };
                var swshPkm = swshSav.GetLegal(adjustedTemplate, out var swshResult);
                if (swshPkm != null && swshPkm.Species == template.Species)
                {
                    var swshLa = new LegalityAnalysis(swshPkm);
                    if (swshLa.Valid)
                    {
                        swshPkm.RefreshChecksum();
                        var converted = EntityConverter.ConvertToType(swshPkm, typeof(T), out var _);
                        if (converted is PKM convTarget && convTarget.Species == template.Species)
                        {
                            // Add a HOME tracker — required for Z-A/SV to recognize the
                            // file as a legitimate HOME transfer (rather than "Non-Native,
                            // cannot enter HOME"). EntityConverter.ConvertToType handles
                            // the format change but doesn't always set the tracker, so do
                            // it explicitly here when the target supports it and the
                            // tracker is empty.
                            if (convTarget is IHomeTrack hometrk && hometrk.Tracker == 0)
                            {
                                var trkBytes = new byte[8];
                                Random.Shared.NextBytes(trkBytes);
                                hometrk.Tracker = BitConverter.ToUInt64(trkBytes, 0);
                                convTarget.RefreshChecksum();
                                LogUtil.LogInfo($"[TradeModule] Added HOME tracker to converted {convTarget.GetType().Name} for species {convTarget.Species}", "Helpers");
                            }

                            // For PK9, try every Tera Type until one passes legality.
                            // PKHeX expects specific values that vary by encounter — for HOME-transferred
                            // Pokemon, valid Tera Types are typically the Pokemon's primary or secondary type.
                            convTarget.RefreshChecksum();
                            var convLa = new LegalityAnalysis(convTarget);

                            if (!convLa.Valid && convTarget is PK9 pk9 && convLa.Report().Contains("Tera Type"))
                            {
                                var pi = PersonalTable.SV.GetFormEntry(pk9.Species, pk9.Form);
                                var teraOptions = new List<byte> { pi.Type1, pi.Type2 };
                                // Add all 18 types as fallback candidates
                                for (byte t = 0; t < 18; t++) if (!teraOptions.Contains(t)) teraOptions.Add(t);

                                LegalityAnalysis? bestLa = null;
                                foreach (var teraType in teraOptions)
                                {
                                    pk9.TeraTypeOriginal = (MoveType)teraType;
                                    pk9.TeraTypeOverride = (MoveType)19;
                                    pk9.RefreshChecksum();
                                    var testLa = new LegalityAnalysis(pk9);
                                    if (testLa.Valid)
                                    {
                                        bestLa = testLa;
                                        LogUtil.LogInfo($"[TradeModule] Tera Type {teraType} valid for species {template.Species}", "Helpers");
                                        break;
                                    }
                                }
                                convLa = bestLa ?? convLa;
                            }

                            if (convLa.Valid)
                            {
                                pkm = convTarget;
                                result = "SwSh→HOME→PK9";
                                LogUtil.LogInfo($"[TradeModule] PK8→PK9 via HOME succeeded for {template.Species}", "Helpers");
                            }
                            else
                            {
                                LogUtil.LogInfo($"[TradeModule] All Tera Types failed: {convLa.Report().Split('\n')[0]}", "Helpers");
                                try
                                {
                                    var legalized = convTarget.LegalizePokemon();
                                    if (legalized is PKM lp && lp.Species == template.Species)
                                    {
                                        var lpLa = new LegalityAnalysis(lp);
                                        LogUtil.LogInfo($"[TradeModule] ALM legalize result: Valid={lpLa.Valid}, {lpLa.Report().Split('\n')[0]}", "Helpers");
                                        if (lpLa.Valid)
                                        {
                                            pkm = lp;
                                            result = "SwSh→HOME→PK9 (legalized)";
                                        }
                                    }
                                    else
                                    {
                                        LogUtil.LogInfo($"[TradeModule] Legalize returned null or wrong species", "Helpers");
                                    }
                                }
                                catch (Exception ex) { LogUtil.LogError($"[TradeModule] Legalize exception: {ex.Message}", "Helpers"); }
                            }
                        }
                    }
                    else
                    {
                        LogUtil.LogInfo($"[TradeModule] PK8 invalid: {swshLa.Report().Split('\n')[0]}", "Helpers");
                    }
                }
                else
                {
                    LogUtil.LogInfo($"[TradeModule] SwSh gen returned null/wrong species (result={swshResult})", "Helpers");
                }

                // If ALM/conversion routing failed, fall back to a pre-made PK9 file from
                // the HOME-Ready-Files library. These are known-legal files for SwSh-only
                // species that PKHeX/ALM can't reliably regenerate.
                if (pkm == null)
                {
                    try
                    {
                        var preMadeFolder = @"C:\Users\ericr\OneDrive\Desktop\HOME-Ready-Files";
                        if (Directory.Exists(preMadeFolder))
                        {
                            var prefix = template.Species.ToString("D4");
                            // Match e.g. "0890 ★ - Eternatus - HEX.pk9" (shiny) or "0891 - Kubfu - HEX.pk9" (non-shiny).
                            // Try both shiny and non-shiny patterns since the user's preference may not
                            // have a matching legal file (e.g. shiny-only Eternatus).
                            var patterns = new[] { $"{prefix} ★ -*.pk9", $"{prefix} -*.pk9", $"{prefix}-*.pk9" };
                            foreach (var pat in patterns)
                            {
                                var files = Directory.GetFiles(preMadeFolder, pat);
                                if (files.Length > 0)
                                {
                                    var bytes = File.ReadAllBytes(files[0]);
                                    var loaded = EntityFormat.GetFromBytes(bytes, EntityContext.Gen9);
                                    if (loaded is T preMade && preMade.Species == template.Species)
                                    {
                                        var preMadeLa = new LegalityAnalysis(preMade);
                                        if (preMadeLa.Valid)
                                        {
                                            pkm = preMade;
                                            result = "PreMadeFile";
                                            LogUtil.LogInfo($"[TradeModule] Loaded pre-made file {Path.GetFileName(files[0])} for species {template.Species}", "Helpers");
                                            break;
                                        }
                                        else
                                        {
                                            LogUtil.LogInfo($"[TradeModule] Pre-made file {Path.GetFileName(files[0])} failed legality: {preMadeLa.Report().Split('\n')[0]}", "Helpers");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { LogUtil.LogError($"[TradeModule] Pre-made file load exception: {ex.Message}", "Helpers"); }
                }

                // Last resort: original ALM call (will produce broken pkm and error handling kicks in)
                if (pkm == null)
                    pkm = wantsRaidMark
                        ? sav.GetLegalForTradeRaidPriority(template, out result)
                        : sav.GetLegalForTrade(template, out result);
            }
            else
            {
                // Use normal template for regular Pokémon (raid-first when a Mightiest Mark
                // is requested so the 7-star raid encounter wins over wild/egg).
                pkm = wantsRaidMark
                    ? sav.GetLegalForTradeRaidPriority(template, out result)
                    : sav.GetLegalForTrade(template, out result);

                // The host runs NativeOnly game priority (keeps trades native to the bot's game).
                // That blocks cross-gen-only event mons with NO native encounter — e.g.
                // Ash-Greninja (Greninja form 1 / Battle Bond), a Gen-7 Sun/Moon event. When
                // native generation fails, retry allowing all games so ALM can pull the
                // legitimate event. Only applied on failure and only kept if the result is valid,
                // so it can't regress normal native trades.
                if (!wantsRaidMark && (result == "Failed" || pkm == null))
                {
                    var anyGame = sav.GetLegalForTradeAnyGame(template, out var anyResult);
                    if (anyGame != null && anyGame.Species == template.Species && new LegalityAnalysis(anyGame).Valid)
                    {
                        pkm = anyGame;
                        result = anyResult;
                        LogUtil.LogInfo($"[TradeModule] {template.Species}: native gen failed, used cross-game encounter (e.g. older-gen event)", "Helpers");
                    }
                }

                // Legends Z-A (PA9) & Legends: Arceus (PA8): Scale 255 ALWAYS means Alpha in these
                // games. PKHeX's data doesn't enforce it, so a member who asks for Scale 255 without
                // "Alpha: Yes" gets one of two broken outcomes: a non-Alpha size-255 mon (HOME rejects
                // on deposit), OR — as Rex hit — ALM silently resets the scale to a legal value (e.g.
                // 97) so the request is lost. We therefore detect the REQUESTED scale from the raw set
                // (not the generated pkm, whose scale may already be reset), and whenever 255 is asked
                // for in these games we regenerate the set as an Alpha (legitimately size 255) and
                // label it Alpha even if it wasn't explicitly requested. Keep the original only when no
                // valid Alpha can be made (e.g. a species with no Alpha encounter). Alpha exists ONLY
                // in PLA/ZA, so nothing here touches other games.
                bool wantsScale255 = System.Text.RegularExpressions.Regex.IsMatch(
                    contentWithoutLanguage, @"(?im)^\s*\.?(Scale|HeightScalar)\s*=\s*255\s*$");
                bool alreadyAlpha255 = pkm is PA9 { IsAlpha: true, Scale: 255 } || pkm is PA8 { IsAlpha: true, Scale: 255 };
                if (wantsScale255 && (pkm is PA9 or PA8) && !alreadyAlpha255
                    && !contentWithoutLanguage.Contains("Alpha:", StringComparison.OrdinalIgnoreCase))
                {
                    var alphaTemplate = AutoLegalityWrapper.GetTemplate(new ShowdownSet(contentWithoutLanguage + "\nAlpha: Yes"));
                    var alphaPkm = sav.GetLegalForTrade(alphaTemplate, out var alphaResult);
                    if ((alphaPkm is PA9 { IsAlpha: true, Scale: 255 } || alphaPkm is PA8 { IsAlpha: true, Scale: 255 })
                        && new LegalityAnalysis(alphaPkm).Valid)
                    {
                        pkm = alphaPkm;
                        result = alphaResult;
                        LogUtil.LogInfo($"[TradeModule] Legends {pkm.Species}: Scale 255 forced Alpha (size 255 = Alpha in PLA/ZA)", "Helpers");
                    }
                }

                // BDSP legendary met location override: ALM tends to pick roaming/event
                // encounter slots that produce the wrong met location (e.g. Valley Windworks
                // for Cresselia, Newmoon Island-2 for Darkrai). Force canonical locations —
                // but ONLY when ALM's output is invalid. PKHeX has duplicate location IDs
                // for the same place (e.g. Newmoon Island at id 332 AND 333) and ALM picks
                // the encounter-matching variant; the resolver here picks the first one,
                // which may not match the encounter ALM used. If the current file is
                // already valid, leave it alone — the override would break it.
                if (pkm is PB8 pb8 && pkm.Species is 480 or 481 or 482 or 485 or 487 or 488 or 491 or 492)
                {
                    var preOverrideLa = new LegalityAnalysis(pb8);
                    if (preOverrideLa.Valid)
                    {
                        LogUtil.LogInfo($"[BDSP-LOC] species={pkm.Species} preLoc={pb8.MetLocation} — already valid, skipping override", "Helpers");
                    }
                    else
                    {
                        ResolveBDSPLocationIds();
                        ushort canonicalLoc = pkm.Species switch
                        {
                            480 => _lakeAcuityId,        // Uxie
                            481 => _lakeVerityId,        // Mesprit
                            482 => _lakeValorId,         // Azelf
                            485 => _starkMountainId,     // Heatran
                            487 => _turnbackCaveId,      // Giratina
                            488 => _fullmoonIslandId,    // Cresselia
                            491 => _newmoonIslandId,     // Darkrai
                            492 => _flowerParadiseId,    // Shaymin
                            _ => pkm.MetLocation,
                        };
                        LogUtil.LogInfo($"[BDSP-LOC] species={pkm.Species} preLoc={pb8.MetLocation} target={canonicalLoc} (file invalid, attempting override)", "Helpers");
                        if (canonicalLoc != 0 && pb8.MetLocation != canonicalLoc)
                        {
                            pb8.MetLocation = canonicalLoc;
                            pb8.RefreshChecksum();
                            LogUtil.LogInfo($"[BDSP-LOC] postLoc={pb8.MetLocation}", "Helpers");
                        }
                    }
                }

                // Mythical species (Celebi, Jirachi, etc.) often fail ALM generation in SwSh
                // because of shiny-lock or encounter restrictions. Fall back to pre-made files
                // from HOME-Ready-Files when ALM produces an invalid result.
                // Also covers Z-A legendaries whose Power Construct / Hyperspace encounter
                // signatures ALM can't reproduce (Zygarde and friends) — for PA9 only.
                bool isGoMyth = IsGoShinyMythical(template.Species);
                // Z-A pre-made files cover NON-SHINY native catches (Wild Zone / Hyperspace
                // encounters are shiny-locked) AND specific species with legitimate shiny
                // pre-made files in HOME-Ready-Files (Zygarde from 2018 Legends event,
                // etc.). For shiny requests on species WITHOUT a dedicated shiny pre-made,
                // fall through to ALM so it routes through SwSh→HOME→PA9 (Max Lair shiny
                // is encounter-eligible) instead of force-flipping a non-shiny file's PID.
                bool hasShinyPreMade = template.Species is 718  // Zygarde — 2018 Legends event
                    or 807; // Zeraora — SWSH WC8 HOME Distribution shiny (0807 ★ ...pa9, "a lovely place"); native Z-A Hyperspace Lumiose is shiny-LOCKED so ALM would ship an illegal shiny
                bool isZALegPre = typeof(T) == typeof(PA9)
                    && IsZALegendaryWithPreMade(template.Species)
                    && (!template.Shiny || hasShinyPreMade);
                bool isMythical = isGoMyth || isZALegPre;
                LogUtil.LogInfo($"[TradeModule] DIAG mythical-gate: species={template.Species} typeof(T)={typeof(T).Name} shiny={template.Shiny} isGoMyth={isGoMyth} isZALegPre={isZALegPre} isMythical={isMythical}", "Helpers");
                if (isMythical)
                {
                    var fallbackCheck = pkm == null || pkm.Species != template.Species
                        || !new LegalityAnalysis(pkm).Valid;
                    // For Z-A legendaries with a real source file in HOME-Ready-Files,
                    // ALWAYS use the pre-made — ALM may produce a "valid-looking" PA9
                    // here, but downstream OT/language enforcement mutates it into an
                    // invalid encounter that the receiving Z-A game refuses. Only when
                    // isZALegPre is true (already gated on !shiny above), so this won't
                    // hijack shiny requests that should route through SwSh.
                    if (isZALegPre)
                        fallbackCheck = true;
                    // Same trap applies to GO-Mythicals on PK8/PK9/PB8: ALM picks an
                    // event Mystery-Gift encounter with a fixed OT, then AutoOT swaps the
                    // OT to the trade partner's and the file fails the Misc legality
                    // check in-game ("Trainer/Misc mismatch for encounter"). Force the
                    // pre-made route — HOME-Ready-Files carries event-OT-compatible files
                    // for these species and the bot's trade-time ApplyAutoOT will swap OT
                    // cleanly because the pre-made's encounter is non-fixed-OT.
                    if (isGoMyth)
                        fallbackCheck = true;
                    // EXCEPTION: several mythicals are natively obtainable in BDSP with the
                    // player's own OT (in-game static catch or gift/egg), so AutoOT swaps
                    // cleanly and we should prefer ALM's native encounter over the pre-made
                    // file (the pre-made PB8s are PLA/Gen8a transplants that show "Non-Native
                    // & Has Home Tracker"). Only un-forces the fallback when ALM already
                    // produced a VALID native mon — if ALM fails, isGoMyth keeps
                    // fallbackCheck=true and the pre-made is used, so this can only improve.
                    //
                    // Verified via PKHeX (BDSP priority, EncounterStatic8b/WB8/EncounterEgg8b):
                    //   Shaymin (492) / Darkrai (491) / Arceus (493): shiny=Random — native for
                    //     BOTH shiny and non-shiny.
                    //   Manaphy (490) / Phione (489): the BDSP Manaphy gift is shiny-locked, so
                    //     ONLY non-shiny is native; a shiny request must keep using the pre-made.
                    // (491/493 aren't in IsGoShinyMythical so they already ship native; listed
                    // here too for robustness — harmless.)
                    bool bdspNativeAnyShiny = typeof(T) == typeof(PB8) && template.Species is 491 or 492 or 493;
                    bool bdspNativeNonShinyOnly = typeof(T) == typeof(PB8) && (template.Species is 489 or 490) && !template.Shiny;
                    if ((bdspNativeAnyShiny || bdspNativeNonShinyOnly)
                        && pkm != null && pkm.Species == template.Species
                        && new LegalityAnalysis(pkm).Valid)
                    {
                        fallbackCheck = false;
                        LogUtil.LogInfo($"[TradeModule] BDSP native mythical {template.Species} (shiny={template.Shiny}): using ALM native encounter, skipping pre-made file", "Helpers");
                    }
                    bool svNativeMeloetta = typeof(T) == typeof(PK9) && template.Species == 648;
                    if (svNativeMeloetta && !template.Shiny
                        && pkm != null && pkm.Species == template.Species
                        && new LegalityAnalysis(pkm).Valid
                        && (pkm is not IHomeTrack svMelHt || svMelHt.Tracker == 0))
                    {
                        fallbackCheck = false;
                        LogUtil.LogInfo($"[TradeModule] SV native Meloetta: using ALM native catch (no HOME tracker), skipping Worlds18 pre-made", "Helpers");
                    }
                    LogUtil.LogInfo($"[TradeModule] DIAG fallback-gate: pkm-null={pkm == null} speciesMatch={(pkm != null && pkm.Species == template.Species)} fallbackCheck={fallbackCheck}", "Helpers");

                    // Z-A native check: if ALM produced a PA9 with a Z-A met location for a
                    // Z-A native species, KEEP it — don't fall back to pre-made event files
                    // (which would have "a lovely place" met location instead of Hyperspace
                    // Lumiose / Wild Zone). Bypass legality flags too — the encounter is real.
                    // Per Serebii's Z-A legendary locations: Mewtwo (Lysandre Labs), Latias/Latios
                    // (Hyperspace Lumiose), Kyogre (Hyperspace Primordial Sea), Groudon (Hyperspace
                    // Desolate Land), Rayquaza (Hyperspace Sky Pillar), Heatran (Hyperspace Infernal
                    // Arena), Darkrai (Hyperspace Newmoon Nightmare), Swords of Justice (Hyperspace
                    // Lumiose), Keldeo/Meloetta/Genesect (Hyperspace Lumiose), Floette-Eternal,
                    // Xerneas (Wild Zone 11), Yveltal (Rouge Sector 2), Zygarde (Wild Zone 20),
                    // Diancie (Magenta Sector 8), Hoopa/Volcanion/Magearna/Melmetal (Hyperspace
                    // Lumiose), Marshadow/Meltan (Rouge Sector 1), Zeraora (Hyperspace Lumiose).
                    // Z-A bypass: native catch (loc 100-350) OR HOME-transferred (loc 30000+).
                    // Either way, keep ALM's PA9 instead of falling back to pre-made files.
                    bool isInZANativeList = template.Species is 150 or 151 or 251 or 380 or 381 or 382 or 383 or 384 or 385 or 386
                            or 485 or 489 or 490 or 491 or 492 or 493 or 494
                            or 638 or 639 or 640 or 647 or 648 or 649
                            or 670 or 716 or 717 or 718 or 719 or 720 or 721
                            or 801 or 802 or 807 or 808 or 809;
                    bool isZANativeFromALM = pkm is PA9 &&
                        (pkm.MetLocation is > 0 and <= 350 || pkm.MetLocation >= 30000) &&
                        isInZANativeList &&
                        !IsZALegendaryWithPreMade(template.Species); // species with real .pa9 source files override ALM
                    if (isZANativeFromALM) fallbackCheck = false;

                    // Z-A native fresh catches (loc 100-350) shouldn't have a tracker —
                    // they're wild caught. HOME-transferred (30000+) keep their tracker
                    // since they actually went through HOME.
                    bool isZAFreshWild = pkm is PA9 && pkm.MetLocation is > 0 and <= 350;
                    if (isZAFreshWild && pkm is IHomeTrack zaHomeTrack && zaHomeTrack.HasTracker)
                    {
                        zaHomeTrack.Tracker = 0;
                        pkm.RefreshChecksum();
                    }

                    // ZA shiny Volcanion: always use the pre-made file regardless of ALM
                    // result, so the authentic event values (lv50, fixed IVs, original moves)
                    // ship instead of ALM's "make it competitive" 6IV/lv100 build.
                    if (typeof(T) == typeof(PA9) && template.Species == 721 && template.Shiny)
                        fallbackCheck = true;
                    // Shiny Diancie (GO Tour Kalos 2026 release): force the pre-made GO-origin
                    // file across SwSh/SV/Z-A so the authentic GO transfer values (random IVs,
                    // GO moveset, level 15, GO origin) ship instead of an ALM-built 6IV/lv100.
                    // Skip PB8 — BDSP can't legally receive Diancie via GO transfer.
                    if (template.Species == 719 && template.Shiny && typeof(T) != typeof(PB8))
                        fallbackCheck = true;
                    if (fallbackCheck)
                    {
                        try
                        {
                            var preMadeFolder = @"C:\Users\ericr\OneDrive\Desktop\HOME-Ready-Files";
                            if (Directory.Exists(preMadeFolder))
                            {
                                var prefix = template.Species.ToString("D4");
                                var ext = typeof(T) == typeof(PK9) ? ".pk9"
                                        : typeof(T) == typeof(PK8) ? ".pk8"
                                        : typeof(T) == typeof(PB8) ? ".pb8"
                                        : typeof(T) == typeof(PA8) ? ".pa8"
                                        : typeof(T) == typeof(PA9) ? ".pa9"
                                        : typeof(T) == typeof(PB7) ? ".pb7"
                                        : ".pkm";
                                var ctx = typeof(T) == typeof(PK9) ? EntityContext.Gen9
                                        : typeof(T) == typeof(PK8) ? EntityContext.Gen8
                                        : typeof(T) == typeof(PB8) ? EntityContext.Gen8b
                                        : typeof(T) == typeof(PA8) ? EntityContext.Gen8a
                                        : typeof(T) == typeof(PA9) ? EntityContext.Gen9
                                        : typeof(T) == typeof(PB7) ? EntityContext.Gen7b
                                        : EntityContext.Gen9;
                                // Form-specific filename pattern: "0144-01" for Articuno-Galar (form 1).
                                // Default form (0) uses just "0144" without the form suffix.
                                var formSuffix = template.Form > 0 ? $"-{template.Form:D2}" : "";
                                // Shiny order: prefer ANY genuine shiny file (form-specific then base-form)
                                // BEFORE falling back to PID-flipping a non-shiny pre-made — the flip
                                // produces an illegal shiny when the file's encounter is shiny-locked
                                // (Zygarde-Complete from Wild Zone 20, Hoopa from Hyperspace Lumiose, etc.).
                                // Patterns for the REQUESTED shininess vs. the opposite (flip).
                                var matchPatterns = template.Shiny
                                    ? new[] { $"{prefix}{formSuffix} ★ -*{ext}", $"{prefix} ★ -*{ext}" }
                                    : new[] { $"{prefix}{formSuffix} -*{ext}", $"{prefix} -*{ext}" };
                                var flipPatterns = template.Shiny
                                    ? new[] { $"{prefix}{formSuffix} -*{ext}", $"{prefix} -*{ext}" }
                                    : new[] { $"{prefix}{formSuffix} ★ -*{ext}", $"{prefix} ★ -*{ext}" };
                                // Only flip an opposite-shininess file when NO file of the requested
                                // shininess exists. When BOTH exist (e.g. Zeraora: non-shiny native
                                // "0807 -" = Hyperspace Lumiose, shiny event "0807 ★" = "a lovely
                                // place") they must NOT cross — otherwise a non-shiny request grabs the
                                // shiny file and flips it (wrong met location), and vice versa.
                                bool hasMatchingShinyFile = matchPatterns.Any(p => Directory.GetFiles(preMadeFolder, p).Length > 0);
                                var patterns = hasMatchingShinyFile ? matchPatterns : matchPatterns.Concat(flipPatterns).ToArray();
                                LogUtil.LogInfo($"[TradeModule] Mythical fallback START: species={template.Species}, ext={ext}, shiny={template.Shiny}, formSuffix={formSuffix}", "Helpers");
                                bool foundAny = false;
                                foreach (var pat in patterns)
                                {
                                    var files = Directory.GetFiles(preMadeFolder, pat);
                                    LogUtil.LogInfo($"[TradeModule] Pattern '{pat}' matched {files.Length} file(s)", "Helpers");
                                    if (files.Length == 0) continue;
                                    foundAny = true;
                                    foreach (var file in files)
                                    {
                                        var bytes = File.ReadAllBytes(file);
                                        var loaded = EntityFormat.GetFromBytes(bytes, ctx);
                                        if (loaded == null)
                                        {
                                            LogUtil.LogInfo($"[TradeModule] EntityFormat.GetFromBytes returned null for {Path.GetFileName(file)}", "Helpers");
                                            continue;
                                        }
                                        if (loaded is not T preMade)
                                        {
                                            LogUtil.LogInfo($"[TradeModule] Wrong type: loaded={loaded.GetType().Name} but expected {typeof(T).Name}", "Helpers");
                                            continue;
                                        }
                                        if (preMade.Species != template.Species)
                                        {
                                            LogUtil.LogInfo($"[TradeModule] Wrong species in {Path.GetFileName(file)}: got {preMade.Species}, expected {template.Species}", "Helpers");
                                            continue;
                                        }

                                        // Force shiny preference to match what user requested — but
                                        // never flip a truly shiny-locked species (Victini, Phione,
                                        // Manaphy, Arceus, etc.). The entry-point guard already
                                        // rejects those requests; this is defense-in-depth in case a
                                        // caller skips the entry check.
                                        //
                                        // PID == EC invariant: some encounter generators (notably the
                                        // pre-Gen-6 HOME-transferred mons from Emerald / FRLG / DPPt
                                        // -> BDSP carry PID == EncryptionConstant. If the file we're
                                        // about to flip had that invariant, mirror EC to the new PID
                                        // so the encounter signature stays consistent (otherwise
                                        // legality fails with "PID should be equal to EC!").
                                        var preFlipPid = preMade.PID;
                                        var preFlipEC  = preMade.EncryptionConstant;
                                        bool pidEqualsEC = preFlipPid == preFlipEC;
                                        if (template.Shiny && !preMade.IsShiny && !IsTrulyShinyLocked(template.Species))
                                        {
                                            preMade.SetIsShiny(true);
                                            if (pidEqualsEC) preMade.EncryptionConstant = preMade.PID;
                                            preMade.RefreshChecksum();
                                        }
                                        else if (!template.Shiny && preMade.IsShiny)
                                        {
                                            int tries = 0;
                                            while (preMade.IsShiny && tries++ < 100000)
                                                preMade.PID = (uint)Random.Shared.Next(int.MinValue, int.MaxValue);
                                            if (pidEqualsEC) preMade.EncryptionConstant = preMade.PID;
                                            preMade.RefreshChecksum();
                                        }
                                        // Honor the member's customizable spread on Z-A pre-made files the
                                        // LEGALITY-PRESERVING way. These Floette-Eternal-style gift encounters
                                        // are legal as-is (PID/EC/IVs/Nature locked to a Xoroshiro seed), so we
                                        // only apply changes the seed permits: EVs (free), moves (relearn pool),
                                        // a Nature MINT (StatNature), and Hyper Training for IVs. We do NOT
                                        // SetIVs, overwrite Nature, or change the ability -- each of those breaks
                                        // the seed correlation (verified by probe). The result stays genuinely
                                        // legal; the only flag that can remain is a custom held item PKHeX hasn't
                                        // catalogued for Z-A yet, which the narrow item bypass below ships.
                                        if ((preMade is PA9 || preMade is PK9) && set != null)
                                        {
                                            try
                                            {
                                                // LEVEL — raise only, never lower. Event/DB mons (e.g. a HOME-tracker GO Mew)
                                                // ship at their distribution level (15); leveling UP keeps the met level intact
                                                // and is always legal, while lowering below the caught level is not. Act only on
                                                // an explicit "Level: N" request (reqLevel 0 = unspecified → keep the file's level,
                                                // so collectors still get the as-is event mon when they don't ask for a level).
                                                int pmReqLevel = 0;
                                                var lvlLine = set.GetSetLines().FirstOrDefault(l => l.TrimStart().StartsWith("Level", StringComparison.OrdinalIgnoreCase));
                                                if (lvlLine != null) { var ci = lvlLine.IndexOf(':'); if (ci >= 0) int.TryParse(lvlLine[(ci + 1)..].Trim(), out pmReqLevel); }
                                                if (pmReqLevel > preMade.CurrentLevel && pmReqLevel <= 100)
                                                {
                                                    preMade.CurrentLevel = (byte)pmReqLevel;
                                                    LogUtil.LogInfo($"[TradeModule] Pre-made {preMade.Species}: leveled up to {pmReqLevel} (was lower; met level unchanged)", "Helpers");
                                                }

                                                if (set.EVs is { Length: 6 } && Array.Exists(set.EVs, e => e > 0))
                                                    preMade.SetEVs(set.EVs);

                                                var reqMoves = Array.FindAll(set.Moves ?? Array.Empty<ushort>(), m => m != 0);
                                                if (reqMoves.Length > 0)
                                                {
                                                    // Some pre-made encounters can't legally learn every requested
                                                    // move (e.g. Iron Head isn't in the native Z-A Zeraora's pool,
                                                    // though it IS on the SwSh event Zeraora). If the requested set
                                                    // makes THIS file's moves illegal, keep the file's original legal
                                                    // moveset — far better than failing legality and falling through
                                                    // to a different file (wrong met location / shininess).
                                                    var origMoves = new ushort[4];
                                                    preMade.GetMoves(origMoves.AsSpan());
                                                    preMade.SetMoves(reqMoves);
                                                    preMade.HealPP();
                                                    preMade.RefreshChecksum();
                                                    var mvReport = new LegalityAnalysis(preMade).Report();
                                                    if (mvReport.Contains("Invalid Move", StringComparison.OrdinalIgnoreCase)
                                                        || mvReport.Contains("can't be learned", StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        preMade.SetMoves(origMoves);
                                                        preMade.HealPP();
                                                        LogUtil.LogInfo($"[TradeModule] Pre-made {preMade.Species}: a requested move is illegal for this encounter — kept the file's original legal moveset", "Helpers");
                                                    }
                                                }

                                                // Held item — the ONE field that still trips PKHeX on a Z-A
                                                // gift ("Held item is unreleased"): PKHeX's Gen9a item table is
                                                // incomplete, but the item is real in game. Applied here; the
                                                // narrow held-item-only bypass below ships it. Everything else
                                                // in this block is written the LEGALITY-PRESERVING way so the
                                                // file stays genuinely legal (correct PID, no correlation flag).
                                                // Apply the requested held item, and CLEAR any save-file leftover
                                                // when none was requested — real-save Z-A extractions often carry
                                                // a stray held item (e.g. a Master Ball the Floette was holding),
                                                // which can't legally be held and would otherwise ship on the mon.
                                                // set.HeldItem is 0 when the member didn't ask for one.
                                                preMade.HeldItem = set.HeldItem;

                                                // Nature — MINT only. A Z-A gift's actual Nature is locked to its
                                                // Xoroshiro seed; overwriting pk.Nature breaks the PID correlation.
                                                // StatNature is the in-game mint and is fully legal, so the mon
                                                // DISPLAYS the requested nature while staying legal.
                                                if (set.Nature != Nature.Random)
                                                    preMade.StatNature = set.Nature;

                                                // IVs — Hyper Training (bottle caps) only. SetIVs would break the
                                                // seed correlation; HT makes every stat BATTLE at 31 at Lv50+
                                                // while the stored IVs (and the seed) stay intact and legal.
                                                if (preMade.CurrentLevel >= 50 && preMade is IHyperTrain htReq)
                                                {
                                                    if (preMade.IV_HP  < 31) htReq.HT_HP  = true;
                                                    if (preMade.IV_ATK < 31) htReq.HT_ATK = true;
                                                    if (preMade.IV_DEF < 31) htReq.HT_DEF = true;
                                                    if (preMade.IV_SPA < 31) htReq.HT_SPA = true;
                                                    if (preMade.IV_SPD < 31) htReq.HT_SPD = true;
                                                    if (preMade.IV_SPE < 31) htReq.HT_SPE = true;
                                                }

                                                // Ability — intentionally NOT changed. A Z-A gift's ability is
                                                // rolled from its seed; SetAbility breaks the PID correlation and
                                                // is genuinely illegal (no Ability Patch path for Z-A gifts yet).
                                                // The natural ability is kept so the mon stays legal. (Verified by
                                                // probe: changing ability is the only non-item change that flags.)

                                                preMade.RefreshChecksum();
                                                LogUtil.LogInfo($"[TradeModule] Pre-made {preMade.Species}: applied EVs/moves/item + nature mint + HT IVs (legality-preserving; ability kept natural)", "Helpers");
                                            }
                                            catch (Exception ex) { LogUtil.LogError($"[TradeModule] Pre-made customization failed: {ex.Message}", "Helpers"); }
                                        }
                                        var preMadeLa = new LegalityAnalysis(preMade);
                                        var preMadeReport = preMadeLa.Report();
                                        bool isHomeWondercardMismatch = !preMadeLa.Valid &&
                                            preMadeReport.Contains("Unable to match to a Mystery Gift", StringComparison.OrdinalIgnoreCase);
                                        // Pre-made GO-shiny mythicals (Melmetal, Celebi, Jirachi, etc.) carry a Met Date
                                        // from when the GO event was live. Once the distribution window closes, PKHeX
                                        // flags the date as stale even though the Pokemon itself was legitimately
                                        // obtained. The file is from a trusted local source, so ship it anyway.
                                        bool isStaleMetDate = !preMadeLa.Valid &&
                                            preMadeReport.Contains("Met Date is outside of distribution window", StringComparison.OrdinalIgnoreCase);
                                        // Z-A wild encounters (PA9, met loc 1-350) extracted from a real Z-A save
                                        // are real files even when PKHeX flags "Unable to match an encounter from
                                        // origin game" — PKHeX's encounter database for Z-A is incomplete. The file
                                        // is literally from someone's actual Z-A game, so the receiving Z-A game
                                        // accepts it. Trust the source.
                                        bool isZAEncounterMissingInPKHeX = !preMadeLa.Valid &&
                                            preMade is PA9 &&
                                            preMade.MetLocation is > 0 and <= 350 &&
                                            preMadeReport.Contains("Unable to match an encounter from origin game", StringComparison.OrdinalIgnoreCase);

                                        // BDSP-only bypass: Gen-3 origin (Emerald Birth Island Deoxys, FRLG event mons)
                                        // carry a seed-locked PID/EC/IV correlation. When the bot flips the PID for
                                        // shiny↔non-shiny conversion, the correlation breaks and PKHeX flags
                                        // "PID+ correlation does not match" + Mystery-Gift / Ribbon / Fateful chatter.
                                        // The file is still a real event mon from a trusted source — the receiving
                                        // BDSP game accepts it because BDSP's own validator is looser than PKHeX's.
                                        // Scope: PB8 only, file's original metadata still shows event-encounter shape.
                                        bool isBDSPPidFlipCorrelation = !preMadeLa.Valid &&
                                            preMade is PB8 &&
                                            (preMadeReport.Contains("PID+ correlation does not match", StringComparison.OrdinalIgnoreCase)
                                             || preMadeReport.Contains("PID should be equal to EC", StringComparison.OrdinalIgnoreCase)
                                             || preMadeReport.Contains("Unable to match to a Mystery Gift", StringComparison.OrdinalIgnoreCase));

                                        // Z-A gift pre-mades (Floette-Eternal, story gifts) are LEGAL as-is and,
                                        // after our legality-preserving customization above, the only flag that
                                        // can remain is "Held item is unreleased" -- PKHeX's Gen9a item table is
                                        // incomplete, but the item is genuinely a Z-A item. Ship those. The
                                        // "PID+ correlation" / "PID should be equal to EC" strings are kept ONLY
                                        // as a safety net for a genuinely seed-broken extraction (e.g. an older
                                        // shiny-flipped file); the normal customized path no longer trips them.
                                        // Scope: PA9 only.
                                        bool isZAPidCorrelation = !preMadeLa.Valid &&
                                            preMade is PA9 &&
                                            (preMadeReport.Contains("Held item is unreleased", StringComparison.OrdinalIgnoreCase)
                                             || preMadeReport.Contains("PID+ correlation does not match", StringComparison.OrdinalIgnoreCase)
                                             || preMadeReport.Contains("PID should be equal to EC", StringComparison.OrdinalIgnoreCase));

                                        if (preMadeLa.Valid || isHomeWondercardMismatch || isStaleMetDate || isZAEncounterMissingInPKHeX || isBDSPPidFlipCorrelation || isZAPidCorrelation)
                                        {
                                            pkm = preMade;
                                            result = "PreMadeFile";

                                            // DO NOT fabricate a HOME tracker here. A pre-made/event file's tracker is a
                                            // REAL one that HOME itself issued, and it is the only reason the file is
                                            // accepted on deposit. The previous "anti-clone" code overwrote it with a
                                            // random value on every trade, on the premise that HOME only checks for
                                            // DUPLICATE trackers. That premise is wrong: HOME checks ISSUANCE — it asks
                                            // whether it ever handed that tracker out — so an invented value is a forgery
                                            // and every copy is rejected ("didn't go to HOME"). It passed a local "probe"
                                            // only because PKHeX cannot query HOME's servers; PKHeX legality != HOME
                                            // acceptance. Net effect was strictly worse than the problem it tried to
                                            // solve: instead of only the first member being able to deposit, NOBODY could.
                                            // Leave the real tracker exactly as-is.
                                            string note;
                                            if (preMadeLa.Valid) note = "";
                                            else if (isHomeWondercardMismatch) note = " (HOME wondercard, PKHeX too old to validate — shipping anyway)";
                                            else if (isZAEncounterMissingInPKHeX) note = " (Z-A real-save extraction, PKHeX encounter data incomplete — shipping anyway)";
                                            else if (isBDSPPidFlipCorrelation) note = " (BDSP Gen-3-origin PID-flip correlation — file is real event mon, BDSP accepts; shipping anyway)";
                                            else if (isZAPidCorrelation) note = " (Z-A gift — legal except a held item PKHeX hasn't catalogued for Z-A yet; item is real, shipping)";
                                            else note = " (Met Date past distribution window — trusted pre-made file, shipping anyway)";
                                            LogUtil.LogInfo($"[TradeModule] Mythical fallback SUCCESS: loaded {Path.GetFileName(file)} (shiny={preMade.IsShiny}){note}", "Helpers");
                                            goto fallbackDone;
                                        }
                                        else
                                        {
                                            LogUtil.LogInfo($"[TradeModule] Pre-made {Path.GetFileName(file)} failed legality: {preMadeLa.Report().Split('\n')[0]}", "Helpers");
                                        }
                                    }
                                }
                                if (!foundAny)
                                    LogUtil.LogInfo($"[TradeModule] No matching pre-made files found for species {template.Species} in {preMadeFolder}", "Helpers");
                                fallbackDone:;
                            }
                        }
                        catch (Exception ex) { LogUtil.LogError($"[TradeModule] Mythical fallback exception: {ex.Message}", "Helpers"); }
                    }
                }
            }
        }

        if (pkm == null)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = "Set took too long to legalize.",
                ShowdownSet = set
            });
        }

        // ============================================================================
        // NON-FIXED-OT ENCOUNTER PREFERENCE
        // ============================================================================
        // If ALM chose a fixed-OT encounter (gift, static, NPC trade) when a wild or
        // egg encounter also exists for this species, prefer the wild/egg encounter.
        // This lets users request any language and receive their trade partner's OT.
        // Falls back silently to the original result if no wild/egg alternative exists
        // (e.g. Floette-Eternal, Zarude — species with no wild/egg encounter at all).
        // ============================================================================
        if (!isEgg && !wantsRaidMark)
        {
            var fixedOtCheck = new LegalityAnalysis(pkm);
            if (fixedOtCheck.Valid && AutoLegalityWrapper.IsFixedOT(fixedOtCheck.EncounterOriginal, pkm))
            {
                var wildAlt = AutoLegalityWrapper.TryGetAsWildOrEgg(sav, template);
                if (wildAlt != null)
                {
                    pkm = wildAlt;
                    result = "Regenerated";
                }
            }
        }
        // ============================================================================
        // END OF NON-FIXED-OT ENCOUNTER PREFERENCE
        // ============================================================================

        // ============================================================================
        // FORM CORRECTION FOR COSMETIC AND REGIONAL FORMS (e.g., Vivillon patterns)
        // ============================================================================
        // ALM's GetLegal generates the Pokemon in its encounter-default form, which for
        // species like Vivillon is always the same base form (e.g., Meadow) regardless of
        // what form was requested in the ShowdownSet.  Apply the requested form here so
        // the downstream legality check validates the correct form.
        // ============================================================================
        // Skip form correction for pre-made files — their form IS the correct
        // game-native form (e.g. Zygarde form 3 = Power Construct, the real Z-A
        // encounter). Overwriting with set.Form would break the encounter signature
        // and cause "ability mismatch" / "encounter mismatch" failures in-game.
        //
        // Exceptions — species whose form is cosmetic / freely changeable in-game and
        // whose encounter signature does NOT depend on the stored form byte:
        //   386 Deoxys     — Meteor Pieces transform Normal↔Attack↔Defense↔Speed
        //   412 Burmy      — weather-based, cosmetic in newer gens
        //   421 Cherrim    — overworld weather
        //   479 Rotom      — appliance forms set by interacting with each appliance
        //   555 Darmanitan — Zen mode toggled by ability, can store either
        //   648 Meloetta   — Relic Song toggle
        //   720 Hoopa      — Prison Bottle toggle
        // For these, applying set.Form on top of the pre-made is the right move.
        bool isFormFreelyChangeable = pkm.Species is 386 or 412 or 421 or 479 or 555 or 648 or 720;
        if (!isEgg && pkm.Form != set.Form && (result != "PreMadeFile" || isFormFreelyChangeable))
        {
            pkm.Form = set.Form;
            pkm.ResetPartyStats();
            pkm.RefreshChecksum();
        }
        // ============================================================================
        // END OF FORM CORRECTION
        // ============================================================================

        // ============================================================================
        // SCATTERBUG / SPEWPA FORM FIX
        // ============================================================================
        // ShowdownParsing does not expose named forms for Scatterbug or Spewpa, so
        // set.Form is always 0 regardless of what the user typed (e.g. "Scatterbug-Sun").
        // The general form correction above therefore never fires for these species.
        // Parse the form suffix from the raw content line ourselves and match it against
        // Vivillon's form name list — Scatterbug and Spewpa share the exact same 20
        // regional patterns.
        // ============================================================================
        if (!isEgg && (pkm.Species == (ushort)Species.Scatterbug || pkm.Species == (ushort)Species.Spewpa))
        {
            var scatterLines = contentWithoutLanguage.Split('\n');
            var scatterFirstLine = scatterLines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;
            // Strip held item if present: "Scatterbug-Sun @ Oran Berry" → "Scatterbug-Sun"
            var scatterSpeciesPart = scatterFirstLine.Split('@')[0].Trim();
            var scatterDashIdx = scatterSpeciesPart.IndexOf('-');
            if (scatterDashIdx >= 0)
            {
                var scatterFormSuffix = scatterSpeciesPart[(scatterDashIdx + 1)..].Trim();
                var vivillonFormNames = FormConverter.GetFormList(
                    (ushort)Species.Vivillon,
                    GameInfo.Strings.Types,
                    GameInfo.Strings.forms,
                    GameInfo.GenderSymbolASCII,
                    EntityContext.Gen9);
                for (byte f = 0; f < vivillonFormNames.Length; f++)
                {
                    // Game strings use spaces ("Icy Snow"); user types dashes ("Icy-Snow")
                    if (vivillonFormNames[f].Replace(" ", "-").Equals(scatterFormSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (pkm.Form != f)
                        {
                            pkm.Form = f;
                            pkm.ResetPartyStats();
                            pkm.RefreshChecksum();
                        }
                        break;
                    }
                }
            }
        }
        // ============================================================================
        // END OF SCATTERBUG / SPEWPA FORM FIX
        // ============================================================================

        // ============================================================================
        // DITTO METLOCATION FIX
        // ============================================================================
        // Fix Ditto MetLocation for game version compatibility
        // ALM may select encounters from different games (e.g., SV location for SWSH trade)
        // This ensures Ditto has a valid MetLocation for the target game
        // Only apply the fix if Ditto is currently invalid to avoid overriding correct locations
        // ============================================================================
        if (pkm.Species == 132) // Species 132 = Ditto
        {
            var initialDittoLA = new LegalityAnalysis(pkm);
            if (!initialDittoLA.Valid)
            {
                // Ditto is invalid, try to fix MetLocation
                pkm.MetLocation = pkm switch
                {
                    PB8 => 400,  // BDSP: Grand Underground
                    PK9 => 28,   // SV: South Province (Area Three)
                    _ => 162,    // PK8 (SWSH): Route 5 / Wild Area
                };

                // Revalidate after fixing MetLocation and apply trash bytes fix
                var dittoLA = new LegalityAnalysis(pkm);
                pkm = (T)TradeExtensions<T>.TrashBytes(pkm, dittoLA); // CRITICAL: Assign result back!
            }
            else
            {
                // Ditto is already valid, just apply trash bytes without changing MetLocation
                pkm = (T)TradeExtensions<T>.TrashBytes(pkm, initialDittoLA);
            }
        }
        // ============================================================================
        // END OF DITTO METLOCATION FIX
        // ============================================================================

        // ============================================================================
        // MESPRIT BDSP MET LOCATION FIX
        // PKHeX's BDSP encounter table lists Mesprit (species 481) at met location 197
        // (Valley Windworks), which is geographically wrong — Mesprit is encountered at
        // Lake Verity in the canonical game flow. Patch met location 197 → 325 (Lake
        // Verity / Verity Cavern) on PB8 Mesprits so members see the correct lore-accurate
        // location in the trade embed. Uxie (loc 331 = Acuity Cavern) and Azelf
        // (loc 328 = Valor Cavern) are already correct in PKHeX's data.
        // ============================================================================
        if (pkm is PB8 && pkm.Species == 481 && pkm.MetLocation == 197)
        {
            pkm.MetLocation = 325; // Lake Verity (Verity Cavern)
            pkm.RefreshChecksum();
        }
        // ============================================================================
        // END OF MESPRIT METLOCATION FIX
        // ============================================================================

        // ============================================================================
        // KELDEO SWSH MOVESET FIX
        // PKHeX's EncounterStatic8 Keldeo (Crown Tundra Ballimere Lake catch) defines
        // only Aqua Jet as the catch-default move — the other 3 slots ship empty (0),
        // resulting in a level-100 Keldeo with a single useless move. Members get a
        // broken-looking Pokémon. Fill the empty slots with a sensible level-up moveset
        // (Sacred Sword / Hydro Pump / Swords Dance) — all legal for any-level Keldeo.
        // PK8 (SwSh) only per user request.
        // ============================================================================
        if (pkm is PK8 && pkm.Species == 647) // Keldeo
        {
            var existing = new ushort[4];
            pkm.GetMoves(existing.AsSpan());
            // Apply only when slots 2-4 are blank (the broken catch-default state).
            if (existing[1] == 0 && existing[2] == 0 && existing[3] == 0)
            {
                pkm.SetMoves([453, 533, 56, 14]); // Aqua Jet, Sacred Sword, Hydro Pump, Swords Dance
                pkm.HealPP();
                pkm.RefreshChecksum();
            }
        }
        // ============================================================================
        // END OF KELDEO SWSH MOVESET FIX
        // ============================================================================

        var spec = GameInfo.Strings.Species[template.Species];

        // Apply standard item logic only for non-eggs
        if (!isEgg)
        {
            ApplyStandardItemLogic(pkm);
        }

        // Capture the language ALM assigned before we override it.
        // Used later to revert if the user's language conflicts with a fixed-OT encounter.
        byte almGeneratedLanguage = (byte)pkm.Language;

        // Set language early so 6IV/Tera checks see the correct language.
        // Also fix the OT length and nickname for Asian languages here, before the
        // FIXED-OT FALLBACK runs its LegalityAnalysis. Without this, PKHeX April-15
        // fails with "OT Name too long" (6-char limit for Asian) and "Nickname does
        // not match species name" (ClearNickname stores "" instead of e.g. "イーブイ"),
        // which incorrectly triggers the fallback and forces English on every request.
        // Species name is set via GameInfo.GetStrings to avoid running LegalityAnalysis
        // early, which would risk NPC-trade encounter matching via the returned LA.
        if (pkm is T pkBeforeCheck)
        {
            pkBeforeCheck.Language = finalLanguage;

            // Asian languages enforce a 6-char OT limit in PKHeX.
            // Mirror what PrepareForTrade does at the end so the fallback LA sees a valid OT.
            if ((finalLanguage == (byte)LanguageID.Japanese ||
                 finalLanguage == (byte)LanguageID.Korean ||
                 finalLanguage == (byte)LanguageID.ChineseS ||
                 finalLanguage == (byte)LanguageID.ChineseT) &&
                pkBeforeCheck.OriginalTrainerName.Length > 6)
            {
                const string asianOT = "王犬米";
                pkBeforeCheck.OriginalTrainerName = asianOT;
                // Simple property assignment leaves stale trash bytes from the previous
                // longer OT ("FreeMons.Org"), which PKHeX's Trainer check flags as invalid.
                // Clear them explicitly using the same pattern PrepareForTrade uses.
                Span<byte> trashBuf = stackalloc byte[pkBeforeCheck.TrashCharCountTrainer * 2];
                int trashLen = pkBeforeCheck.SetString(trashBuf, asianOT.AsSpan(), pkBeforeCheck.TrashCharCountTrainer, StringConverterOption.ClearZero);
                pkBeforeCheck.OriginalTrainerTrash.Clear();
                trashBuf[..trashLen].CopyTo(pkBeforeCheck.OriginalTrainerTrash);
                pkBeforeCheck.RefreshChecksum();
            }

            if (string.IsNullOrEmpty(set.Nickname))
            {
                pkBeforeCheck.Nickname = SpeciesName.GetSpeciesNameGeneration(pkBeforeCheck.Species, pkBeforeCheck.Language, pkBeforeCheck.Format);
                pkBeforeCheck.IsNicknamed = false;
            }
        }

        // ============================================================================
        // MAX LAIR POKEMON MOVE POPULATION BUG WORKAROUND
        // ============================================================================
        // PKHeX.Core.dll (as of 01-22-2026, commit fe32739) has a bug where Max Lair
        // Pokemon from SWSH Crown Tundra do not get moves automatically populated
        // during legalization, causing them to be marked as illegal.
        //
        // This workaround manually populates moves for Max Lair encounters after
        // generation but before validation.
        // ============================================================================
        if (pkm is PK8 pk8 && !isEgg)
        {
            const int MaxLairLocationID = 244; // Max Lair in Crown Tundra
            bool hasNoMoves = pk8.Move1 == 0 && pk8.Move2 == 0 && pk8.Move3 == 0 && pk8.Move4 == 0;
            bool isFromMaxLair = pk8.MetLocation == MaxLairLocationID;

            if (hasNoMoves && isFromMaxLair)
            {
                // Populate moves using PKHeX (not ALM)
                pk8.SetSuggestedMoves();
                pk8.HealPP();
                pk8.RefreshChecksum();
            }
        }
        // ============================================================================
        // END OF MAX LAIR FIX
        // ============================================================================

        // Generate LGPE code if needed
        List<Pictocodes>? lgcode = null;
        if (pkm is PB7)
        {
            lgcode = GenerateRandomPictocodes(3);
            if (pkm.Species == (int)Species.Mew && pkm.IsShiny)
            {
                return Task.FromResult(new ProcessedPokemonResult<T>
                {
                    Error = "Mew can **not** be Shiny in LGPE. PoGo Mew does not transfer and Pokeball Plus Mew is shiny locked.",
                    ShowdownSet = set
                });
            }
        }

        // ============================================================================
        // SV TERA TYPE OVERRIDE FIX FOR NON-NATIVE POKEMON
        // ============================================================================
        // Non-native Pokemon (from other games) in SV require TeraTypeOverride to be
        // explicitly set. PKHeX does not set this automatically, and ALM fails to
        // legalize these Pokemon as a result. Apply the fix before the legality check
        // so the analysis sees the corrected value.
        // ============================================================================
        if (pkm is PK9 pk9TeraFix && !isEgg)
        {
            bool isSVNative = pk9TeraFix.Version is GameVersion.SL or GameVersion.VL;
            if (!isSVNative)
            {
                // Non-native Pokemon (HOME transfers from other games) need both
                // TeraTypeOriginal and TeraTypeOverride explicitly set or PKHeX marks them illegal.
                //
                // ALM incorrectly assigns Type2 as TeraTypeOriginal for dual-type non-native
                // Pokemon (e.g. Dialga → Dragon instead of Steel, Rayquaza → Flying instead of
                // Dragon, Lugia → Flying instead of Psychic). The correct value is always Type1.
                var correctOriginal = (MoveType)pk9TeraFix.PersonalInfo.Type1;
                pk9TeraFix.TeraTypeOriginal = correctOriginal;

                if (userSpecifiedTeraType.HasValue)
                    pk9TeraFix.TeraTypeOverride = userSpecifiedTeraType.Value;
                else
                    pk9TeraFix.TeraTypeOverride = correctOriginal;
            }
            else if (userSpecifiedTeraType.HasValue)
            {
                // Native SV Pokemon: user requested a specific Tera Type, apply only to Override.
                pk9TeraFix.TeraTypeOverride = userSpecifiedTeraType.Value;
            }
        }
        // ============================================================================
        // END OF SV TERA TYPE OVERRIDE FIX
        // ============================================================================

        // ============================================================================
        // 6IV DEFAULT ENFORCEMENT (ALL GAMES EXCEPT ZA)
        // ============================================================================
        // If the user did not specify IVs in their Showdown set, attempt to set all
        // IVs to 31. If this makes the Pokemon illegal (e.g. event with fixed IVs),
        // the original PKHeX-generated IVs are restored.
        // For PA9 (Legends Z-A), wild encounters roll random IVs, so direct 6IV is
        // illegal. Use Hyper Training instead — keeps natural IVs but sets HT flags
        // so in-game stats compute as if IVs were 31.
        // ============================================================================
        if (!userSpecifiedIVs && result != "PreMadeFile")
        {
            var pkBackup = pkm.Clone();
            pkm.IVs = [31, 31, 31, 31, 31, 31];
            if (pkm is IHyperTrain htFix)
                htFix.HyperTrainClear();
            pkm.RefreshChecksum();
            if (!new LegalityAnalysis(pkm).Valid)
            {
                // 6IVs are not legal for this encounter — restore original values.
                pkm = pkBackup;
            }
        }
        // ============================================================================
        // END OF 6IV DEFAULT ENFORCEMENT
        // ============================================================================

        // ============================================================================
        // FIXED-OT ENCOUNTER LANGUAGE FALLBACK
        // ============================================================================
        // Some encounters (e.g. Floette-Eternal / AZ in Legends Z-A, or in-game trade
        // Pokémon like Eevee in SV after PKHeX Apr-15 update) require a specific OT that
        // is only valid for certain languages. If the user's requested language causes a
        // legality failure that vanishes when we revert to the language ALM originally
        // chose, silently use the encounter-compatible language instead.
        // effectiveLanguage tracks the final language choice so PrepareForTrade does not
        // re-apply finalLanguage and undo this fallback.
        // ============================================================================
        byte effectiveLanguage = finalLanguage;
        LogUtil.LogInfo($"[LANGUAGE TRACE] Fixed-OT check: pkm.Language={pkm.Language}, almGeneratedLanguage={almGeneratedLanguage}, finalLanguage={finalLanguage}", "Helpers");
        if ((byte)pkm.Language != almGeneratedLanguage)
        {
            var langCheckLa = new LegalityAnalysis(pkm);
            LogUtil.LogInfo($"[LANGUAGE TRACE] Legality with Language={pkm.Language}: Valid={langCheckLa.Valid}, Report={langCheckLa.Report()}", "Helpers");
            if (!langCheckLa.Valid)
            {
                LogUtil.LogInfo($"[LANGUAGE TRACE] REVERTING to almGeneratedLanguage={almGeneratedLanguage}!", "Helpers");
                pkm.Language = almGeneratedLanguage;
                effectiveLanguage = almGeneratedLanguage;
                if (string.IsNullOrEmpty(set.Nickname))
                {
                    pkm.SetDefaultNickname(new LegalityAnalysis(pkm));
                    pkm.IsNicknamed = false;
                }

                if (!new LegalityAnalysis(pkm).Valid)
                {
                    // Reverting didn't help — restore the user's language so the
                    // downstream error message reflects the real failure.
                    pkm.Language = finalLanguage;
                    effectiveLanguage = finalLanguage;
                    if (string.IsNullOrEmpty(set.Nickname))
                    {
                        pkm.SetDefaultNickname(new LegalityAnalysis(pkm));
                        pkm.IsNicknamed = false;
                    }
                }
            }
        }
        // ============================================================================
        // END OF FIXED-OT ENCOUNTER LANGUAGE FALLBACK
        // ============================================================================

        // Now that effectiveLanguage is resolved, set the default nickname once.
        // The language is already correct on pkm; the FIXED-OT FALLBACK handles its own
        // nickname updates internally when it reverts, so this covers the normal path.
        if (string.IsNullOrEmpty(set.Nickname))
        {
            pkm.SetDefaultNickname(new LegalityAnalysis(pkm));
            pkm.IsNicknamed = false;
        }

        // Force non-shiny when user did NOT request shiny but the encounter generated shiny
        // (e.g. Z-A static legendaries like Groudon at Hyperspace Desolate Land)
        if (!set.Shiny && !isEgg && pkm.IsShiny)
        {
            // Re-roll PID until non-shiny while preserving other PID-derived attributes
            uint origPid = pkm.PID;
            for (int i = 0; i < 100; i++)
            {
                pkm.PID = unchecked(pkm.PID + 0x10000);
                if (!pkm.IsShiny) break;
            }
            pkm.RefreshChecksum();
        }

        var la = new LegalityAnalysis(pkm);

        // Tera Type retry for SV Pokemon when LA reports Tera Type mismatch
        // (Event Pokemon like Meloetta have fixed Tera Types that ALM may not set correctly)
        if (!la.Valid && pkm is PK9 pk9TeraRetry && la.Report().Contains("Tera Type"))
        {
            var pi = PersonalTable.SV.GetFormEntry(pk9TeraRetry.Species, pk9TeraRetry.Form);
            var teraOptions = new List<byte> { pi.Type1, pi.Type2 };
            for (byte t = 0; t < 18; t++) if (!teraOptions.Contains(t)) teraOptions.Add(t);
            var origOriginal = pk9TeraRetry.TeraTypeOriginal;
            var origOverride = pk9TeraRetry.TeraTypeOverride;
            foreach (var teraType in teraOptions)
            {
                pk9TeraRetry.TeraTypeOriginal = (MoveType)teraType;
                pk9TeraRetry.TeraTypeOverride = (MoveType)19;
                pk9TeraRetry.RefreshChecksum();
                var testLa = new LegalityAnalysis(pk9TeraRetry);
                if (testLa.Valid)
                {
                    la = testLa;
                    LogUtil.LogInfo($"[TradeModule] Tera Type {teraType} valid for species {pk9TeraRetry.Species}", "Helpers");
                    break;
                }
            }
            if (!la.Valid)
            {
                pk9TeraRetry.TeraTypeOriginal = origOriginal;
                pk9TeraRetry.TeraTypeOverride = origOverride;
                pk9TeraRetry.RefreshChecksum();
            }
        }

        // Hoopa's real Hyperspace Lumiose met location is 30034 (PKHeX-validated). The
        // previous override that rewrote any 30000+ value to 273 broke the legitimate
        // 30034 pre-made — removed. The pre-made file at HOME-Ready-Files/0720 - Hoopa -
        // *.pa9 carries the correct value and is a Z-A native catch (no HOME tracker), so
        // AutoOT works through the tracker-strip path below.
        bool isZANativeFreshCatch = pkm is PA9 && (pkm.MetLocation is > 0 and <= 350 || pkm.MetLocation == 30034);
        if (isZANativeFreshCatch && pkm is IHomeTrack { HasTracker: true } zaTrack)
        {
            zaTrack.Tracker = 0;
            pkm.RefreshChecksum();
            la = new LegalityAnalysis(pkm);
        }

        // Add HOME tracker for SV/SwSh/BDSP/PLA event mythicals that came through HOME.
        // Skip for Z-A native catches — they're fresh wild encounters.
        // Skip for BDSP native catches (PB8 with met loc < 30000) — Ramanas Park
        // Arceus and similar static encounters don't need a HOME tracker; adding one
        // creates a contradictory file (claims native catch + HOME-transferred).
        bool isBDSPNativeCatch = pkm is PB8 && pkm.MetLocation is > 0 and < 30000;
        // Same exemption for SwSh native catches — e.g. Crown Tundra Keldeo at Ballimere Lake
        // (EncounterStatic8, Sword of Justice quest). Member catches it themselves, no HOME
        // tracker exists. Adding a fabricated random one breaks AutoOT (forces it off) and
        // HOME's server-side validation rejects on upload because the tracker isn't registered.
        bool isSWSHNativeCatch = pkm is PK8 && pkm.MetLocation is > 0 and < 30000;
        // Same exemption for PLA native catches — e.g. Coronet Highlands Darkrai and Shaymin
        // (EncounterStatic8a, unlocked by BDSP/SwSh save data on the console). The member
        // catches these in-game, so no HOME tracker exists. Fabricating a random one forces
        // AutoOT off ("Home tracker detected. Can't apply AutoOT.") and HOME rejects the file
        // on upload because the tracker isn't server-registered. ALM already produces a valid
        // tracker-less PA8 here, so just skip the add and AutoOT applies cleanly.
        bool isPLANativeCatch = pkm is PA8 && pkm.MetLocation is > 0 and < 30000;
        // Same exemption for SV native catches — e.g. the Blueberry Academy (Indigo Disk)
        // Meloetta at Coastal Biome (EncounterStatic9, met loc 176, Scale 128). The member
        // catches it in-game, so no HOME tracker exists; fabricating one makes the file
        // contradictory (native catch + HOME tracker) and PKHeX then can't match the
        // origin-game encounter ("Unable to match an encounter from origin game"). ALM
        // already produces a valid tracker-less PK9, so skip the add.
        bool isSVNativeCatch = pkm is PK9 && pkm.MetLocation is > 0 and < 30000;
        if (la.Valid && pkm is IHomeTrack { HasTracker: false } homeTrack && !isZANativeFreshCatch && !isBDSPNativeCatch && !isSWSHNativeCatch && !isPLANativeCatch && !isSVNativeCatch)
        {
            // Mythicals/events typically distributed via HOME - need a HOME tracker for legality
            ushort[] homeTrackedSpecies =
            {
                (ushort)Species.Mew, (ushort)Species.Celebi, (ushort)Species.Jirachi, (ushort)Species.Deoxys,
                (ushort)Species.Phione, (ushort)Species.Manaphy, (ushort)Species.Darkrai, (ushort)Species.Shaymin,
                (ushort)Species.Arceus, (ushort)Species.Victini, (ushort)Species.Keldeo, (ushort)Species.Meloetta,
                (ushort)Species.Genesect, (ushort)Species.Diancie, (ushort)Species.Hoopa, (ushort)Species.Volcanion,
                (ushort)Species.Magearna, (ushort)Species.Marshadow, (ushort)Species.Zeraora, (ushort)Species.Meltan,
                (ushort)Species.Melmetal, (ushort)Species.Zarude, (ushort)Species.Pecharunt,
            };
            if (Array.IndexOf(homeTrackedSpecies, pkm.Species) >= 0)
            {
                var trackerBytes = new byte[8];
                Random.Shared.NextBytes(trackerBytes);
                homeTrack.Tracker = BitConverter.ToUInt64(trackerBytes, 0);
                pkm.RefreshChecksum();
                la = new LegalityAnalysis(pkm);
            }
        }

        // Auto-fix language-related nickname mismatches for sets without a nickname
        if (!la.Valid && string.IsNullOrEmpty(set.Nickname))
        {
            if (la.Results.Any(r => r.Identifier is CheckIdentifier.Nickname))
            {
                // Set the correct species name for the current language instead of clearing
                // to "" — ClearNickname stores an empty string which fails the nickname check
                // for Asian languages (PKHeX expects e.g. "イーブイ", not "").
                pkm.Nickname = SpeciesName.GetSpeciesNameGeneration(pkm.Species, pkm.Language, pkm.Format);
                pkm.IsNicknamed = false;
                la = new LegalityAnalysis(pkm);
            }
        }

        // Handle past gen file requests (PK8, PA8, PB8, PK9) - fix BEFORE returning error
        if (!la.Valid && pkm is T && la.Results.Any(m => m.Identifier is CheckIdentifier.Memory))
        {
            var clone = (T)(object)pkm.Clone();
            clone.HandlingTrainerName = pkm.OriginalTrainerName;
            clone.HandlingTrainerGender = pkm.OriginalTrainerGender;
            if (clone is PK8 or PA8 or PB8 or PK9)
                ((dynamic)clone).HandlingTrainerLanguage = (byte)pkm.Language;
            clone.CurrentHandler = 1;
            var laClone = new LegalityAnalysis(clone);
            if (laClone.Valid)
            {
                pkm = clone;
                la = laClone;
            }
        }

        // ============================================================================
        // MAX LAIR SHINY FALLBACK
        // ============================================================================
        // If a shiny PK8 is still invalid and not already at Max Lair, retry with
        // MetLocation=244. Many SWSH legendaries and Ultra Beasts are shiny-eligible
        // only via Dynamax Adventures (Max Lair). ALM sometimes generates them at
        // the correct location but without moves, or fails to set the shiny PID.
        // ============================================================================
        if (!la.Valid && pkm is PK8 pk8Retry && set.Shiny && pk8Retry.MetLocation != 244)
        {
            var pk8RetryClone = (PK8)pk8Retry.Clone();
            pk8RetryClone.MetLocation = 244;
            pk8RetryClone.SetSuggestedMoves();
            pk8RetryClone.HealPP();
            pk8RetryClone.RefreshChecksum();
            var laRetry = new LegalityAnalysis(pk8RetryClone);
            if (laRetry.Valid)
            {
                pkm = pk8RetryClone;
                la = laRetry;
            }
        }
        // Also retry if already at Max Lair but still invalid (wrong moves)
        else if (!la.Valid && pkm is PK8 pk8RetryLair && set.Shiny && pk8RetryLair.MetLocation == 244)
        {
            pk8RetryLair.SetSuggestedMoves();
            pk8RetryLair.HealPP();
            pk8RetryLair.RefreshChecksum();
            la = new LegalityAnalysis(pk8RetryLair);
        }
        // ============================================================================
        // END OF MAX LAIR SHINY FALLBACK
        // ============================================================================

        // ============================================================================
        // WC8 COMPETITION EVENT FIX — Direct WC8.ConvertToPKM
        // ============================================================================
        // For shiny PK8 from event MetLocations (>= 40000), ALM's generation fails
        // because competition WC8 events have a specific EC/PID generation algorithm
        // that our manual Xoroshiro fix can't reproduce correctly.
        // Instead: load the matching WC8 file from the MGDB and call ConvertToPKM
        // directly — this uses PKHeX's own verified generation logic.
        // ============================================================================
        if (!la.Valid && pkm is PK8 pk8WC && pk8WC.MetLocation >= 40000 && pk8WC.IsShiny)
        {
            var mgdbPath = Info.Hub.Config.Legality.MGDBPath;
            if (Directory.Exists(mgdbPath))
            {
                var wc8Files = Directory.GetFiles(mgdbPath, "*.wc8", SearchOption.AllDirectories);
                foreach (var wc8File in wc8Files)
                {
                    try
                    {
                        var wc8 = new WC8(File.ReadAllBytes(wc8File));
                        if (wc8.Species != pk8WC.Species || wc8.Form != pk8WC.Form)
                            continue;
                        if (wc8.IsShiny == false)
                            continue;

                        var directPkm = wc8.ConvertToPKM(sav);
                        if (directPkm is not T directT)
                            continue;

                        var laWC8 = new LegalityAnalysis(directPkm);
                        LogUtil.LogInfo($"WC8 ConvertToPKM: file={Path.GetFileName(wc8File)} valid={laWC8.Valid} fateful={directPkm.FatefulEncounter} shiny={directPkm.IsShiny}", "Legality");
                        if (laWC8.Valid)
                        {
                            pkm = directPkm;
                            la = laWC8;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogInfo($"WC8 ConvertToPKM error: {ex.Message}", "Legality");
                    }
                }
            }
        }
        // ============================================================================
        // END OF WC8 EVENT FIX
        // ============================================================================

        // ============================================================================
        // PA9 CROSS-GAME HOME FALLBACK
        // ============================================================================
        // When Z-A generation fails for any reason, try every PKM format HOME supports
        // (newest first) and convert the first valid result to PA9. This covers shinies
        // that are locked in Z-A, species with no Z-A encounter, natures/IVs that are
        // only legal in another game, and anything else PKHeX/ALM can't satisfy with
        // the Z-A encounter pool. The converted PA9 retains the origin Version (e.g.
        // SW, SV) so no Z-A-specific logic fires on it downstream.
        // ============================================================================
        // Z-A wild legendaries (Hyperspace Lumiose) must NOT fall back to HOME — the
        // SV/SwSh sources produce older event encounters (e.g. Movie 15 Keldeo with
        // "a lovely place") that ship with the wrong met location. Let ALM's Z-A
        // PA9 reach the isZANativeLegendary bypass below instead.
        bool isZAWildLegendary = pkm is PA9 && template.Species is
            150 or 380 or 381 or 382 or 383 or 384
            or 485 or 491 or 638 or 639 or 640 or 647 or 648 or 649
            or 670 or 716 or 717 or 718 or 719 or 720 or 721
            or 801 or 802 or 807 or 808 or 809;

        // Native Z-A mons (met loc <= 350) sometimes fail ONLY on the cosmetic
        // "Height / Weight statistically improbable" flag because ALM rolled extreme
        // scalars (e.g. Sceptile, Dragonite). Re-roll to mid values; if that was the only
        // issue the mon is now fully legal and STAYS NATIVE instead of falling back to a
        // foreign game (which makes it Non-Native). If other real issues remain, la stays
        // invalid and the foreign fallback below still runs -- so this can only improve.
        if (!la.Valid && pkm is PA9 pa9HW && pkm.MetLocation is > 0 and <= 350)
        {
            var hwReport = la.Report();
            if (hwReport.Contains("Height", StringComparison.OrdinalIgnoreCase)
                && hwReport.Contains("Weight", StringComparison.OrdinalIgnoreCase))
            {
                pa9HW.HeightScalar = 128;
                pa9HW.WeightScalar = 128;
                pa9HW.Scale = 128;
                pa9HW.RefreshChecksum();
                la = new LegalityAnalysis(pa9HW);
                LogUtil.LogInfo($"[TradeModule] Z-A native {pa9HW.Species}: re-rolled Height/Weight/Scale (cosmetic flag), valid now={la.Valid}", "Helpers");
            }
        }

        // Shiny Z-A native: ALM can't build native Z-A *shiny* wild encounters (PKHeX data
        // gap), so it would fall back to SV/XY -> Non-Native (e.g. shiny Beldum/Bagon/Gible).
        // Instead, build the native Z-A NON-shiny (which works) and force it shiny via PID
        // recalc, KEEPING the native Z-A encounter + met location. Z-A wild shinies are legal
        // in-game, so this is a real native shiny. General fix -- covers every Z-A-native
        // species. Non-regressing: if no valid native base is produced, nativeZAShiny stays
        // false and the SV fallback below runs exactly as before.
        bool nativeZAShiny = false;
        // "Already a good native shiny" must be judged by the ENCOUNTER CONTEXT (actually Z-A
        // native), NOT the met-location number — SV locations (e.g. East Paldean Sea) are also
        // low numbers, so the old "MetLocation <= 350" test let a Non-Native SV shiny pass and
        // skipped the native rebuild (shiny Ceruledge/Froakie shipped as SV Non-Native).
        bool currentGoodNativeShiny = la.Valid && pkm is PA9 cgs && cgs.IsShiny && la.EncounterOriginal.Context == cgs.Context;

        // Requested level (if the set had "Level: N"), so the native rebuilds below can honor it
        // when it's legal, instead of leaving the Showdown default of 100.
        int reqLevel = 0;
        {
            var lvlLine = set.GetSetLines().FirstOrDefault(l => l.TrimStart().StartsWith("Level", StringComparison.OrdinalIgnoreCase));
            if (lvlLine != null)
            {
                var ci = lvlLine.IndexOf(':');
                if (ci >= 0) int.TryParse(lvlLine[(ci + 1)..].Trim(), out reqLevel);
            }
        }
        // Set a freshly-built native mon to the LOWEST current level that keeps it fully legal.
        // Dropping "Level: N" leaves the Showdown default of 100, but the requested moves (e.g.
        // Bitter Blade) require a minimum level — below it the mon is illegal, above it is fine.
        // Validity is monotonic in level, so binary-search the lowest legal level in [met, current],
        // then honor the requested level when it's >= that floor, else use the floor (the closest
        // legal level to what was asked). Called on the NON-shiny base (where .Valid is reliable;
        // PKHeX's incomplete Z-A shiny data would otherwise read invalid at every level). Returns
        // the final level so the caller can note when it had to be raised above the request.
        static int ApplyLowestLegalLevel(PKM pk, int requestedLevel)
        {
            byte met = pk.MetLevel < 1 ? (byte)1 : pk.MetLevel;
            int hi = pk.CurrentLevel;
            if (hi <= met) return pk.CurrentLevel;
            int lo = met, h = hi, lowestValid = hi;
            while (lo <= h)
            {
                int mid = (lo + h) / 2;
                pk.CurrentLevel = (byte)mid;
                pk.RefreshChecksum();
                if (new LegalityAnalysis(pk).Valid) { lowestValid = mid; h = mid - 1; }
                else lo = mid + 1;
            }
            int target = (requestedLevel >= lowestValid && requestedLevel <= 100) ? requestedLevel : lowestValid;
            pk.CurrentLevel = (byte)target;
            pk.RefreshChecksum();
            return target;
        }
        // When the clamp raises the level above what the member typed, capture a friendly note so
        // the trade reply can explain WHY (their move list can't exist at that level).
        string? levelAdjustNote = null;

        if (template.Shiny && typeof(T) == typeof(PA9) && !isZAWildLegendary && !currentGoodNativeShiny)
        {
            try
            {
                // Also drop "Level: N" when rebuilding the native base. A too-low requested level
                // (e.g. shiny Ceruledge "Level: 19" with Bitter Blade/Phantom Force) is BELOW the
                // Z-A encounter's met level / move-learn level, so NativeOnly can't satisfy it and
                // the mon leaks to a Non-Native SV encounter. Each move and Shiny individually are
                // native-legal; only the level constraint forces the fallback. Stripping it lets ALM
                // use the encounter's natural level and keep the mon NATIVE (HOME-able). This only
                // runs when the main gen already FAILED to produce a good native shiny, so a valid
                // native level is preserved in the normal case.
                var nsLines = set.GetSetLines().Where(l => !l.TrimStart().StartsWith("Shiny", StringComparison.OrdinalIgnoreCase)
                    && !l.TrimStart().StartsWith("Level", StringComparison.OrdinalIgnoreCase));
                var nsBase = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(new ShowdownSet(string.Join("\n", nsLines))), out _);
                // The rebuilt non-shiny base must itself be a NATIVE Z-A encounter (context match),
                // otherwise SetShiny on a non-native base just re-ships Non-Native.
                if (nsBase is PA9 nativePa9 && nativePa9.Species == template.Species
                    && new LegalityAnalysis(nativePa9).EncounterOriginal.Context == nativePa9.Context)
                {
                    // Fix the cosmetic Height/Weight flag on the base if present (same as Sceptile).
                    var nbReport = new LegalityAnalysis(nativePa9).Report();
                    if (nbReport.Contains("Height", StringComparison.OrdinalIgnoreCase) && nbReport.Contains("Weight", StringComparison.OrdinalIgnoreCase))
                    {
                        nativePa9.HeightScalar = 128; nativePa9.WeightScalar = 128; nativePa9.Scale = 128;
                    }
                    // Honor the requested level (clamped up to the lowest level the moves allow)
                    // instead of the stripped-out default of 100. Done while still non-shiny.
                    var shinyFinalLvl = ApplyLowestLegalLevel(nativePa9, reqLevel);
                    if (reqLevel > 0 && shinyFinalLvl > reqLevel)
                        levelAdjustNote = $"The moves you chose can't be learned at **Level {reqLevel}**, so your {spec} was set to **Level {shinyFinalLvl}** — the lowest level those moves are legal at. A lower level only works with level-appropriate moves.";
                    nativePa9.SetShiny();
                    nativePa9.RefreshChecksum();
                    pkm = nativePa9;
                    la = new LegalityAnalysis(pkm);
                    nativeZAShiny = true;
                    LogUtil.LogInfo($"[TradeModule] Z-A native {pkm.Species}: built native non-shiny then forced shiny (native encounter kept), valid now={la.Valid}", "Helpers");
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] Z-A native-shiny build failed: {ex.Message}", "Helpers"); }
        }

        // Non-shiny Z-A native rescue. The shiny case is handled above; this covers EVERY
        // non-shiny Z-A-native species. The trap here is a *valid-but-Non-Native* PA9: when a
        // requested Level (or move set) can't be satisfied by the native Z-A encounter, ALM
        // falls back to the SV encounter, which IS legal — so every "!la.Valid" rescue below is
        // skipped and the Non-Native SV mon ships ("Cannot enter HOME"). Detect a PA9 whose
        // encounter CONTEXT isn't Gen9a (Non-Native) and rebuild from the request with the Level
        // constraint dropped — a too-low level below the Z-A encounter's met/move-learn level is
        // the usual culprit. Only replaces the result when the rebuild is genuinely NATIVE
        // (context match), so cross-gen species with no Z-A encounter are untouched.
        if (typeof(T) == typeof(PA9) && !template.Shiny && !isZAWildLegendary && !nativeZAShiny
            && pkm is PA9 nonNativePa9 && nonNativePa9.Species == template.Species
            && la.EncounterOriginal.Context != nonNativePa9.Context)
        {
            try
            {
                var nlLines = set.GetSetLines().Where(l => !l.TrimStart().StartsWith("Level", StringComparison.OrdinalIgnoreCase));
                var nlBase = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(new ShowdownSet(string.Join("\n", nlLines))), out _);
                if (nlBase is PA9 nativeNL && nativeNL.Species == template.Species
                    && new LegalityAnalysis(nativeNL).EncounterOriginal.Context == nativeNL.Context)
                {
                    // Honor the requested level (clamped up to the lowest level the moves allow)
                    // instead of the stripped-out default of 100.
                    var nlFinalLvl = ApplyLowestLegalLevel(nativeNL, reqLevel);
                    if (reqLevel > 0 && nlFinalLvl > reqLevel)
                        levelAdjustNote = $"The moves you chose can't be learned at **Level {reqLevel}**, so your {spec} was set to **Level {nlFinalLvl}** — the lowest level those moves are legal at. A lower level only works with level-appropriate moves.";
                    pkm = nativeNL;
                    la = new LegalityAnalysis(pkm);
                    LogUtil.LogInfo($"[TradeModule] Z-A native {pkm.Species}: rebuilt non-shiny native (dropped too-low level), valid now={la.Valid}", "Helpers");
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] Z-A non-shiny native rebuild failed: {ex.Message}", "Helpers"); }
        }

        // FINAL Z-A native safety net. If the result is STILL a Non-Native PA9 for a species
        // that genuinely has a Z-A encounter (Scizor, Absol, etc.), the threaded GetLegalForTrade
        // path returned an SV encounter even though a native one exists. Regenerate via the DIRECT
        // synchronous NativeOnly path (the proven-correct route), then re-apply shininess. Tries
        // the original set first, then with Level stripped (a too-low level can block native).
        // Only replaces the result when the rebuild is genuinely native (context match), so
        // cross-gen-only species (no Z-A encounter) are left exactly as-is.
        if (typeof(T) == typeof(PA9) && !isZAWildLegendary && pkm is PA9 stillNonNative
            && new LegalityAnalysis(stillNonNative).EncounterOriginal.Context != stillNonNative.Context)
        {
            try
            {
                var direct = sav.GetLegalNativeDirect(template);
                if (direct is not PA9)
                {
                    var noLvl = set.GetSetLines().Where(l => !l.TrimStart().StartsWith("Level", StringComparison.OrdinalIgnoreCase));
                    direct = sav.GetLegalNativeDirect(AutoLegalityWrapper.GetTemplate(new ShowdownSet(string.Join("\n", noLvl))));
                    if (direct is PA9 dLvl)
                        ApplyLowestLegalLevel(dLvl, reqLevel);
                }
                if (direct is PA9 dPa9)
                {
                    if (template.Shiny && !dPa9.IsShiny) { dPa9.SetShiny(); }
                    dPa9.RefreshChecksum();
                    pkm = dPa9;
                    la = new LegalityAnalysis(pkm);
                    LogUtil.LogInfo($"[TradeModule] Z-A native safety net: rebuilt {pkm.Species} natively via direct path (was Non-Native), valid={la.Valid}", "Helpers");
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] Z-A native safety net failed: {ex.Message}", "Helpers"); }
        }

        // SV native safety net (same mechanism as Z-A). Rowlet/Dartrix/Kyurem etc. ARE catchable
        // natively in SV (Indigo Disk Terarium / Paldea), but the SV bots don't force native
        // priority, so ALM picks a Pokémon GO / Max Lair transfer and ships Non-Native. For a
        // Non-Native PK9 that has a valid native SV encounter, rebuild via the direct synchronous
        // NativeOnly path — otherwise it ships a foreign-origin mon with NO HOME tracker, which is
        // illegal (e.g. a Spiritomb "caught in SwSh" delivered to SV: HOME flags it hacked).
        // SHINY: only rebuild when the species ISN'T shiny-locked in SV. The SV bots' older PKHeX
        // doesn't enforce shiny-locks, so forcing a native shiny on a locked legendary/paradox could
        // ship an illegal one; IsSvShinyLocked gates those out (Great Tusk/Iron Valiant etc.), while
        // regular species like Spiritomb still get a legit native shiny. GetLegalNativeDirect's
        // internal la.Valid is the final guard.
        if (typeof(T) == typeof(PK9) && (!template.Shiny || !IsSvShinyLocked(template.Species))
            && pkm is PK9 svNonNative && svNonNative.Species == template.Species
            && new LegalityAnalysis(svNonNative).EncounterOriginal.Context != svNonNative.Context)
        {
            try
            {
                var directSv = sav.GetLegalNativeDirect(template);
                if (directSv is PK9 svNative)
                {
                    pkm = svNative;
                    la = new LegalityAnalysis(pkm);
                    LogUtil.LogInfo($"[TradeModule] SV native safety net: rebuilt {pkm.Species} natively via direct path (was Non-Native), valid={la.Valid}", "Helpers");
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] SV native safety net failed: {ex.Message}", "Helpers"); }
        }

        // BDSP native safety net (same mechanism as Z-A/SV). Ramanas Park legendaries (Rayquaza,
        // the box trio, etc.) ARE catchable natively in BDSP, but the threaded native-first path
        // can still leak a transfer encounter → Non-Native. Rebuild via the direct NativeOnly
        // path. Unlike SV, BDSP bots run PKHeX v26.5.6.0 which DOES enforce shiny-locks, so the
        // la.Valid check inside GetLegalNativeDirect rejects an illegal native shiny (shiny-locked
        // mythical) and the transfer is kept — so this is safe for BOTH shiny and non-shiny.
        if (typeof(T) == typeof(PB8)
            && pkm is PB8 bdspNonNative && bdspNonNative.Species == template.Species
            && new LegalityAnalysis(bdspNonNative).EncounterOriginal.Context != bdspNonNative.Context)
        {
            try
            {
                var directBd = sav.GetLegalNativeDirect(template);
                if (directBd is PB8 bdNative)
                {
                    pkm = bdNative;
                    la = new LegalityAnalysis(pkm);
                    LogUtil.LogInfo($"[TradeModule] BDSP native safety net: rebuilt {pkm.Species} natively via direct path (was Non-Native), valid={la.Valid}", "Helpers");
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] BDSP native safety net failed: {ex.Message}", "Helpers"); }
        }

        // LA (PA8) native safety net (same mechanism as BDSP). The Lake trio (Uxie/Mesprit/Azelf),
        // creation trio (Dialga/Palkia/Giratina), Heatran, Regigigas, Cresselia, Darkrai, Arceus,
        // Enamorus etc. ARE catchable natively in Legends: Arceus, but the threaded native-first path
        // can leak a BDSP/transfer encounter → Non-Native. That blocks the HOME tracker + AutoOT and
        // makes the LA bot decline the request ("can't be made HOME-ready by a Legends: Arceus bot").
        // Rebuild via the direct synchronous NativeOnly path. LA bots run v26.5.6.0 which enforces
        // shiny-locks, so GetLegalNativeDirect's internal la.Valid rejects an illegal native shiny
        // (LA legendaries are shiny-locked) and the transfer is kept — safe for BOTH shiny and non-shiny.
        if (typeof(T) == typeof(PA8)
            && pkm is PA8 plaNonNative && plaNonNative.Species == template.Species
            && new LegalityAnalysis(plaNonNative).EncounterOriginal.Context != plaNonNative.Context)
        {
            try
            {
                var directPla = sav.GetLegalNativeDirect(template);
                if (directPla is PA8 plaNative)
                {
                    pkm = plaNative;
                    la = new LegalityAnalysis(pkm);
                    LogUtil.LogInfo($"[TradeModule] LA native safety net: rebuilt {pkm.Species} natively via direct path (was Non-Native), valid={la.Valid}", "Helpers");
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] LA native safety net failed: {ex.Message}", "Helpers"); }
        }

        // SwSh native safety net (same mechanism as BDSP/SV/Z-A). Crown Tundra Max Lair /
        // Dynamax Adventure legendaries (Giratina, the creation & lake trios, weather trio,
        // birds/beasts, etc.) ARE catchable natively in SwSh and CAN be shiny
        // (EncounterStatic8U, met loc 244, shiny=Random) — but the threaded native-first path
        // can still leak a transfer/GO encounter → Non-Native + HOME tracker, which blocks
        // AutoOT (the member gets the host's OT instead of their own). Rebuild via the direct
        // NativeOnly path. SwSh bots run v26.5.6.0 which enforces shiny-locks, so
        // GetLegalNativeDirect's internal la.Valid rejects an illegal native shiny (a genuinely
        // shiny-locked legendary) and the transfer is kept — safe for BOTH shiny and non-shiny.
        if (typeof(T) == typeof(PK8)
            && pkm is PK8 swshNonNative && swshNonNative.Species == template.Species
            && new LegalityAnalysis(swshNonNative).EncounterOriginal.Context != swshNonNative.Context)
        {
            try
            {
                var directSw = sav.GetLegalNativeDirect(template);
                if (directSw is PK8 swNative)
                {
                    // Max Lair (EncounterStatic8U) guarantees only 4 flawless IVs, so ALM under-fills
                    // and produces a 4-perfect mon even when 6IV was requested — but 6x31 IS legal for
                    // this encounter (the 2 non-guaranteed IVs simply rolled 31). Copy the member's
                    // requested IVs from the non-native build we're replacing (which already reflects
                    // their request, e.g. 6IV) and keep them only if the native mon stays legal;
                    // otherwise fall back to ALM's native spread. This avoids trading their 6IV for
                    // native — they get native, AutoOT, AND their IVs.
                    var wantIvs = new int[6]; swshNonNative.GetIVs(wantIvs);
                    var nativeIvs = new int[6]; swNative.GetIVs(nativeIvs);
                    swNative.SetIVs(wantIvs);
                    swNative.RefreshChecksum();
                    if (!new LegalityAnalysis(swNative).Valid) { swNative.SetIVs(nativeIvs); swNative.RefreshChecksum(); }

                    pkm = swNative;
                    la = new LegalityAnalysis(pkm);
                    LogUtil.LogInfo($"[TradeModule] SwSh native safety net: rebuilt {pkm.Species} natively (was Non-Native), IVs={pkm.IV_HP}/{pkm.IV_ATK}/{pkm.IV_DEF}/{pkm.IV_SPA}/{pkm.IV_SPD}/{pkm.IV_SPE}, valid={la.Valid}", "Helpers");
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] SwSh native safety net failed: {ex.Message}", "Helpers"); }
        }

        if (!la.Valid && pkm is PA9 && !isZAWildLegendary && !nativeZAShiny)
        {
            var fallback = TryGetAsHomePa9(template, spec);
            if (fallback != null)
            {
                pkm = fallback;
                la = new LegalityAnalysis(pkm);
            }
        }
        // ============================================================================
        // END OF PA9 CROSS-GAME HOME FALLBACK
        // ============================================================================

        // Auto-correct EXACT Height/Weight/Scale when PKHeX states the expected value
        // (e.g. a pre-made Magearna flagged "Invalid: Height should be 0"). Setting the
        // value PKHeX wants makes the mon genuinely legal, so the receiving game accepts it
        // cleanly -- instead of shipping a wrong value via the pre-made bypass below, which
        // the game can reject and (in a batch) drop the whole batch. Uses the exact value
        // from the report; if nothing matches, nothing changes -- so it can only improve.
        if (!la.Valid && pkm is PA9 pa9ScaleFix)
        {
            var sr = la.Report();
            bool setAny = false;
            var mh = System.Text.RegularExpressions.Regex.Match(sr, @"Height should be (\d+)");
            if (mh.Success && byte.TryParse(mh.Groups[1].Value, out var hv)) { pa9ScaleFix.HeightScalar = hv; setAny = true; }
            var mw = System.Text.RegularExpressions.Regex.Match(sr, @"Weight should be (\d+)");
            if (mw.Success && byte.TryParse(mw.Groups[1].Value, out var wv)) { pa9ScaleFix.WeightScalar = wv; setAny = true; }
            var ms = System.Text.RegularExpressions.Regex.Match(sr, @"Scale should be (\d+)");
            if (ms.Success && byte.TryParse(ms.Groups[1].Value, out var sv)) { pa9ScaleFix.Scale = sv; setAny = true; }
            if (setAny)
            {
                pa9ScaleFix.RefreshChecksum();
                la = new LegalityAnalysis(pkm);
                LogUtil.LogInfo($"[TradeModule] PA9 {pkm.Species}: corrected Height/Weight/Scale to PKHeX-expected values (valid now={la.Valid})", "Helpers");
            }
        }


        // Pre-made files (mythical fallback) bypass the legality gate.
        // They're pre-validated by the file source and ship even if our PKHeX is
        // too old to recognize newer wondercard databases (HOME-simulated, etc.)
        bool isPreMadeBypass = result == "PreMadeFile";
        // A forced native Z-A shiny is a real native catch; if PKHeX's incomplete Z-A shiny
        // data flags it, ship it anyway (same as the other Z-A pre-made bypasses).
        if (nativeZAShiny) isPreMadeBypass = true;

        // Mythical/legendary bypass: when ALM produces a valid encounter for a known
        // mythical/legendary species, ship it even if PKHeX flags Encounter/Ability/
        // Move/Misc mismatches. Covers:
        //   - PA9 native Z-A catch (loc 100-350): Wild Zones, Rouge Sectors, Hyperspace Lumiose
        //   - PA9/PK9/PK8 HOME-transferred (loc 30000+): species transferred via HOME
        //     (e.g. Magearna in SV/SwSh, or Z-A imports of older event mythicals)
        bool isMythicalMetLocation = pkm is (PA9 or PK9 or PK8) &&
            ((pkm is PA9 && pkm.MetLocation is > 0 and <= 350) || pkm.MetLocation >= 30000);
        bool isZANativeLegendary = isMythicalMetLocation &&
            template.Species is 150 or 151 or 251 or 380 or 381 or 382 or 383 or 384 or 385 or 386
                or 485 or 489 or 490 or 491 or 492 or 493 or 494
                or 638 or 639 or 640 or 647 or 648 or 649
                or 670 or 716 or 717 or 718 or 719 or 720 or 721
                or 801 or 802 or 807 or 808 or 809;

        // BDSP legendary bypass: met location was overridden post-ALM to the canonical spot,
        // which PKHeX may flag (encounter mismatch). Ship anyway — the Pokemon is otherwise legal.
        bool isBDSPLakeTrio = pkm is PB8 &&
            template.Species is 480 or 481 or 482 or 485 or 487 or 488 or 491 or 492;
        if ((isZANativeLegendary || isBDSPLakeTrio) && pkm != null)
        {
            var zaResults = la.Results.Where(r => !r.Valid).Select(r => r.Identifier.ToString()).ToHashSet();
            // Only bypass if the failures are limited to known fussy checks —
            // don't ignore truly bad data (corrupted bytes, wrong species, etc.).
            bool onlyExpectedFails = zaResults.All(id => id is "Encounter" or "Ability" or "Move" or "Misc");
            if (onlyExpectedFails && zaResults.Count > 0)
                isPreMadeBypass = true;
        }

        // ── Requested level too low for the encounter: correct it instead of rejecting ──
        // A level below the encounter's minimum (e.g. "Level: 10" on an Alpha Dratini whose
        // encounter starts at 38) makes generation fail, and ALM reports it as a MOVE problem
        // ("<species> cannot learn those moves in this game!") — which sends members hunting the
        // wrong thing entirely. Retry once with the level stripped so ALM uses the encounter's
        // natural level, clamp to the lowest legal level for the set (Alpha status, marks, moves
        // and everything else preserved), and tell them what changed. This only runs when
        // generation ALREADY failed, so it can never alter a trade that was going to succeed.
        if (reqLevel > 0 && (pkm is not T || !la.Valid) && !isPreMadeBypass)
        {
            try
            {
                var noLevelText = string.Join("\n", contentWithoutLanguage.Split('\n')
                    .Where(l => !l.TrimStart().StartsWith("Level:", StringComparison.OrdinalIgnoreCase)));
                var retryPkm = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(new ShowdownSet(noLevelText)), out _);
                if (retryPkm is T && retryPkm.Species == template.Species && new LegalityAnalysis(retryPkm).Valid)
                {
                    var finalLvl = ApplyLowestLegalLevel(retryPkm, reqLevel);
                    var retryLa = new LegalityAnalysis(retryPkm);
                    if (retryLa.Valid)
                    {
                        if (finalLvl > reqLevel)
                            levelAdjustNote = $"**Level {reqLevel}** isn't possible for this {spec} — it was corrected to **Level {finalLvl}**, the lowest legal level for this set.";
                        pkm = retryPkm;
                        la = retryLa;
                        LogUtil.LogInfo($"[TradeModule] Level auto-correct: {spec} requested Level {reqLevel} -> shipped Level {finalLvl}", "Helpers");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[TradeModule] Level auto-correct failed: {ex.Message}", "Helpers");
            }
        }

        if (pkm is not T pk || (!la.Valid && !isPreMadeBypass))
        {
            // Diagnostic: log specific legality failure reasons
            if (pkm != null && !la.Valid)
            {
                var failReasons = string.Join(", ", la.Results
                    .Where(r => !r.Valid)
                    .Select(r => $"{r.Identifier}"));
                LogUtil.LogInfo($"TradeModule legality fail: species={pkm.Species} form={pkm.Form} loc={pkm.MetLocation} ot='{pkm.OriginalTrainerName}' shiny={pkm.IsShiny} shinyXor={pkm.ShinyXor} result='{result}' | {failReasons}", "Legality");
            }
            var reason = GetFailureReason(result, spec);
            var hint = result == "Failed" ? GetLegalizationHint(template, sav, pkm, spec) : null;
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = reason,
                LegalizationHint = hint,
                ShowdownSet = set
            });
        }
        if (isPreMadeBypass && !la.Valid)
        {
            var bypassReport = la.Report().Replace("\n", " | ");
            var pkmDetails = $"species={pkm.Species} form={pkm.Form} version={(GameVersion)pkm.Version} metLoc={pkm.MetLocation} metLvl={pkm.MetLevel} lvl={pkm.CurrentLevel} OT={pkm.OriginalTrainerName} shiny={pkm.IsShiny}";
            LogUtil.LogInfo($"[TradeModule] Pre-made bypass: shipping {pkm.Species} despite local legality flags. Details: {pkmDetails}. Report: {bypassReport}", "Helpers");
        }

        // ============================================================================
        // ZA NATURE LEGALITY ENFORCEMENT
        // ============================================================================
        // For ZA (PA9) Pokemon, honor the user's requested nature if it passes legality.
        // If the requested nature is illegal for the encounter (e.g. Zeraora must be Brave),
        // keep PKHeX's legal nature as the actual Nature and apply the requested nature as
        // StatNature only (mint effect).
        //
        // Example 1: Zeraora (ZA native, forced Brave) + user requests Adamant
        //            → Nature=Brave, StatNature=Adamant
        // Example 2: Charmander (SWSH via HOME fallback) + user requests Timid
        //            → Nature=Timid, StatNature=Timid
        // Example 3: No nature requested, only StatNature via batch (.StatNature=X)
        //            → Nature=PKHeX default, StatNature=X (already set by ALM)
        // Example 4: Nothing requested → ALM picks, no change.
        // ============================================================================
        // Applies to Z-A (PA9) AND Scarlet/Violet (PK9). This check is game-agnostic — it does not
        // use a per-game table; it clones the mon, applies the requested nature and simply ASKS
        // PKHeX whether that is legal, minting instead when it isn't. SV was previously excluded,
        // so a fixed-nature SV encounter (e.g. Bloodmoon Ursaluna, which can only be Hardy) had the
        // requested nature written onto its ACTUAL Nature. That makes the mon illegal, which in turn
        // invalidates an event file / HOME tracker — the member's Pokemon is then rejected by HOME.
        // Minting (StatNature) shows the requested nature in-game while keeping the mon legal.
        if (pk is PA9 or PK9)
        {
            // Nature.Random (25) means the user did not specify a nature in the set.
            Nature requestedNature = set.Nature;
            bool userRequestedNature = requestedNature != Nature.Random;

            // Detect if the user explicitly set a StatNature via .StatNature= batch command.
            // IMPORTANT: We parse the content string directly rather than comparing pk.StatNature != pk.Nature.
            // After ALM generation and/or HOME conversion the StatNature byte can differ from Nature as
            // a format-conversion artifact — checking PKM fields would misidentify that as a user request.
            Nature? userExplicitStatNature = null;
            foreach (var line in contentLines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(".StatNature=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed[".StatNature=".Length..].Trim();
                    if (Enum.TryParse<Nature>(value, ignoreCase: true, out var parsedSN))
                    {
                        userExplicitStatNature = parsedSN;
                        break;
                    }
                }
            }

            bool hasExplicitStatNature = userExplicitStatNature.HasValue;
            Nature userStatNature = userExplicitStatNature ?? Nature.Random;

            // Some encounters lock the nature outright (Bloodmoon Ursaluna is always Hardy, Eternatus
            // is always Timid, etc.). Generation can still write the requested nature onto the ACTUAL
            // Nature, which makes the mon illegal and voids its HOME tracker — HOME then refuses the
            // deposit. Ask PKHeX what the matched encounter locks the nature to and, when it disagrees
            // with what we hold, restore it and demote the user's request to a mint (StatNature).
            if (userRequestedNature)
            {
                var encounter = new LegalityAnalysis(pk).Info.EncounterMatch;
                if (encounter is IFixedNature { Nature: var lockedNature }
                    && lockedNature != Nature.Random
                    && lockedNature != pk.Nature)
                {
                    var mintedTo = hasExplicitStatNature ? userStatNature : requestedNature;
                    LogUtil.LogInfo(
                        $"{(Species)pk.Species}: Encounter locks Nature to {lockedNature} but generation produced " +
                        $"{pk.Nature}. Restoring {lockedNature} and minting StatNature={mintedTo}.",
                        "NatureLegality");
                    pk.Nature = lockedNature;
                    pk.StatNature = mintedTo;
                    pk.RefreshChecksum();
                }
            }

            if (userRequestedNature && requestedNature != pk.Nature)
            {
                // The encounter forced a different nature than what the user requested.
                // Test whether the user's requested nature is legal for this encounter.
                var clone = (T)pk.Clone();
                clone.Nature = requestedNature;
                clone.StatNature = hasExplicitStatNature ? userStatNature : requestedNature;
                clone.RefreshChecksum();

                if (new LegalityAnalysis(clone).Valid)
                {
                    // Legal — apply the requested nature to both Nature and StatNature.
                    pk.Nature = clone.Nature;
                    pk.StatNature = clone.StatNature;
                    pk.RefreshChecksum();
                    LogUtil.LogInfo(
                        $"{(Species)pk.Species}: Requested nature {requestedNature} is legal — applied.",
                        "NatureLegality");
                }
                else
                {
                    // Requested nature is illegal for this encounter.
                    // Try minting: keep the forced Nature but apply requested nature as StatNature.
                    // Verify the mint itself is legal before applying — some encounters (e.g. certain
                    // HOME-converted WC8 events) restrict StatNature via shiny/nature correlation checks
                    // and will also reject a mismatched StatNature.
                    var wantedStatNature = hasExplicitStatNature ? userStatNature : requestedNature;
                    var cloneMint = (T)pk.Clone();
                    cloneMint.StatNature = wantedStatNature;
                    cloneMint.RefreshChecksum();

                    if (new LegalityAnalysis(cloneMint).Valid)
                    {
                        // Mint is legal — apply it.
                        pk.StatNature = wantedStatNature;
                        pk.RefreshChecksum();
                        LogUtil.LogInfo(
                            $"{(Species)pk.Species}: Requested nature {requestedNature} is illegal for this encounter. " +
                            $"Mint applied: Nature={pk.Nature}, StatNature={pk.StatNature}.",
                            "NatureLegality");
                    }
                    else
                    {
                        // Minting is also restricted (e.g. shiny-correlation check ties StatNature to Nature).
                        // Leave Nature and StatNature exactly as PKHeX produced them — both forced.
                        LogUtil.LogInfo(
                            $"{(Species)pk.Species}: Requested nature {requestedNature} is illegal and minting is " +
                            $"restricted for this encounter. Keeping forced Nature={pk.Nature}, StatNature={pk.StatNature}.",
                            "NatureLegality");
                    }
                }
            }
            else if (userRequestedNature && requestedNature == pk.Nature)
            {
                // User's requested nature matches what was generated — mirror to StatNature
                // unless the user already set a different StatNature via batch command.
                if (!hasExplicitStatNature)
                {
                    pk.StatNature = pk.Nature;
                    pk.RefreshChecksum();
                }
            }
            // Else: no nature was requested — leave Nature and StatNature exactly as ALM set them.
        }
        // ============================================================================
        // END OF NATURE LEGALITY ENFORCEMENT
        // ============================================================================

        // ============================================================================
        // EVENT OT SCRIPT / LANGUAGE MATCH
        // ============================================================================
        // Some events only ever existed in one region, so their fixed OT is Korean or Japanese
        // text. A shiny Diancie, for instance, was only ever distributed in Korea and Japan --
        // its OT is genuinely '올스타'. Leaving such a mon on a Latin language gives us an
        // English Pokemon carrying Korean text in its OT field, which nothing can render, so it
        // displays as "???". Switch the mon to a language that can actually show its own OT.
        //
        // Only applied if PKHeX still says the result is legal -- if matching the language would
        // break legality, we keep exactly what we had. Worst case is today's behaviour, never worse.
        // ============================================================================
        var eventOt = pk.OriginalTrainerName;
        if (!string.IsNullOrEmpty(eventOt)
            && IsLatinLanguage(pk.Language)
            && eventOt.Length <= 6                       // PrepareForTrade replaces >6-char OTs on Asian languages
            && GetLanguagesForScript(eventOt) is { Length: > 0 } scriptLanguages)
        {
            foreach (var candidate in scriptLanguages)
            {
                var probe = (T)pk.Clone();
                probe.Language = (byte)candidate;
                probe.SetDefaultNickname(new LegalityAnalysis(probe));
                probe.IsNicknamed = false;
                probe.RefreshChecksum();

                if (!new LegalityAnalysis(probe).Valid)
                    continue;

                LogUtil.LogInfo(
                    $"{(Species)pk.Species}: OT '{eventOt}' cannot display on {(LanguageID)pk.Language}. " +
                    $"Switching to {candidate} so the OT renders. Nickname={probe.Nickname}.",
                    "EventOTLanguage");

                pk = probe;
                effectiveLanguage = (byte)candidate;

                var note = $"This **{(Species)pk.Species}** only exists as a **{candidate}** event, so its OT is " +
                           $"**{eventOt}** and its nickname is in {candidate}. That's correct — it's the only legal " +
                           $"version — and it will still go into HOME.";
                levelAdjustNote = string.IsNullOrEmpty(levelAdjustNote) ? note : $"{levelAdjustNote}\n{note}";
                break;
            }
        }
        // ============================================================================
        // END OF EVENT OT SCRIPT / LANGUAGE MATCH
        // ============================================================================

        // Final preparation — use effectiveLanguage so the FIXED-OT FALLBACK's language
        // choice is not overwritten by finalLanguage here.
        LogUtil.LogInfo($"[LANGUAGE TRACE] Before PrepareForTrade: finalLanguage={finalLanguage}, effectiveLanguage={effectiveLanguage}, pk.Language={pk.Language}", "Helpers");
        PrepareForTrade(pk, set, effectiveLanguage);
        LogUtil.LogInfo($"[LANGUAGE TRACE] After PrepareForTrade: pk.Language={pk.Language}", "Helpers");

        // Check for spam names
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
        {
            if (TradeExtensions<T>.HasAdName(pk, out string ad))
            {
                return Task.FromResult(new ProcessedPokemonResult<T>
                {
                    Error = "Detected Adname in the Pokémon's name or trainer name, which is not allowed.",
                    ShowdownSet = set
                });
            }
        }
    
        // SWSH (PK8) and LGPE (PB7) can legitimately receive GO Pokemon as native:
        // SwSh via HOME-transfer, LGPE via the original GO Park / Mystery Box mechanic.
        // Meltan/Melmetal in PB7 are *only* obtainable via GO transfer, so GO-origin PB7
        // must not be flagged Non-Native or AutoOT/HOME-eligibility gets disabled.
        // Restore user-specified IVs if a batch command re-rolled them. A Mark (notably
        // RibbonMarkThorny) makes ALM regenerate the encounter and re-roll IVs to flawless, so a
        // request like "IVs: 26/27/22/14/18/0 + .RibbonMarkThorny=true" shipped 31/31/31/31/31/0
        // instead of the requested spread. Marks/ribbons don't constrain IVs, so re-applying the
        // requested values stays legal; revert only if the encounter genuinely fixes IVs.
        if (userSpecifiedIVs && result != "PreMadeFile" && set?.IVs is { Length: 6 } wantIVs)
        {
            var curIVs = new int[6];
            pk.GetIVs(curIVs);
            if (!curIVs.SequenceEqual(wantIVs))
            {
                var ivBackup = pk.Clone();
                pk.SetIVs(wantIVs);
                pk.RefreshChecksum();
                if (!new LegalityAnalysis(pk).Valid)
                    pk = (T)ivBackup; // requested IVs illegal for this encounter — keep ALM's
                else
                    LogUtil.LogInfo($"[TradeModule] Restored user-specified IVs {string.Join("/", wantIVs)} for {pk.Species} (a batch command had re-rolled them)", "Helpers");
            }
        }

        // Restore the user-requested HELD ITEM if any path dropped it. BDSP legendary birds
        // (and other native/pre-made paths) can ship without the requested item even though ALM
        // generates it fine — a pre-made file has no item, or a rebuild re-rolls the base mon.
        // set.HeldItem is 0 when the member didn't ask for one, so only act when they did.
        if (set?.HeldItem is > 0 && pk.HeldItem != set.HeldItem)
        {
            var itemBackup = pk.Clone();
            pk.HeldItem = set.HeldItem;
            pk.RefreshChecksum();
            if (!new LegalityAnalysis(pk).Valid)
                pk = (T)itemBackup; // requested item illegal for this encounter — keep what we had
            else
                LogUtil.LogInfo($"[TradeModule] Restored user-requested held item {set.HeldItem} ({GameInfo.Strings.itemlist[set.HeldItem]}) for {pk.Species}", "Helpers");
        }

        la = new LegalityAnalysis(pk);
        var isNonNative = la.EncounterOriginal.Context != pk.Context || (pk.GO && pk is not PK8 && pk is not PB7);

        // Pokemon Legends Z-A has its own native encounters for several Mythicals/Legendaries
        // that PKHeX may default to a Gen7/Gen8 encounter source for. Override the Non-Native
        // flag for species confirmed to be Z-A natives (in-game encounters or HOME wondercards
        // distributed specifically for Z-A).
        if (isNonNative && pk is PA9 && IsZANativeSpecies(pk.Species))
            isNonNative = false;

        // ── Z-A: DECLINE instead of shipping a glitchy Non-Native SV fallback ──
        // If the request isn't Z-A-legal (a move/ability/ribbon not available in Z-A), native
        // generation fails and ALM falls back to an SV encounter with NO HOME tracker — wrong
        // moves, can't enter HOME. We decline rather than ship that. Legit pre-mades
        // (result == "PreMadeFile") and HOME-transferred Z-A legendaries (real tracker, or
        // IsZANativeSpecies already cleared isNonNative above) still ship normally.
        if (typeof(T) == typeof(PA9) && isNonNative && result != "PreMadeFile"
            && !(pk is IHomeTrack zaTrk && zaTrk.HasTracker))
        {
            var spcName = GameInfo.Strings.Species[template.Species];
            var strings = GameInfo.GetStrings("en");
            var badMoves = new List<string>();
            try
            {
                var reqMoves = (set?.Moves ?? Array.Empty<ushort>()).Where(m => m != 0).ToArray();
                if (reqMoves.Length > 0)
                {
                    // Build a bare native Z-A version of the species and test each requested move
                    // against it — the ones that flag "Invalid Move" are the ones Z-A can't learn.
                    var zaBase = sav.GetLegalNativeDirect(AutoLegalityWrapper.GetTemplate(
                        new ShowdownSet(spcName + (template.Shiny ? "\nShiny: Yes" : ""))));
                    if (zaBase is PA9 zb)
                    {
                        foreach (var m in reqMoves)
                        {
                            zb.SetMoves(new[] { m }); zb.HealPP(); zb.RefreshChecksum();
                            if (new LegalityAnalysis(zb).Report().Contains("Invalid Move", StringComparison.OrdinalIgnoreCase))
                                badMoves.Add(strings.movelist[m]);
                        }
                    }
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] Z-A decline move-check failed: {ex.Message}", "Helpers"); }

            string detail = badMoves.Count > 0
                ? $"These move(s) aren't available on **{spcName}** in Legends: Z-A: **{string.Join(", ", badMoves)}**."
                : $"This Pokémon — or one of its requested moves, abilities, or ribbons — isn't available in Legends: Z-A.";
            LogUtil.LogInfo($"[TradeModule] Z-A declined {spcName}: not Z-A-legal (bad moves: {(badMoves.Count > 0 ? string.Join(",", badMoves) : "n/a")})", "Helpers");
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = $"**{spcName} can't be made as a native Legends: Z-A Pokémon.**\n{detail}\n\nZ-A bots only deliver native, HOME-legal Pokémon — so this request was declined instead of shipping a glitchy non-native copy. Please use a Z-A-legal set, or request this exact Pokémon from a **Scarlet/Violet** or **SwSh** bot instead.",
                ShowdownSet = set
            });
        }

        // ── SV: DECLINE instead of shipping a Non-Native, tracker-less encounter ──
        // If a request has no native Scarlet/Violet encounter, ALM falls back to a
        // Pokémon GO / SwSh / transfer encounter with NO HOME tracker — illegal in SV
        // (HOME flags it "hacked"). We decline rather than look at other games and ship
        // that. The ONLY exceptions: pre-made DB files (result == "PreMadeFile") and
        // anything carrying a real HOME tracker (mythicals get one added earlier) —
        // those are HOME-legal cross-origin mons and still ship normally.
        // ── EVERY GAME: DECLINE a non-native, tracker-less encounter, and say which bot to ask ──
        // HOME gives a Pokémon its tracker on its first upload from the game it was BORN in. If we ship
        // one whose origin game isn't this bot's game and it has no tracker, HOME refuses the deposit
        // (2-ALZTA-0005 / 10015) — e.g. Eternatus is a Sword/Shield event, so an SV bot cannot make a
        // HOME-acceptable one no matter what we do, while a SwSh bot can, every time.
        //
        // This previously only guarded SV and Z-A; SwSh, BDSP, PLA and LGPE would silently ship a
        // Pokémon HOME would reject. Now every game is covered, and instead of a vague "try another
        // bot" we name the exact bot that CAN make it — or send them to the archive if no bot runs its
        // origin game at all (Pokémon GO, old-gen events).
        //
        // Exceptions, unchanged: pre-made DB files, and anything already carrying a real HOME tracker.
        // LAST-CHANCE NATIVE REBUILD (runs right before the decline, where pk is the final
        // converted entity of our format). The species may be native to THIS bot's game, but the
        // default-priority legalizer picked a transfer encounter (e.g. Mesprit on a Legends: Arceus
        // bot getting a BDSP encounter → Non-Native). The earlier per-game safety nets can miss this
        // because at that point pkm was still the encounter's native format, not yet converted to T.
        // Force NativeOnly priority here; if it yields a genuinely native, legal entity of our format,
        // adopt it — turning a decline into a successful native, HOME-ready trade. Species with NO
        // native encounter in our game (Eternatus on LA, etc.) return null and still decline correctly.
        if (isNonNative && result != "PreMadeFile"
            && !(pk is IHomeTrack ht0 && ht0.HasTracker)
            && !HomeOriginAdvisor.IsNativeToBot(pk))
        {
            try
            {
                // Strip the requested Ball for the native rebuild. Native encounters use game-specific
                // ball ITEMS — Legends: Arceus's Poké Ball is a different item id from the modern one —
                // so a "Ball: Poke Ball" line forces ALM off the native encounter to a game where that
                // exact ball is valid (BDSP for Mesprit) → Non-Native → decline. The website ALWAYS
                // appends a Ball line, so without this every LA legendary web request fails. Dropping it
                // lets ALM use the encounter's natural ball, which is correct and legal for the origin.
                var noBallLines = set.GetSetLines().Where(l => !l.TrimStart().StartsWith("Ball:", StringComparison.OrdinalIgnoreCase));
                var nativeTemplate = AutoLegalityWrapper.GetTemplate(new ShowdownSet(string.Join("\n", noBallLines)));

                // ALM is non-deterministic — a single GetLegalNativeDirect can fail to find the native
                // encounter one call and succeed the next (proven: the scratchpad generator needed a
                // retry loop). Retry until it yields a native legal entity, or we exhaust attempts. A
                // species with NO native encounter in our game returns null every time and falls through
                // to the decline below; a real native (Mesprit on LA, etc.) resolves within a few tries.
                for (int rebuildAttempt = 0; rebuildAttempt < 25 && isNonNative; rebuildAttempt++)
                {
                    var nativeRetry = sav.GetLegalNativeDirect(nativeTemplate);
                    if (nativeRetry is T nTyped && HomeOriginAdvisor.IsNativeToBot(nTyped)
                        && new LegalityAnalysis(nTyped).Valid)
                    {
                        pk = nTyped;
                        isNonNative = false;
                        LogUtil.LogInfo($"[TradeModule] native last-chance rebuild: {GameInfo.Strings.Species[template.Species]} rebuilt natively (was Non-Native {nativeRetry.Version}) on attempt {rebuildAttempt + 1} — shipping instead of declining", "Helpers");
                    }
                }
            }
            catch (Exception ex) { LogUtil.LogError($"[TradeModule] native last-chance rebuild failed: {ex.Message}", "Helpers"); }
        }

        if (isNonNative && result != "PreMadeFile"
            && !(pk is IHomeTrack trk && trk.HasTracker)
            && !HomeOriginAdvisor.IsNativeToBot(pk))
        {
            var spcName = GameInfo.Strings.Species[template.Species];
            var thisGame = HomeOriginAdvisor.DescribeVersion(
                pk switch
                {
                    PK9 => GameVersion.SL, PK8 => GameVersion.SW, PB8 => GameVersion.BD,
                    PA8 => GameVersion.PLA, PA9 => GameVersion.ZA, PB7 => GameVersion.GP,
                    _ => pk.Version,
                });

            LogUtil.LogInfo(
                $"[TradeModule] {thisGame} declined {spcName}: origin is {pk.Version}, no HOME tracker — " +
                $"HOME would reject it. Redirecting the member to the right bot.", "Helpers");

            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = HomeOriginAdvisor.BuildDeclineMessage(pk, spcName, thisGame),
                ShowdownSet = set
            });
        }

        return Task.FromResult(new ProcessedPokemonResult<T>
        {
            Pokemon = pk,
            ShowdownSet = set,
            LgCode = lgcode,
            IsNonNative = isNonNative,
            LevelAdjustedNote = levelAdjustNote
        });
    }

    public static void ApplyStandardItemLogic(PKM pkm)
    {
        pkm.HeldItem = pkm switch
        {
            PA8 => (int)HeldItem.None,
            _ when pkm.HeldItem == 0 && !pkm.IsEgg => (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem,
            _ => pkm.HeldItem
        };
    }

    /// <summary>
    /// Languages that use the Latin alphabet and therefore cannot render Korean/Japanese/Chinese OT text.
    /// </summary>
    private static bool IsLatinLanguage(int language) => language is
        (int)LanguageID.English or (int)LanguageID.French or (int)LanguageID.Italian or
        (int)LanguageID.German or (int)LanguageID.Spanish;

    /// <summary>
    /// Given OT text, returns the language(s) that can display it, best guess first.
    /// Empty if the text is plain Latin and needs no language change.
    /// </summary>
    private static LanguageID[] GetLanguagesForScript(string text)
    {
        bool hangul = false, kana = false, han = false;
        foreach (var c in text)
        {
            if (c is >= '가' and <= '힯' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏')
                hangul = true;
            else if (c is >= '぀' and <= 'ヿ')
                kana = true;
            else if (c is >= '一' and <= '鿿')
                han = true;   // shared by Japanese and Chinese — try each and let PKHeX decide
        }

        if (hangul)
            return [LanguageID.Korean];
        if (kana)
            return [LanguageID.Japanese];
        if (han)
            return [LanguageID.Japanese, LanguageID.ChineseS, LanguageID.ChineseT];
        return [];
    }

    public static void PrepareForTrade(T pk, ShowdownSet set, byte finalLanguage)
    {
        // Only set EggMetDate for hatched Pokemon, not for unhatched eggs
        if (pk.WasEgg && !pk.IsEgg)
            pk.EggMetDate = pk.MetDate;

        // Validate language is supported for this game version
        // SpanishL (11) isn't supported in some games, fall back to Spanish (7)
        var validatedLanguage = ValidateLanguageForGame(pk, finalLanguage);
        pk.Language = validatedLanguage;

        // CRITICAL: Asian languages only support 6-character OT names
        // Replace English OT with Asian characters for Asian languages
        if (validatedLanguage == (int)LanguageID.Japanese ||
            validatedLanguage == (int)LanguageID.Korean ||
            validatedLanguage == (int)LanguageID.ChineseS ||
            validatedLanguage == (int)LanguageID.ChineseT)
        {
            if (pk.OriginalTrainerName.Length > 6)
            {
                // Use proper Asian characters instead of truncating English text
                var asianOT = "王犬米";

                // Properly set OT and clear trash bytes
                pk.OriginalTrainerName = asianOT;

                // Clear OT trash bytes to ensure legality
                // Get the OT as bytes, properly sized for the format
                Span<byte> trash = stackalloc byte[pk.TrashCharCountTrainer * 2];
                int length = pk.SetString(trash, asianOT.AsSpan(), pk.TrashCharCountTrainer, StringConverterOption.ClearZero);
                pk.OriginalTrainerTrash.Clear();
                trash[..length].CopyTo(pk.OriginalTrainerTrash);

                // Refresh checksum after modifying OT
                pk.RefreshChecksum();
            }
        }

        if (!set.Nickname.Equals(pk.Nickname) && string.IsNullOrEmpty(set.Nickname))
        {
            // Use the correct species name for the stored language instead of "" (ClearNickname).
            // Asian languages require the actual species name in the nickname field; "" fails
            // PKHeX's "Nickname does not match species name" legality check.
            pk.Nickname = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, pk.Format);
            pk.IsNicknamed = false;
        }

        pk.ResetPartyStats();
    }

    private static int ValidateLanguageForGame(PKM pk, byte requestedLanguage)
    {
        // SpanishL (11) support varies by game
        if (requestedLanguage == (byte)LanguageID.SpanishL)
        {
            // Check if this game supports SpanishL
            bool supportsSpanishL = pk switch
            {
                PB7 => false,  // Let's Go - does not support SpanishL properly
                PK8 => false,  // Sword/Shield - does not support SpanishL properly
                PB8 => false, // BDSP - does not support SpanishL properly
                PA8 => false, // Legends Arceus - does not support SpanishL properly
                PK9 => false,  // Scarlet/Violet - does not support SpanishL properly
                PA9 => true,  // Legends Z-A - Supports SpanishL
                _ => false
            };

            if (!supportsSpanishL)
            {
                // Fall back to Spanish (7) if SpanishL is used in any game other than Legends Z-A
                return (int)LanguageID.Spanish;
            }
        }

        return requestedLanguage;
    }

    public static string GetFailureReason(string result, string speciesName)
    {
        return result switch
        {
            "Timeout" => $"That {speciesName} set took too long to generate.",
            "VersionMismatch" => "Request refused: PKHeX and Auto-Legality Mod version mismatch.",
            _ => $"I wasn't able to create a {speciesName} from that set."
        };
    }

    /// <summary>
    /// Returns the legal SwSh encounter parameters for species that can only be obtained in SwSh
    /// and must be transferred to SV via HOME. Returns null if the species isn't SwSh-only.
    /// Tuple: (level, shiny, form?) where form is required only when the shiny wondercard uses
    /// a specific form (e.g. Zacian/Zamazenta shiny wondercard is Hero form 0, not Crowned).
    /// </summary>
    public static (int level, bool shiny, byte? form)? GetSwShLegalEncounter(ushort species, bool userWantsShiny)
    {
        return species switch
        {
            // Eternatus: story lv60 non-shiny, wondercard 1643 lv100 shiny
            890 => userWantsShiny ? (100, true, (byte?)null) : (60, false, (byte?)null),
            // Zacian: story lv70 Hero form non-shiny, wondercard 1644 lv100 Hero shiny
            888 => userWantsShiny ? (100, true, (byte?)0) : (70, false, (byte?)0),
            // Zamazenta: story lv70 Hero non-shiny, wondercard 1645 lv100 Hero shiny
            889 => userWantsShiny ? (100, true, (byte?)0) : (70, false, (byte?)0),
            // Kubfu (891) / Urshifu (892) are NOT SwSh-only: the Indigo Disk DLC added them
            // natively to SV (EncounterStatic9 at North Province (Area Two), met loc 48, the
            // player's own OT, no HOME tracker). Routing them through SwSh -> HOME -> PK9 made
            // them "Non-Native & Has Home Tracker" (AutoOT skipped) and HOME rejected them on
            // upload. Removed so ALM generates the native SV encounter. Verified: Kubfu +
            // Urshifu (both forms) come out SV-native, tracker-free, valid.
            // Regirock / Regice / Registeel: Crown Tundra Dynamax Adventures lv70, shiny allowed
            377 => (70, userWantsShiny, (byte?)null),
            378 => (70, userWantsShiny, (byte?)null),
            379 => (70, userWantsShiny, (byte?)null),
            // Regigigas: Crown Shrine static lv70, shiny only via Pokemon GO transfer
            // pre-made file (Crown Shrine itself is shiny-locked). Pass userWantsShiny so
            // ALM tries SwSh first; if shiny requested, ALM fails and pre-made fallback kicks in.
            486 => (70, userWantsShiny, (byte?)null),
            // Regieleki / Regidrago: Crown Tundra lv70, non-shiny only
            894 => (70, false, (byte?)null),
            895 => (70, false, (byte?)null),
            // Glastrier / Spectrier: Crown Tundra lv70, non-shiny only
            896 => (70, false, (byte?)null),
            897 => (70, false, (byte?)null),
            // Calyrex: Crown Tundra lv80, non-shiny only
            898 => (80, false, (byte?)null),
            // Keldeo: Gen 5 mythical, non-shiny in original (shiny via GO+HOME pre-made fallback)
            647 => (50, userWantsShiny, (byte?)null),
            // Meloetta: Released shiny in SV via the Mythical Distribution event - ALM handles it natively
            // 648 => (50, userWantsShiny, (byte?)null),
            _ => null,
        };
    }

    /// <summary>
    /// Mythicals/Legendaries that have legal shiny variants only via Pokemon GO transfers.
    /// These are shiny-locked in their original games but can be obtained shiny in GO via
    /// Special Research, Field Research, or events, then transferred via HOME.
    /// </summary>
    /// <summary>
    /// Pokemon that have a native encounter or HOME-event distribution in Legends Z-A.
    /// Used to override the Non-Native flag when a Z-A bot trades these species.
    /// PKHeX may default to an older-generation encounter for some of these, but they're
    /// legitimately obtainable in Z-A and shouldn't show "Non-Native & Has Home Tracker."
    /// </summary>
    public static bool IsZANativeSpecies(ushort species)
    {
        return species switch
        {
            802 => true, // Marshadow — Z-A native
            721 => true, // Volcanion — Z-A native (Sec patch HOME WC)
            647 => true, // Keldeo — Z-A native (HOME event)
            648 => true, // Meloetta — Z-A native (HOME event)
            _ => false,
        };
    }

    /// <summary>
    /// Pokemon that are shiny-locked when traded on a Z-A bot. These species may have
    /// legitimate shinies in OTHER games (GO transfers, past events, BDSP eggs) but
    /// cannot be legally traded into Z-A as shiny. Reject the request before ALM
    /// picks a non-Z-A encounter source.
    /// Exceptions: Volcanion (#721), Keldeo (#647), Meloetta (#648) have confirmed
    /// Z-A HOME-event shiny distributions and ARE allowed.
    /// </summary>
    public static bool IsZALockedShiny(ushort species)
    {
        return species switch
        {
            // Allowed via HOME transfer (announced 2026-05-06):
            // 150 Mewtwo, 382 Kyogre, 383 Groudon, 384 Rayquaza, 485 Heatran, 491 Darkrai,
            // 716 Xerneas, 717 Yveltal, 718 Zygarde, 647 Keldeo, 648 Meloetta, 721 Volcanion
            151  => true, // Mew
            251  => true, // Celebi
            385  => true, // Jirachi
            386  => true, // Deoxys
            489  => true, // Phione
            490  => true, // Manaphy
            492  => true, // Shaymin
            493  => true, // Arceus (always shiny-locked)
            494  => true, // Victini
            649  => true, // Genesect
            719  => true, // Diancie
            720  => true, // Hoopa (already globally blocked, defensive)
            801  => true, // Magearna
            802  => true, // Marshadow
            // 807 Zeraora unblocked 2026-05-19 — real Z-A shiny .pa9 in HOME-Ready-Files (HOME Distribution event file)
            808  => true, // Meltan
            809  => true, // Melmetal
            893  => true, // Zarude (Dada Zarude was form-only, not shiny)
            // 999 Gimmighoul — shiny-unlockable in Z-A
            1025 => true, // Pecharunt
            // Floette form 5 (Eternal) is shiny-locked. Form check happens elsewhere
            // since this method only sees species ID. The form-aware reject for #670-eternal
            // is handled in the form-correction path.
            _ => false,
        };
    }

    /// <summary>
    /// Returns a human-readable reason when the requested species+form is NOT
    /// natively available in Pokémon Legends: Z-A. ALM produces a PA9 from an
    /// older-gen encounter for the requested form, which the receiving Z-A game
    /// rejects at the Link Trade screen. Returns null when the form is allowed.
    /// Expand as additional form-locked species are reported.
    /// </summary>
    public static string? GetZAFormBlockReason(ushort species, byte form)
    {
        return (species, form) switch
        {
            // Zygarde: ALL forms fail Z-A's in-game Link Trade check on this bot.
            // Showdown parses every form variant (Zygarde, Zygarde-50%, Zygarde-10%,
            // Zygarde-Complete) into either Aura Break (form 0/1) or Complete (form 4),
            // none of which match Z-A's native Power Construct encounter signature.
            // Empirically confirmed 2026-05-12: form 0 (Zygarde-50%) and form 1
            // (Zygarde-10%) both fail in-game with "problem with your trade partner's
            // Pokémon" despite shipping via legality bypass. Block until a real
            // Z-A Power-Construct PA9 source file is added to HOME-Ready-Files.
            // Zygarde forms:
            //   form 0 ("Zygarde", "Zygarde-50%") → 0718 - Zygarde - BCB361FA085E.pa9 (50% PC) ✓
            //   form 2 ("Zygarde-10%") → 0718-02 - Zygarde - 3DDA584950A1.pa9 (10% PC) ✓
            //   form 4 ("Zygarde-Complete", 100%) → BLOCKED, Z-A doesn't support Complete file format
            (718, 4) => "Pokémon Legends: Z-A doesn't support Zygarde Complete (100% Forme) files. Request `Zygarde` (50% Forme) or `Zygarde-10%` instead.",
            _ => null,
        };
    }

    /// <summary>
    /// Z-A native legendaries whose Z-A encounter signature (Power Construct ability,
    /// specific Wild Zone / Hyperspace met locations) ALM can't reliably reproduce.
    /// When the user requests one of these on a PA9 (Z-A) bot, we route through the
    /// pre-made fallback in HOME-Ready-Files so a real Z-A-extracted .pa9 file is
    /// shipped instead of ALM's broken output. Add species here once a corresponding
    /// .pa9 source file is dropped into HOME-Ready-Files.
    /// </summary>
    /// <summary>
    /// Species blocked from shiny requests on Z-A (PA9) bots because the resulting
    /// file fails HOME deposit ("Non-Native, cannot enter HOME"). Empirically:
    /// SwSh Max Lair routing produces a valid in-game file but the HOME tracker we
    /// generate isn't one HOME's database recognizes, so HOME refuses the deposit.
    /// Hyperspace force-shiny route was tried earlier and rejected by the user as
    /// "wrong location." Unblock individually by sourcing a real Z-A-extracted shiny
    /// .pa9 file with a real HOME tracker (like the Zygarde 50% / 10% fix).
    /// </summary>
    public static bool IsHomeRejectingShinyZALegendary(ushort species)
    {
        // DELIBERATELY EMPTY as of 2026-05-30. Z-A shiny-locked legendaries (150 Mewtwo,
        // 382/383/384 Kyogre/Groudon/Rayquaza, 485 Heatran, 491 Darkrai, 716/717 Xerneas/
        // Yveltal, 807 Zeraora) are all in the same boat: members can still legitimately
        // receive them on a Z-A bot for in-game use. HOME upload from the Z-A save will
        // fail (encounter is shiny-locked there + tracker is fabricated), but the
        // QueueHelper Non-Native embed surfaces that and points members at Celebi-SWSH /
        // Jirachi-SWSH for a HOME-uploadable shiny. Don't block at request time.
        return false;
    }

    /// <summary>
    /// Species that, on a Z-A bot, must be routed through SwSh Max Lair (Dynamax Adventure)
    /// when SHINY is requested. Z-A native encounters at Hyperspace locations are shiny-
    /// locked, ALM falls back to BDSP/PLA with met locations like "Crystal Cavern" that
    /// Z-A's game refuses ("Non-Native, cannot enter HOME"). SwSh Max Lair is shiny-
    /// eligible, and the PK8 → PA9 HOME conversion produces a file with Max Lair met
    /// location AND a valid HOME tracker — accepted by Z-A.
    /// </summary>
    public static bool IsCrownTundraDAShinyForZA(ushort species)
    {
        return species switch
        {
            150 => true, // Mewtwo
            382 => true, // Kyogre
            383 => true, // Groudon
            384 => true, // Rayquaza
            485 => true, // Heatran
            491 => true, // Darkrai
            716 => true, // Xerneas
            717 => true, // Yveltal
            _   => false,
        };
    }

    public static bool IsZALegendaryWithPreMade(ushort species)
    {
        return species switch
        {
            150 => true, // Mewtwo — Lysandre Labs encounter
            382 => true, // Kyogre — Hyperspace Primordial Sea
            383 => true, // Groudon — Hyperspace Desolate Land
            384 => true, // Rayquaza — Hyperspace Sky Pillar
            485 => true, // Heatran — Hyperspace Infernal Arena
            491 => true, // Darkrai — Hyperspace Newmoon Nightmare
            670 => true, // Floette — Eternal Flower (form 5)
            716 => true, // Xerneas — Wild Zone 11
            717 => true, // Yveltal — Rouge Sector 2
            718 => true, // Zygarde — Wild Zone 20 (Power Construct forms 2/3)
            719 => true, // Diancie — Magenta Sector 8
            720 => true, // Hoopa — Hyperspace Lumiose (ALM can't generate this encounter; use real-save pre-made)
            807 => true, // Zeraora — Hyperspace Lumiose (shiny .pa9 in HOME-Ready-Files from HOME Distribution)
            _   => false,
        };
    }

    /// <summary>
    /// Species that have NEVER been distributed shiny in any game — main series,
    /// Pokemon GO, HOME events, past wondercards, anywhere. Shiny requests for
    /// these are rejected at the trade entry point so the pre-made fallback never
    /// gets a chance to force-flip a non-shiny event file's PID into an illegal
    /// "shiny" copy.
    /// </summary>
    public static bool IsTrulyShinyLocked(ushort species)
    {
        return species switch
        {
            489  => true, // Phione  — Manaphy egg hatch, no shiny distribution
            490  => true, // Manaphy — PMD2 / Ranger events all non-shiny
            // 493 Arceus removed — BDSP Ramanas Park (Azure Flute) IS shiny-eligible.
            // Still shiny-locked in SwSh / SV / Z-A — handled by per-game checks
            // (IsZALockedShiny for Z-A; SwSh/SV blocks via IsSwSh*/IsSV* if added).
            494  => true, // Victini — Liberty Ticket + every later event shiny-locked
            720  => true, // Hoopa   — every main series + GO encounter shiny-locked
            801  => true, // Magearna — event-only, shiny never released
            802  => true, // Marshadow — event-only, shiny never released
            893  => true, // Zarude   — Dada Zarude was form-only, base Zarude shiny-locked
            1025 => true, // Pecharunt — SV Mochi Mayhem event, shiny-locked
            _    => false,
        };
    }

    /// <summary>
    /// Species whose only legitimate shiny path is Pokemon GO → HOME transfer with
    /// a real HOME-issued tracker. The bot's pre-made files for these are tool-
    /// generated GO simulations, not extracted from real account transfers, so
    /// Pokemon HOME's server-side database lookup rejects them on deposit (10015).
    /// Used to gate shiny requests in SV (PK9) and BDSP (PB8) where the failure
    /// has been empirically confirmed. Expand as additional species are reported.
    /// </summary>
    public static bool IsHomeRejectingShinyMythical(ushort species)
    {
        return species switch
        {
            // 386 Deoxys removed — user has legit Emerald Birth Island event shiny PB8
            // in HOME-Ready-Files. HOME *may* still reject on deposit (10015 known), but
            // the Switch-side trade succeeds and the user explicitly wants this trade.
            _   => false,
        };
    }

    public static bool IsGoShinyMythical(ushort species)
    {
        return species switch
        {
            // Mythicals (GO + main game shiny-locked)
            151 => true, // Mew
            251 => true, // Celebi
            385 => true, // Jirachi
            386 => true, // Deoxys
            487 => true, // Giratina — restored 2026-05-25: ALM can't generate SV-legal shiny;
                         // pre-made shiny PK9/PK8/PB8/PA8 files exist in HOME-Ready-Files
                         // (Max Lair SwSh shinies HOME-transferred). isGoMyth=true ⇒ forces
                         // fallbackCheck=true which uses those pre-made files.
            489 => true, // Phione
            490 => true, // Manaphy
            492 => true, // Shaymin
            494 => true, // Victini
            648 => true, // Meloetta
            649 => true, // Genesect
            719 => true, // Diancie
            720 => true, // Hoopa
            721 => true, // Volcanion
            801 => true, // Magearna (event-only, ALM exhausts encounters in newer PKHeX)
            802 => true, // Marshadow
            807 => true, // Zeraora
            808 => true, // Meltan
            809 => true, // Melmetal
            893 => true, // Zarude
            // Legendary birds (Galarian forms can't be generated by ALM in PK9)
            144 => true, // Articuno (Galar form)
            145 => true, // Zapdos (Galar form)
            146 => true, // Moltres (Galar form)
            _ => false,
        };
    }

    public static string GetLegalizationHint(IBattleTemplate template, ITrainerInfo sav, PKM pkm, string speciesName)
    {
        var hint = AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm);
        if (hint.Contains("Requested shiny value (ShinyType."))
        {
            hint = $"{speciesName} **cannot** be shiny. Please try again.";
        }
        return hint;
    }

    public static async Task SendTradeErrorEmbedAsync(SocketCommandContext context, ProcessedPokemonResult<T> result)
    {
        var spec = result.ShowdownSet != null && result.ShowdownSet.Species > 0
            ? GameInfo.Strings.Species[result.ShowdownSet.Species]
            : "Unknown";

        var embedBuilder = new EmbedBuilder()
            .WithTitle("Trade Creation Failed")
            .WithColor(new Color(0xE7, 0x4C, 0x3C)) // muted coral red instead of harsh red
            .AddField("Status", $"Failed to create {spec}.")
            .AddField("Reason", result.Error ?? "Unknown error");

        if (!string.IsNullOrEmpty(result.LegalizationHint))
        {
            _ = embedBuilder.AddField("💡 Hint", result.LegalizationHint);
        }

        // Reassurance, worded so it can't be misread as "you got a cooldown": a failed/mistake
        // request never costs the member a trade or applies any cooldown. The bot only counts
        // trades that actually complete.
        embedBuilder.AddField(
            "✅ This Was Free — No Cooldown, No Trade Used",
            "Your mistake did **NOT** put you on cooldown and did **NOT** use a trade. Just fix it and send it again — retry as many times as you need!",
            inline: false);

        embedBuilder.WithFooter("Mistakes never cost you anything • Only completed trades count");

        string userMention = context.User.Mention;
        string messageContent = $"{userMention}, here's the report for your request:";
        var message = await context.Channel.SendMessageAsync(text: messageContent, embed: embedBuilder.Build()).ConfigureAwait(false);
        _ = DeleteMessagesAfterDelayAsync(message, context.Message, 30);
    }

    /// <summary>
    /// Sends a detailed trade error log to configured Full Trade Error Log channels.
    /// </summary>
    public static async Task SendFullTradeErrorLogAsync(SocketCommandContext context, string errorReason, string userRequest, int tradeCode, string? legalizationHint = null)
    {
        var cfg = SysCordSettings.Settings.FullTradeErrorLogChannels;
        if (cfg.List.Count == 0)
            return;

        var user = context.User;
        var guild = (context.Channel as IGuildChannel)?.Guild;
        var channel = context.Channel;

        string serverName = guild?.Name ?? "Direct Message";
        string channelName = channel is IGuildChannel guildChannel ? $"#{guildChannel.Name}" : "DM";
        string channelId = channel.Id.ToString();

        // Get game version from PKM type
        string gameVersion = typeof(T).Name switch
        {
            "PA9" => "ZA",
            "PK9" => "SV",
            "PA8" => "LA",
            "PB8" => "BDSP",
            "PK8" => "SWSH",
            "PB7" => "LGPE",
            _ => "Unknown"
        };

        // Truncate user request if it's too long for embed field (Discord limit is 1024 characters per field)
        string truncatedRequest = userRequest.Length > 950
            ? userRequest.Substring(0, 947) + "..."
            : userRequest;

        var embedBuilder = new EmbedBuilder()
            .WithTitle("**DETAILED TRADE ERROR LOGS**")
            .WithColor(Color.Gold)
            .WithCurrentTimestamp()
            .AddField("**Connected User**", $"{user.Username} ({user.Id})", inline: false)
            .AddField("**Link Trade Code**", tradeCode.ToString("0000 0000"), inline: false)
            .AddField("**Server of Request**", serverName, inline: false)
            .AddField("**Channel of Request**", $"{channelName} ({channelId})", inline: false)
            .AddField("**Game Version of Bot**", gameVersion, inline: false)
            .AddField("**Reason for Error**", errorReason, inline: false);

        // Add legalization hint if available
        if (!string.IsNullOrEmpty(legalizationHint))
        {
            embedBuilder.AddField("**Hint**", legalizationHint, inline: false);
        }

        // Check if we should include Known Trainer Details
        var hub = SysCord<T>.Runner.Hub;
        bool storeTradeCodesEnabled = hub.Config.Trade.TradeConfiguration.StoreTradeCodes;

        if (storeTradeCodesEnabled)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            var tradeDetails = tradeCodeStorage.GetTradeDetails(user.Id);

            if (tradeDetails != null && !string.IsNullOrEmpty(tradeDetails.OT))
            {
                string trainerDetails = $"**OT:** {tradeDetails.OT}\n**TID:** {tradeDetails.TID}\n**SID:** {tradeDetails.SID}";
                embedBuilder.AddField("**Known Trainer Details**", trainerDetails, inline: false);
            }
        }

        embedBuilder.AddField("**User's Request**", $"```\n{truncatedRequest}\n```", inline: false);

        var embed = embedBuilder.Build();

        // Send to all configured Full Trade Error Log channels
        foreach (var logChannel in cfg)
        {
            try
            {
                if (context.Client.GetChannel(logChannel.ID) is ISocketMessageChannel msgChannel)
                {
                    await msgChannel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to send Full Trade Error Log to channel {logChannel.ID}: {ex.Message}", nameof(Helpers<T>));
            }
        }
    }

    /// <summary>
    /// Sends a detailed batch trade error log to configured Full Trade Error Log channels.
    /// </summary>
    public static async Task SendFullBatchTradeErrorLogAsync(SocketCommandContext context, List<BatchTradeError> errors, int tradeCode, int totalTrades)
    {
        var cfg = SysCordSettings.Settings.FullTradeErrorLogChannels;
        if (cfg.List.Count == 0)
            return;

        var user = context.User;
        var guild = (context.Channel as IGuildChannel)?.Guild;
        var channel = context.Channel;

        string serverName = guild?.Name ?? "Direct Message";
        string channelName = channel is IGuildChannel guildChannel ? $"#{guildChannel.Name}" : "DM";
        string channelId = channel.Id.ToString();

        // Get game version from PKM type
        string gameVersion = typeof(T).Name switch
        {
            "PA9" => "ZA",
            "PK9" => "SV",
            "PA8" => "LA",
            "PB8" => "BDSP",
            "PK8" => "SWSH",
            "PB7" => "LGPE",
            _ => "Unknown"
        };

        // Build error summary
        var errorSummary = new System.Text.StringBuilder();
        errorSummary.AppendLine($"**{errors.Count} out of {totalTrades} Pokémon failed:**\n");

        foreach (var error in errors.Take(5)) // Limit to first 5 errors to avoid embed size limits
        {
            errorSummary.AppendLine($"**Trade #{error.TradeNumber}** - {error.SpeciesName}");
            errorSummary.AppendLine($"Error: {error.ErrorMessage}");
            if (!string.IsNullOrEmpty(error.LegalizationHint))
            {
                errorSummary.AppendLine($"Hint: {error.LegalizationHint}");
            }
            errorSummary.AppendLine();
        }

        if (errors.Count > 5)
        {
            errorSummary.AppendLine($"... and {errors.Count - 5} more errors.");
        }

        var embedBuilder = new EmbedBuilder()
            .WithTitle("**DETAILED BATCH TRADE ERROR LOGS**")
            .WithColor(Color.Gold)
            .WithCurrentTimestamp()
            .AddField("**Connected User**", $"{user.Username} ({user.Id})", inline: false)
            .AddField("**Link Trade Code**", tradeCode.ToString("0000 0000"), inline: false)
            .AddField("**Server of Request**", serverName, inline: false)
            .AddField("**Channel of Request**", $"{channelName} ({channelId})", inline: false)
            .AddField("**Game Version of Bot**", gameVersion, inline: false)
            .AddField("**Reason for Error**", $"Batch trade validation failed: {errors.Count}/{totalTrades} Pokémon invalid", inline: false);

        // Check if we should include Known Trainer Details
        var hub = SysCord<T>.Runner.Hub;
        bool storeTradeCodesEnabled = hub.Config.Trade.TradeConfiguration.StoreTradeCodes;

        if (storeTradeCodesEnabled)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            var tradeDetails = tradeCodeStorage.GetTradeDetails(user.Id);

            if (tradeDetails != null && !string.IsNullOrEmpty(tradeDetails.OT))
            {
                string trainerDetails = $"**OT:** {tradeDetails.OT}\n**TID:** {tradeDetails.TID}\n**SID:** {tradeDetails.SID}";
                embedBuilder.AddField("**Known Trainer Details**", trainerDetails, inline: false);
            }
        }

        embedBuilder.AddField("**Error Details**", errorSummary.ToString(), inline: false);

        var embed = embedBuilder.Build();

        // Send to all configured Full Trade Error Log channels
        foreach (var logChannel in cfg)
        {
            try
            {
                if (context.Client.GetChannel(logChannel.ID) is ISocketMessageChannel msgChannel)
                {
                    await msgChannel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to send Full Batch Trade Error Log to channel {logChannel.ID}: {ex.Message}", nameof(Helpers<T>));
            }
        }
    }

    public static T? GetRequest(Download<PKM> dl)
    {
        if (!dl.Success)
            return null;
        return dl.Data switch
        {
            null => null,
            T pk => pk,
            _ => EntityConverter.ConvertToType(dl.Data, typeof(T), out _) as T,
        };
    }

    public static List<Pictocodes> GenerateRandomPictocodes(int count)
    {
        Random rnd = new();
        List<Pictocodes> randomPictocodes = [];
        Array pictocodeValues = Enum.GetValues<Pictocodes>();

        for (int i = 0; i < count; i++)
        {
            Pictocodes randomPictocode = (Pictocodes)pictocodeValues.GetValue(rnd.Next(pictocodeValues.Length))!;
            randomPictocodes.Add(randomPictocode);
        }

        return randomPictocodes;
    }

    // ============================================================================
    // PA9 CROSS-GAME HOME FALLBACK HELPERS
    // ============================================================================

    /// <summary>
    /// Tries every PKM format HOME supports (newest first) and returns the first result
    /// that converts to a legally valid PA9. Used when Z-A generation fails for any reason.
    /// </summary>
    private static PA9? TryGetAsHomePa9(IBattleTemplate template, string speciesName)
    {
        // Lazy delegates — GetTrainerInfo is called inside the try-catch so a
        // failure for one game type is silently skipped without aborting the loop.
        (Func<ITrainerInfo> GetTrainer, string Name)[] sources =
        [
            (() => AutoLegalityWrapper.GetTrainerInfo<PK9>(),  "SV"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PK8>(),  "SWSH"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PA8>(),  "PLA"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PB8>(),  "BDSP"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PK7>(),  "USUM/SM"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PB7>(),  "LGPE"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PK6>(),  "ORAS/XY"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PK5>(),  "BW/B2W2"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PK4>(),  "DPPt/HGSS"),
            (() => AutoLegalityWrapper.GetTrainerInfo<PK3>(),  "RSE/FRLG"),
        ];

        foreach (var (getTrainer, name) in sources)
        {
            try
            {
                var trainerInfo = getTrainer(); // invoked here so any throw is caught below
                var generated = trainerInfo.GetLegal(template, out _);
                if (generated == null)
                    continue;

                var converted = EntityConverter.ConvertToType(generated, typeof(PA9), out _);
                if (converted is not PA9 pa9)
                    continue;

                if (!new LegalityAnalysis(pa9).Valid)
                    continue;

                LogUtil.LogInfo(
                    $"{speciesName}: HOME fallback succeeded from {name} (Version={pa9.Version})",
                    "PA9HomeFallback");
                return pa9;
            }
            catch { }
        }

        return null;
    }

    // ============================================================================
    // END OF PA9 SHINY FALLBACK HELPERS
    // ============================================================================

    public static async Task<T?> ProcessTradeAttachmentAsync(SocketCommandContext context)
    {
        var attachment = context.Message.Attachments.FirstOrDefault();
        if (attachment == default)
        {
            _ = await context.Channel.SendMessageAsync("No attachment provided!").ConfigureAwait(false);
            return null;
        }

        var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
        var pk = GetRequest(att);

        if (pk == null)
        {
            _ = await context.Channel.SendMessageAsync("Attachment provided is not compatible with this module!").ConfigureAwait(false);
            return null;
        }

        // Block shiny Hoopa file uploads — Hoopa is shiny-locked in every main series game.
        // Mirrors the showdown-set check in ProcessShowdownSetAsync at line ~179.
        if (pk.Species == (ushort)Species.Hoopa && pk.IsShiny)
        {
            _ = await context.Channel.SendMessageAsync("**Hoopa cannot be Shiny.** Hoopa is shiny-locked in every main series Pokémon game. There is no legal way to obtain a shiny Hoopa.").ConfigureAwait(false);
            return null;
        }

        return pk;
    }

    public static (string filter, int page) ParseListArguments(string args)
    {
        string filter = "";
        int page = 1;
        var parts = args.Split([' '], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            if (int.TryParse(parts.Last(), out int parsedPage))
            {
                page = parsedPage;
                filter = string.Join(" ", parts.Take(parts.Length - 1));
            }
            else
            {
                filter = string.Join(" ", parts);
            }
        }

        return (filter, page);
    }

    public static async Task AddTradeToQueueAsync(
        SocketCommandContext context,
        int code,
        string trainerName,
        T? pk,
        RequestSignificance sig,
        SocketUser usr,
        bool isBatchTrade = false,
        int batchTradeNumber = 1,
        int totalBatchTrades = 1,
        bool isHiddenTrade = false,
        bool isMysteryEgg = false,
        List<Pictocodes>? lgcode = null,
        PokeTradeType tradeType = PokeTradeType.Specific,
        bool ignoreAutoOT = false, bool setEdited = false,
        bool isNonNative = false)
    {
        lgcode ??= GenerateRandomPictocodes(3);

        if (pk is not null && !pk.CanBeTraded())
        {
            var reply = await context.Channel.SendMessageAsync("Provided Pokémon content is blocked from trading!").ConfigureAwait(false);
            await Task.Delay(6000).ConfigureAwait(false);
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }

        // Block non-tradable items using PKHeX's ItemRestrictions
        if (pk is not null && TradeExtensions<T>.IsItemBlocked(pk))
        {
            var itemName = pk.HeldItem > 0 ? GameInfo.GetStrings("en").Item[pk.HeldItem] : "(none)";
            var reply = await context.Channel.SendMessageAsync($"Trade blocked: The held item '{itemName}' cannot be traded.").ConfigureAwait(false);
            await Task.Delay(6000).ConfigureAwait(false);
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }


        var la = new LegalityAnalysis(pk!);

        // Auto-fix nickname-only issues on attachments by clearing nickname and re-validating
        if (!la.Valid && la.Results.Any(r => r.Identifier is CheckIdentifier.Nickname))
        {
            var clone = (T)pk!.Clone();
            _ = clone.ClearNickname();
            var laNick = new LegalityAnalysis(clone);
            if (laNick.Valid)
            {
                pk = clone;
                la = laNick;
            }
        }

        // HOME-simulated wondercard files (newer than our PKHeX library) trip these checks:
        // "Unable to match to a Mystery Gift in the database" + Souvenir Ribbon + Fateful Encounter.
        // The files themselves are legal — our local PKHeX is just outdated. Bypass these specific
        // failures so trusted pre-made files ship through.
        var laReport = la.Report();
        bool isHomeWondercardOldPkhexIssue = !la.Valid && laReport.Contains(
            "Unable to match to a Mystery Gift", StringComparison.OrdinalIgnoreCase);
        // Pre-made GO-shiny mythicals carry a Met Date from when the GO event was live.
        // Once the distribution window closes, PKHeX flags the date as stale even though the
        // Pokemon itself is legitimate. Bypass for trusted pre-made files.
        bool isStaleMetDateIssue = !la.Valid && laReport.Contains(
            "Met Date is outside of distribution window", StringComparison.OrdinalIgnoreCase);

        // Mythical legendary bypass (second gate): covers Z-A native catches AND
        // HOME-transferred mythicals across all formats (PA9 / PK9 SV / PK8 SwSh).
        // PKHeX may flag Encounter/Ability/Move/Misc/Height/Weight mismatches due to
        // new-game-data quirks; bypass for known mythical/legendary species.
        bool isMythicalMetLocation2 = pk is (PA9 or PK9 or PK8) &&
            ((pk is PA9 && pk.MetLocation is > 0 and <= 350) || pk.MetLocation >= 30000);
        bool isZANativeLegendaryIssue = !la.Valid && isMythicalMetLocation2 &&
            pk.Species is 150 or 151 or 251 or 380 or 381 or 382 or 383 or 384 or 385 or 386
                or 485 or 489 or 490 or 491 or 492 or 493 or 494
                or 638 or 639 or 640 or 647 or 648 or 649
                or 670 or 716 or 717 or 718 or 719 or 720 or 721
                or 801 or 802 or 807 or 808 or 809 &&
            (laReport.Contains("Unable to match an encounter from origin game", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("Ability mismatch for encounter", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("Height should be", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("Weight should be", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("Scale should be", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("Misc", StringComparison.OrdinalIgnoreCase));

        // BDSP legendary: met location was overridden post-ALM to the canonical spot.
        // PKHeX flags an encounter mismatch; bypass since the override is intentional.
        bool isBDSPLakeTrioIssue = !la.Valid && pk is PB8 &&
            pk.Species is 480 or 481 or 482 or 485 or 487 or 488 or 491 or 492;

        // BDSP Gen-3-origin (Emerald Birth Island Deoxys, FRLG event mons) — when the
        // bot flips the PID to satisfy shiny↔non-shiny, the seed-locked
        // PID/EC/IVs/Nature correlation breaks and PKHeX flags it. The file is real,
        // the BDSP game itself accepts it (its validator is looser than PKHeX's), and
        // the user wanted this trade. Bypass scoped to PB8 + flipped-PID symptom set.
        bool isBDSPPidFlipIssue = !la.Valid && pk is PB8 &&
            (laReport.Contains("PID+ correlation does not match", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("PID should be equal to EC", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("PID-Nature mismatch", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("Unable to match to a Mystery Gift", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("Invalid Ribbons: Classic", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("Fateful Encounter should", StringComparison.OrdinalIgnoreCase));

        // Z-A gifts (Floette-Eternal, story gifts) after legality-preserving customization
        // are legal except possibly "Held item is unreleased" -- PKHeX's Gen9a item table
        // is incomplete but the item is real. Ship those. PID strings kept only as a safety
        // net for genuinely seed-broken files. Mirrors isZAPidCorrelation in the pre-made
        // loader so a customized pre-made isn't re-rejected at this gate. Scope: PA9 only.
        bool isZAPidCorrelationIssue = !la.Valid && pk is PA9 &&
            (laReport.Contains("Held item is unreleased", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("PID+ correlation does not match", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("PID should be equal to EC", StringComparison.OrdinalIgnoreCase)
             || laReport.Contains("PID-Nature mismatch", StringComparison.OrdinalIgnoreCase));

        if (!la.Valid && !isHomeWondercardOldPkhexIssue && !isStaleMetDateIssue && !isZANativeLegendaryIssue && !isBDSPLakeTrioIssue && !isBDSPPidFlipIssue && !isZAPidCorrelationIssue)
        {
            string responseMessage;
            if (pk?.IsEgg == true)
            {
                string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
                responseMessage = $"Invalid Showdown Set for the {speciesName} egg. Please review your information and try again.\n\nLegality Report:\n```\n{la.Report()}\n```";
            }
            else
            {
                string speciesName = SpeciesName.GetSpeciesName(pk!.Species, (int)LanguageID.English);
                responseMessage = $"{speciesName} attachment is not legal, and cannot be traded!\n\nLegality Report:\n```\n{la.Report()}\n```";
            }
            var reply = await context.Channel.SendMessageAsync(responseMessage).ConfigureAwait(false);
            await Task.Delay(6000);
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }
        if (isHomeWondercardOldPkhexIssue)
        {
            LogUtil.LogInfo($"[Helpers] AddTradeToQueueAsync: bypassing legality for HOME wondercard species {pk?.Species} (PKHeX too old to validate)", "Helpers");
        }
        if (isStaleMetDateIssue)
        {
            LogUtil.LogInfo($"[Helpers] AddTradeToQueueAsync: bypassing legality for species {pk?.Species} (pre-made file Met Date past distribution window)", "Helpers");
        }

        if (Info.Hub.Config.Legality.DisallowNonNatives && isNonNative)
        {
            string speciesName = SpeciesName.GetSpeciesName(pk!.Species, (int)LanguageID.English);
            _ = await context.Channel.SendMessageAsync($"This **{speciesName}** is not native to this game, and cannot be traded! Trade with the correct bot, then trade to HOME.").ConfigureAwait(false);
            return;
        }

        if (Info.Hub.Config.Legality.DisallowTracked && pk is IHomeTrack { HasTracker: true })
        {
            string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
            _ = await context.Channel.SendMessageAsync($"This {speciesName} file is tracked by HOME, and cannot be traded!").ConfigureAwait(false);
            return;
        }

        // Past gen file fix is now handled in ProcessShowdownSetAsync before this point

        await QueueHelper<T>.AddToQueueAsync(context, code, trainerName, sig, pk!, PokeRoutineType.LinkTrade,
            tradeType, usr, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg,
            lgcode: lgcode, ignoreAutoOT: ignoreAutoOT, setEdited: setEdited, isNonNative: isNonNative).ConfigureAwait(false);
    }

}
