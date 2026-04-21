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
        // Auto-convert bullet points and other unicode dashes to standard dashes
        // Users on mobile often paste with bullets (•) or em-dashes (—) which break parsing
        content = content.Replace("•", "-").Replace("·", "-").Replace("—", "-").Replace("–", "-").Replace("‐", "-");

        // Strip Discord blockquote prefixes "| " from each line (mobile copy/paste artifact)
        // This was causing moves/nature/EVs to be silently ignored as invalid lines
        var stripLines = content.Split('\n');
        for (int i = 0; i < stripLines.Length; i++)
        {
            var trimmed = stripLines[i].TrimStart();
            if (trimmed.StartsWith("| "))
                stripLines[i] = trimmed.Substring(2);
            else if (trimmed.StartsWith("|"))
                stripLines[i] = trimmed.Substring(1);
        }
        content = string.Join('\n', stripLines);

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

        // Fix reversed nickname format: "(Species) Nickname" -> "Nickname (Species)"
        // The Showdown parser expects "Nickname (Species)" format
        if (filteredLines.Length > 0)
        {
            var firstLine = filteredLines[0].Trim();
            var reversedNickMatch = System.Text.RegularExpressions.Regex.Match(firstLine, @"^\(([^)]+)\)\s+(.+)$");
            if (reversedNickMatch.Success)
            {
                var species = reversedNickMatch.Groups[1].Value.Trim();
                var nickname = reversedNickMatch.Groups[2].Value.Trim();
                // Check if the item (@) is in the nickname part
                var atIndex = nickname.IndexOf('@');
                string item = "";
                if (atIndex >= 0)
                {
                    item = nickname.Substring(atIndex);
                    nickname = nickname.Substring(0, atIndex).Trim();
                }
                filteredLines[0] = $"{nickname} ({species}){(item.Length > 0 ? " " + item : "")}";
                LogUtil.LogInfo($"[Nickname Fix] Reversed format detected. Fixed: '{firstLine}' -> '{filteredLines[0]}'", "Helpers");
            }
        }

        var contentWithoutLanguage = string.Join('\n', filteredLines);

        // Now parse the ShowdownSet without the Language line
        if (!ShowdownParsing.TryParseAnyLanguage(contentWithoutLanguage, out ShowdownSet? set) || set == null || set.Species == 0)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = "Unable to parse Showdown set. Could not identify the Pokémon species.",
                ShowdownSet = set
            });
        }

        LogUtil.LogInfo($"[ShowdownParse] Species={set.Species}, Shiny={set.Shiny}, Nickname='{set.Nickname}', FormName='{set.FormName}', Text='{set.Text.Replace("\n"," | ")}'", "Helpers");

        // EV/IV/Level validation is handled by the detailed legality error system after ALM.

        // Reject shiny requests for species with no legal shiny distribution.
        if (set.Shiny)
        {
            // Gen 9 shiny-locked (no legal shiny path, including events)
            var svShinyLocked = new Dictionary<int, string>
            {
                { 1009, "Walking Wake" },
                { 1010, "Iron Leaves" },
                { 1017, "Ogerpon" },
                { 1024, "Terapagos" },
                { 1025, "Pecharunt" },
            };
            // Gen 8 SWSH shiny-locked
            var swshShinyLocked = new Dictionary<int, string>
            {
                { 893, "Zarude" },
                { 898, "Calyrex" },
            };
            Dictionary<int, string>? activeLock = null;
            if (typeof(T) == typeof(PK9) || typeof(T) == typeof(PA9)) activeLock = svShinyLocked;
            else if (typeof(T) == typeof(PK8)) activeLock = swshShinyLocked;

            if (activeLock != null && activeLock.TryGetValue(set.Species, out var lockReason))
            {
                return Task.FromResult(new ProcessedPokemonResult<T>
                {
                    Error = $"**{lockReason}** is shiny-locked and cannot be traded as shiny.",
                    ShowdownSet = set
                });
            }
        }

        var template = AutoLegalityWrapper.GetTemplate(set);

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

        // Auto-fix: silently ignore invalid moves/items instead of failing the trade
        // - Invalid moves are dropped (Pokemon can re-learn moves in-game with relearn enabled)
        // - Invalid items are replaced with Ability Patch (worth $125k, sellable for any item)
        if (actualInvalidLines.Count != 0)
        {
            LogUtil.LogInfo($"[AutoFix] Ignoring {actualInvalidLines.Count} invalid line(s): {string.Join(", ", actualInvalidLines.Select(l => l.Value))}", "Helpers");
            // Continue anyway - ALM will skip the invalid moves/items during generation
        }

        // Use language-specific trainer for generation so all internal fields match
        var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);

        // Fix for Asian languages: Truncate OT to 6 characters max
        // Japanese, Korean, ChineseS, ChineseT only support 6 character OT names
        if (finalLanguage == (byte)LanguageID.Japanese ||
            finalLanguage == (byte)LanguageID.Korean ||
            finalLanguage == (byte)LanguageID.ChineseS ||
            finalLanguage == (byte)LanguageID.ChineseT)
        {
            if (sav.OT.Length > 6)
            {
                // Create a new trainer with truncated OT
                var truncatedOT = sav.OT.Substring(0, 6);
                sav = new SimpleTrainerInfo(sav.Version)
                {
                    OT = truncatedOT,
                    TID16 = sav.TID16,
                    SID16 = sav.SID16,
                    Language = sav.Language,
                    Generation = sav.Generation
                };
            }
        }

        PKM pkm;
        string result;

        // Generate egg or normal pokemon based on isEgg flag
        if (isEgg)
        {
            // Create a proper RegenTemplate from the ShowdownSet
            var regenTemplate = new RegenTemplate(set);

            // Generate egg using ALM
            pkm = sav.GenerateEgg(regenTemplate, out var eggResult);
            result = eggResult.ToString();

            // FIX: ALM doesn't always respect the ball from the content for eggs
            // Manually parse and apply the ball if user specified one
            if (pkm != null)
            {
                var ballLine = contentLines.FirstOrDefault(l => l.TrimStart().StartsWith("Ball:", StringComparison.OrdinalIgnoreCase));
                if (ballLine != null)
                {
                    var ballName = ballLine.Split(':')[1].Trim().Replace(" ", string.Empty);
                    var balls = GameInfo.Strings.balllist;
                    int ballIndex = Array.FindIndex(balls, z => z.Replace(" ", string.Empty).Equals(ballName, StringComparison.OrdinalIgnoreCase));
                    if (ballIndex > 0)
                    {
                        pkm.Ball = (byte)ballIndex;
                        pkm.RefreshChecksum();
                    }
                }
            }
        }
        else
        {
            LogUtil.LogInfo($"[Generation] Species={template.Species}, Form={template.Form}, T={typeof(T).Name}, sav.Version={sav.Version}", "Helpers");
            // Vivillon form workaround: ALM can only generate Meadow (form 6) in ZA.
            // Force template to Meadow for generation, then fix the form afterwards.
            if (template.Species == 666 && template.Form != 6 && typeof(T) == typeof(PA9))
            {
                LogUtil.LogInfo($"[Vivillon] Overriding template form {template.Form} to Meadow (6) for generation", "Vivillon");
                var showdownText = set.Text.Replace($"Vivillon-{set.FormName}", "Vivillon-Meadow");
                if (showdownText == set.Text)
                    showdownText = "Vivillon-Meadow\nLevel: 100";
                var meadowSet = new ShowdownSet(showdownText);
                var meadowTemplate = AutoLegalityWrapper.GetTemplate(meadowSet);
                pkm = sav.GetLegal(meadowTemplate, out result);
            }
            // Floette-Eternal: PKHeX now has proper Z-A encounter data, no workaround needed.
            // Melmetal/Meltan in SWSH: ALM cannot generate GO-origin encounters.
            // Build manually, bypassing ALM entirely. Skip legality for these specific species.
            else if ((template.Species == 808 || template.Species == 809) && typeof(T) == typeof(PK8))
            {
                var specName = template.Species == 809 ? "Melmetal" : "Meltan";
                LogUtil.LogInfo($"[{specName}] Building manually - ALM cannot generate GO encounters", "Helpers");

                var goMon = new PK8();
                goMon.Species = (ushort)template.Species;
                goMon.Form = 0;
                goMon.Gender = 2; // Genderless
                goMon.CurrentLevel = 100;
                goMon.MetLevel = 25;
                goMon.MetLocation = 30012; // HOME transfer from GO
                goMon.Version = GameVersion.GO;
                goMon.Ball = 4; // Poke Ball
                goMon.Language = sav.Language;
                goMon.OriginalTrainerName = sav.OT;
                goMon.TID16 = sav.TID16;
                goMon.SID16 = sav.SID16;
                goMon.OriginalTrainerGender = (byte)sav.Gender;

                // Nature
                var nature = set.Nature != Nature.Random ? set.Nature : Nature.Adamant;
                goMon.Nature = nature;
                goMon.StatNature = nature;

                // Ability
                goMon.AbilityNumber = 1;
                if (template.Species == 809)
                    goMon.Ability = (int)Ability.IronFist;
                else
                    goMon.Ability = (int)Ability.MagnetPull;

                // Moves - use defaults that are legal for GO-origin transfer
                if (template.Species == 809)
                {
                    goMon.Move1 = 742; // Double Iron Bash (signature)
                    goMon.Move2 = 8;   // Ice Punch (TR)
                    goMon.Move3 = 276; // Superpower (TR)
                    goMon.Move4 = 9;   // Thunder Punch (TR)
                }
                else
                {
                    goMon.Move1 = 84;  // Thunder Shock
                    goMon.Move2 = 430; // Flash Cannon
                    goMon.Move3 = 86;  // Thunder Wave
                    goMon.Move4 = 29;  // Headbutt
                }

                // IVs - all 31
                goMon.IV_HP = 31; goMon.IV_ATK = 31; goMon.IV_DEF = 31;
                goMon.IV_SPA = 31; goMon.IV_SPD = 31; goMon.IV_SPE = 31;
                if (set.IVs != null && set.IVs.Count() == 6)
                {
                    goMon.IV_HP = set.IVs[0]; goMon.IV_ATK = set.IVs[1]; goMon.IV_DEF = set.IVs[2];
                    goMon.IV_SPE = set.IVs[3]; goMon.IV_SPA = set.IVs[4]; goMon.IV_SPD = set.IVs[5];
                }

                // EVs
                if (set.EVs != null && set.EVs.Any(e => e > 0))
                {
                    goMon.EV_HP = set.EVs[0]; goMon.EV_ATK = set.EVs[1]; goMon.EV_DEF = set.EVs[2];
                    goMon.EV_SPE = set.EVs[3]; goMon.EV_SPA = set.EVs[4]; goMon.EV_SPD = set.EVs[5];
                }

                // Held item
                if (set.HeldItem > 0) goMon.HeldItem = set.HeldItem;

                // Height/Weight
                goMon.HeightScalar = 128;
                goMon.WeightScalar = 128;

                // Date - within GO transfer distribution window
                goMon.MetDate = new DateOnly(2024, 6, 15);

                // Shiny
                if (set.Shiny)
                    goMon.SetIsShiny(true);

                goMon.HealPP();
                goMon.ClearNickname();
                goMon.RefreshChecksum();

                pkm = goMon;
                result = "Regenerated";
                LogUtil.LogInfo($"[{specName}] Built manually. IsShiny={goMon.IsShiny}, Valid={new LegalityAnalysis(goMon).Valid}", "Helpers");
            }
            // Deoxys: Generate as GO-origin for any game. PKHeX 26.3.20 can't match encounters.
            else if (template.Species == 386 && (typeof(T) == typeof(PK8) || typeof(T) == typeof(PB8) || typeof(T) == typeof(PK9)))
            {
                LogUtil.LogInfo($"[Deoxys] Building as GO-origin for {typeof(T).Name}", "Helpers");
                dynamic goDeoxys = Activator.CreateInstance(typeof(T))!;
                goDeoxys.Species = (ushort)386;
                goDeoxys.Form = (byte)template.Form;
                goDeoxys.Gender = 2;
                goDeoxys.CurrentLevel = (byte)100;
                goDeoxys.MetLevel = (byte)15;
                goDeoxys.MetLocation = (ushort)30012;
                goDeoxys.Version = GameVersion.GO;
                goDeoxys.Ball = (byte)4;
                goDeoxys.Language = (byte)sav.Language;
                goDeoxys.OriginalTrainerName = "GO";
                goDeoxys.OriginalTrainerGender = (byte)0;
                goDeoxys.TID16 = (ushort)12345;
                goDeoxys.SID16 = (ushort)54321;
                goDeoxys.HandlingTrainerName = sav.OT;
                goDeoxys.HandlingTrainerGender = (byte)sav.Gender;
                goDeoxys.HandlingTrainerLanguage = (byte)sav.Language;
                goDeoxys.CurrentHandler = (byte)1;
                goDeoxys.Ability = (int)Ability.Pressure;
                goDeoxys.AbilityNumber = 1;
                goDeoxys.IV_HP = 31; goDeoxys.IV_ATK = 31; goDeoxys.IV_DEF = 31;
                goDeoxys.IV_SPA = 31; goDeoxys.IV_SPD = 31; goDeoxys.IV_SPE = 31;
                goDeoxys.HeightScalar = (byte)128; goDeoxys.WeightScalar = (byte)128;
                goDeoxys.MetDate = new DateOnly(2022, 3, 15);
                var dNature = set.Nature != Nature.Random ? set.Nature : Nature.Timid;
                goDeoxys.Nature = dNature; goDeoxys.StatNature = dNature;
                goDeoxys.Move1 = set.Moves?.Length > 0 && set.Moves[0] != 0 ? set.Moves[0] : (ushort)94;
                goDeoxys.Move2 = set.Moves?.Length > 1 && set.Moves[1] != 0 ? set.Moves[1] : (ushort)63;
                goDeoxys.Move3 = set.Moves?.Length > 2 && set.Moves[2] != 0 ? set.Moves[2] : (ushort)0;
                goDeoxys.Move4 = set.Moves?.Length > 3 && set.Moves[3] != 0 ? set.Moves[3] : (ushort)0;
                if (set.EVs != null && set.EVs.Any(e => e > 0))
                {
                    goDeoxys.EV_HP = set.EVs[0]; goDeoxys.EV_ATK = set.EVs[1]; goDeoxys.EV_DEF = set.EVs[2];
                    goDeoxys.EV_SPE = set.EVs[3]; goDeoxys.EV_SPA = set.EVs[4]; goDeoxys.EV_SPD = set.EVs[5];
                }
                if (set.HeldItem > 0) goDeoxys.HeldItem = set.HeldItem;
                if (set.Shiny) ((PKM)goDeoxys).SetIsShiny(true);
                ((PKM)goDeoxys).HealPP(); ((PKM)goDeoxys).ClearNickname(); ((PKM)goDeoxys).RefreshChecksum();
                pkm = (PKM)goDeoxys; result = "Regenerated";
            }
            // Celebi in SWSH: Generate as GO-origin (same pattern as Melmetal/Mew).
            // PKHeX 26.3.20 can't match non-shiny Celebi encounters in PK8 directly.
            else if (template.Species == 251 && typeof(T) == typeof(PK8))
            {
                LogUtil.LogInfo($"[Celebi] Building as GO-origin for PK8", "Helpers");
                var goCelebi = new PK8
                {
                    Species = 251, Form = 0, Gender = 2, CurrentLevel = 100,
                    MetLevel = 15, MetLocation = 30012, Version = GameVersion.GO, Ball = 4,
                    Language = sav.Language,
                    OriginalTrainerName = "GO", OriginalTrainerGender = 0,
                    TID16 = 12345, SID16 = 54321,
                    HandlingTrainerName = sav.OT, HandlingTrainerGender = (byte)sav.Gender,
                    HandlingTrainerLanguage = (byte)sav.Language, CurrentHandler = 1,
                    Ability = (int)Ability.NaturalCure, AbilityNumber = 1,
                    IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
                    HeightScalar = 128, WeightScalar = 128,
                    MetDate = new DateOnly(2022, 3, 15),
                };
                var cNature = set.Nature != Nature.Random ? set.Nature : Nature.Timid;
                goCelebi.Nature = cNature; goCelebi.StatNature = cNature;
                goCelebi.Move1 = set.Moves?.Length > 0 && set.Moves[0] != 0 ? set.Moves[0] : (ushort)94;  // Psychic
                goCelebi.Move2 = set.Moves?.Length > 1 && set.Moves[1] != 0 ? set.Moves[1] : (ushort)202; // Giga Drain
                goCelebi.Move3 = set.Moves?.Length > 2 && set.Moves[2] != 0 ? set.Moves[2] : (ushort)105; // Recover
                goCelebi.Move4 = set.Moves?.Length > 3 && set.Moves[3] != 0 ? set.Moves[3] : (ushort)0;
                if (set.EVs != null && set.EVs.Any(e => e > 0))
                {
                    goCelebi.EV_HP = set.EVs[0]; goCelebi.EV_ATK = set.EVs[1]; goCelebi.EV_DEF = set.EVs[2];
                    goCelebi.EV_SPE = set.EVs[3]; goCelebi.EV_SPA = set.EVs[4]; goCelebi.EV_SPD = set.EVs[5];
                }
                if (set.HeldItem > 0) goCelebi.HeldItem = set.HeldItem;
                if (set.Shiny) goCelebi.SetIsShiny(true);
                goCelebi.HealPP(); goCelebi.ClearNickname(); goCelebi.RefreshChecksum();
                pkm = goCelebi; result = "Regenerated";
            }
            // Jirachi in SWSH: Same GO-origin pattern.
            else if (template.Species == 385 && typeof(T) == typeof(PK8))
            {
                LogUtil.LogInfo($"[Jirachi] Building as GO-origin for PK8", "Helpers");
                var goJirachi = new PK8
                {
                    Species = 385, Form = 0, Gender = 2, CurrentLevel = 100,
                    MetLevel = 15, MetLocation = 30012, Version = GameVersion.GO, Ball = 4,
                    Language = sav.Language,
                    OriginalTrainerName = "GO", OriginalTrainerGender = 0,
                    TID16 = 12345, SID16 = 54321,
                    HandlingTrainerName = sav.OT, HandlingTrainerGender = (byte)sav.Gender,
                    HandlingTrainerLanguage = (byte)sav.Language, CurrentHandler = 1,
                    Ability = (int)Ability.SereneGrace, AbilityNumber = 1,
                    IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
                    HeightScalar = 128, WeightScalar = 128,
                    MetDate = new DateOnly(2022, 3, 15),
                };
                var jNature = set.Nature != Nature.Random ? set.Nature : Nature.Timid;
                goJirachi.Nature = jNature; goJirachi.StatNature = jNature;
                goJirachi.Move1 = set.Moves?.Length > 0 && set.Moves[0] != 0 ? set.Moves[0] : (ushort)248; // Meteor Mash
                goJirachi.Move2 = set.Moves?.Length > 1 && set.Moves[1] != 0 ? set.Moves[1] : (ushort)94;  // Psychic
                goJirachi.Move3 = set.Moves?.Length > 2 && set.Moves[2] != 0 ? set.Moves[2] : (ushort)0;
                goJirachi.Move4 = set.Moves?.Length > 3 && set.Moves[3] != 0 ? set.Moves[3] : (ushort)0;
                if (set.EVs != null && set.EVs.Any(e => e > 0))
                {
                    goJirachi.EV_HP = set.EVs[0]; goJirachi.EV_ATK = set.EVs[1]; goJirachi.EV_DEF = set.EVs[2];
                    goJirachi.EV_SPE = set.EVs[3]; goJirachi.EV_SPA = set.EVs[4]; goJirachi.EV_SPD = set.EVs[5];
                }
                if (set.HeldItem > 0) goJirachi.HeldItem = set.HeldItem;
                if (set.Shiny) goJirachi.SetIsShiny(true);
                goJirachi.HealPP(); goJirachi.ClearNickname(); goJirachi.RefreshChecksum();
                pkm = goJirachi; result = "Regenerated";
            }
            // Mew in SWSH: Shiny-locked from all in-game encounters. PKHeX's standard
            // encounter API cannot generate a legal shiny Mew in PK8 (GO encounter data
            // not exposed through GenerateEncounters). Build manually as GO-origin.
            else if (template.Species == 151 && typeof(T) == typeof(PK8) && set.Shiny)
            {
                LogUtil.LogInfo($"[Mew] Building manually as GO-origin (shiny-locked otherwise)", "Helpers");
                var goMew = new PK8
                {
                    Species = 151, Form = 0, Gender = 2, CurrentLevel = 100,
                    MetLevel = 15, MetLocation = 30012, Version = GameVersion.GO, Ball = 4,
                    Language = sav.Language,
                    OriginalTrainerName = "GO", OriginalTrainerGender = 0,
                    TID16 = 12345, SID16 = 54321,
                    HandlingTrainerName = sav.OT, HandlingTrainerGender = (byte)sav.Gender,
                    HandlingTrainerLanguage = (byte)sav.Language, CurrentHandler = 1,
                    Ability = (int)Ability.Synchronize, AbilityNumber = 1,
                    IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
                    HeightScalar = 128, WeightScalar = 128,
                    MetDate = new DateOnly(2022, 3, 15),
                };
                var mewNature = set.Nature != Nature.Random ? set.Nature : Nature.Timid;
                goMew.Nature = mewNature; goMew.StatNature = mewNature;
                goMew.Move1 = set.Moves?.Length > 0 && set.Moves[0] != 0 ? set.Moves[0] : (ushort)94;
                goMew.Move2 = set.Moves?.Length > 1 && set.Moves[1] != 0 ? set.Moves[1] : (ushort)1;
                goMew.Move3 = set.Moves?.Length > 2 && set.Moves[2] != 0 ? set.Moves[2] : (ushort)0;
                goMew.Move4 = set.Moves?.Length > 3 && set.Moves[3] != 0 ? set.Moves[3] : (ushort)0;
                if (set.EVs != null && set.EVs.Any(e => e > 0))
                {
                    goMew.EV_HP = set.EVs[0]; goMew.EV_ATK = set.EVs[1]; goMew.EV_DEF = set.EVs[2];
                    goMew.EV_SPE = set.EVs[3]; goMew.EV_SPA = set.EVs[4]; goMew.EV_SPD = set.EVs[5];
                }
                if (set.HeldItem > 0) goMew.HeldItem = set.HeldItem;
                goMew.SetIsShiny(true);
                goMew.HealPP(); goMew.ClearNickname(); goMew.RefreshChecksum();
                pkm = goMew; result = "Regenerated";
            }
            // Jirachi in SWSH: Shiny-locked. Same pattern as Mew — manual GO-origin build.
            else if (template.Species == 385 && typeof(T) == typeof(PK8) && set.Shiny)
            {
                LogUtil.LogInfo($"[Jirachi] Building manually as GO-origin", "Helpers");
                var goJirachi = new PK8
                {
                    Species = 385, Form = 0, Gender = 2, CurrentLevel = 100,
                    MetLevel = 5, MetLocation = 30012, Version = GameVersion.GO, Ball = 4,
                    Language = sav.Language,
                    OriginalTrainerName = "GO", OriginalTrainerGender = 0,
                    TID16 = 12345, SID16 = 54321,
                    HandlingTrainerName = sav.OT, HandlingTrainerGender = (byte)sav.Gender,
                    HandlingTrainerLanguage = (byte)sav.Language, CurrentHandler = 1,
                    Ability = (int)Ability.SereneGrace, AbilityNumber = 1,
                    IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
                    HeightScalar = 128, WeightScalar = 128,
                    MetDate = new DateOnly(2022, 3, 15),
                };
                var jNature = set.Nature != Nature.Random ? set.Nature : Nature.Timid;
                goJirachi.Nature = jNature; goJirachi.StatNature = jNature;
                goJirachi.Move1 = set.Moves?.Length > 0 && set.Moves[0] != 0 ? set.Moves[0] : (ushort)248;
                goJirachi.Move2 = set.Moves?.Length > 1 && set.Moves[1] != 0 ? set.Moves[1] : (ushort)94;
                goJirachi.Move3 = set.Moves?.Length > 2 && set.Moves[2] != 0 ? set.Moves[2] : (ushort)0;
                goJirachi.Move4 = set.Moves?.Length > 3 && set.Moves[3] != 0 ? set.Moves[3] : (ushort)0;
                if (set.EVs != null && set.EVs.Any(e => e > 0))
                {
                    goJirachi.EV_HP = set.EVs[0]; goJirachi.EV_ATK = set.EVs[1]; goJirachi.EV_DEF = set.EVs[2];
                    goJirachi.EV_SPE = set.EVs[3]; goJirachi.EV_SPA = set.EVs[4]; goJirachi.EV_SPD = set.EVs[5];
                }
                if (set.HeldItem > 0) goJirachi.HeldItem = set.HeldItem;
                goJirachi.SetIsShiny(true);
                goJirachi.HealPP(); goJirachi.ClearNickname(); goJirachi.RefreshChecksum();
                pkm = goJirachi; result = "Regenerated";
            }
            // Giratina (Altered or Origin): Origin form requires Griseous Orb (PK8/PB8/PA8) or
            // Griseous Core (PK9). In SV/PK9, Giratina has no native shiny encounter — we must
            // generate in BDSP (PB8 native shiny) and convert up via HOME transfer chain.
            else if (template.Species == 487)
            {
                LogUtil.LogInfo($"[Giratina] Generating form={template.Form} shiny={set.Shiny} for {typeof(T).Name}", "Helpers");
                pkm = sav.GetLegal(template, out result);
                var giraValid = pkm != null && new LegalityAnalysis(pkm).Valid;
                if (!giraValid)
                {
                    LogUtil.LogInfo($"[Giratina] Direct generation failed, trying PB8 (BDSP) fallback", "Helpers");
                    try
                    {
                        ITrainerInfo bdspSav = new SimpleTrainerInfo(GameVersion.BD)
                        {
                            OT = sav.OT, TID16 = sav.TID16, SID16 = sav.SID16, Language = sav.Language,
                        };
                        var pb8 = bdspSav.GetLegal(template, out var pb8Result);
                        if (pb8 != null && pb8.Species == 487)
                        {
                            // PB8 Giratina Origin needs Griseous Orb to hold form
                            pb8.Form = (byte)template.Form;
                            if (template.Form == 1 && pb8.HeldItem == 0) pb8.HeldItem = 112;
                            pb8.ClearNickname();
                            pb8.RefreshChecksum();
                            var converted = EntityConverter.ConvertToType(pb8, typeof(T), out var convRes);
                            if (converted is T convTarget && convTarget.Species == 487)
                            {
                                // Restore form + correct item for target format after conversion
                                convTarget.Form = (byte)template.Form;
                                if (template.Form == 1)
                                {
                                    // PK9 uses Griseous Core (2413), all earlier gens use Griseous Orb (112)
                                    convTarget.HeldItem = typeof(T) == typeof(PK9) ? 2413 : 112;
                                }
                                convTarget.Gender = 2; // genderless
                                convTarget.ClearNickname();
                                convTarget.RefreshChecksum();
                                pkm = convTarget;
                                result = "Regenerated";
                                LogUtil.LogInfo($"[Giratina] PB8→{typeof(T).Name} conversion: {convRes}, form={convTarget.Form}, item={convTarget.HeldItem}, shiny={convTarget.IsShiny}", "Helpers");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"[Giratina] Fallback exception: {ex.Message}", "Helpers");
                    }
                }
                if (pkm != null)
                {
                    var giraLa = new LegalityAnalysis(pkm);
                    LogUtil.LogInfo($"[Giratina] Final: Form={pkm.Form}, HeldItem={pkm.HeldItem}, Shiny={pkm.IsShiny}, Valid={giraLa.Valid}", "Helpers");
                }
            }
            // Shaymin-Sky: Only legal with Gracidea held in some games.
            // In BDSP (PB8), Gracidea is unreleased - fall back to Shaymin-Land.
            else if (template.Species == 492 && template.Form == 1)
            {
                if (typeof(T) == typeof(PB8))
                {
                    LogUtil.LogInfo($"[Shaymin-Sky] Not legal in BDSP (Gracidea unreleased), generating Shaymin-Land instead", "Helpers");
                    var landText = "Shaymin\nLevel: 100" + (set.Shiny ? "\nShiny: Yes" : "");
                    var landSet = new ShowdownSet(landText);
                    var landTemplate = AutoLegalityWrapper.GetTemplate(landSet);
                    pkm = sav.GetLegal(landTemplate, out result);
                }
                else
                {
                    LogUtil.LogInfo($"[Shaymin-Sky] Auto-attaching Gracidea (item 466)", "Helpers");
                    pkm = sav.GetLegal(template, out result);
                    if (pkm != null && pkm.HeldItem != 466)
                    {
                        pkm.HeldItem = 466;
                        pkm.RefreshChecksum();
                    }
                }
            }
            // Genesect: Requires a Drive matching its form (Burn=116, Chill=117, Douse=118, Shock=119)
            else if (template.Species == 649 && template.Form > 0)
            {
                int driveItem = template.Form switch
                {
                    1 => 117, // Douse Drive (Water)
                    2 => 118, // Shock Drive (Electric)
                    3 => 116, // Burn Drive (Fire)
                    4 => 119, // Chill Drive (Ice)
                    _ => 0
                };
                LogUtil.LogInfo($"[Genesect] Auto-attaching Drive item {driveItem} for form {template.Form}", "Helpers");
                pkm = sav.GetLegal(template, out result);
                if (pkm != null && driveItem > 0 && pkm.HeldItem != driveItem)
                {
                    pkm.HeldItem = driveItem;
                    pkm.RefreshChecksum();
                }
            }
            // Arceus: Requires the Plate matching its type (forms 1-17)
            else if (template.Species == 493 && template.Form > 0 && template.Form <= 17)
            {
                // Plate item IDs: Flame Plate=298, Splash Plate=299, ..., Pixie Plate=644
                int[] plateItems = { 0, 298, 299, 300, 301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 311, 312, 313, 644 };
                int plateItem = template.Form < plateItems.Length ? plateItems[template.Form] : 0;
                LogUtil.LogInfo($"[Arceus] Auto-attaching Plate item {plateItem} for form {template.Form}", "Helpers");
                pkm = sav.GetLegal(template, out result);
                if (pkm != null && plateItem > 0 && pkm.HeldItem != plateItem)
                {
                    pkm.HeldItem = (ushort)plateItem;
                    pkm.RefreshChecksum();
                }
            }
            // Silvally: Requires Memory matching its form (forms 1-17)
            else if (template.Species == 773 && template.Form > 0 && template.Form <= 17)
            {
                // Memory item IDs: Fighting Memory=904, Flying Memory=905, ..., Fairy Memory=920
                int memoryItem = 903 + template.Form;
                LogUtil.LogInfo($"[Silvally] Auto-attaching Memory item {memoryItem} for form {template.Form}", "Helpers");
                pkm = sav.GetLegal(template, out result);
                if (pkm != null && pkm.HeldItem != memoryItem)
                {
                    pkm.HeldItem = (ushort)memoryItem;
                    pkm.RefreshChecksum();
                }
            }
            // Zacian: Crowned is a battle-only form. Store as Hero (form 0) with Rusted Sword held.
            // The game auto-transforms to Crowned when entering battle.
            else if (template.Species == 888)
            {
                bool wantsCrowned = set.FormName != null && set.FormName.Contains("Crowned", StringComparison.OrdinalIgnoreCase);
                LogUtil.LogInfo($"[Zacian] FormName='{set.FormName}', wantsCrowned={wantsCrowned}", "Helpers");
                // Force template to Hero form (0) - Crowned form fails legality outside battle
                if (wantsCrowned)
                {
                    var heroText = "Zacian\nLevel: 100";
                    if (set.Shiny) heroText += "\nShiny: Yes";
                    var heroSet = new ShowdownSet(heroText);
                    var heroTemplate = AutoLegalityWrapper.GetTemplate(heroSet);
                    pkm = sav.GetLegal(heroTemplate, out result);
                    if (pkm != null)
                    {
                        pkm.HeldItem = 1103; // Rusted Sword - auto-transforms to Crowned in battle
                        pkm.RefreshChecksum();
                    }
                }
                else
                {
                    pkm = sav.GetLegal(template, out result);
                }
            }
            // Zamazenta: Same - store as Hero with Rusted Shield
            else if (template.Species == 889)
            {
                bool wantsCrowned = set.FormName != null && set.FormName.Contains("Crowned", StringComparison.OrdinalIgnoreCase);
                LogUtil.LogInfo($"[Zamazenta] FormName='{set.FormName}', wantsCrowned={wantsCrowned}", "Helpers");
                if (wantsCrowned)
                {
                    var heroText = "Zamazenta\nLevel: 100";
                    if (set.Shiny) heroText += "\nShiny: Yes";
                    var heroSet = new ShowdownSet(heroText);
                    var heroTemplate = AutoLegalityWrapper.GetTemplate(heroSet);
                    pkm = sav.GetLegal(heroTemplate, out result);
                    if (pkm != null)
                    {
                        pkm.HeldItem = 1104; // Rusted Shield - auto-transforms to Crowned in battle
                        pkm.RefreshChecksum();
                    }
                }
                else
                {
                    pkm = sav.GetLegal(template, out result);
                }
            }
            // Dialga-Origin: Requires Adamant Crystal (item 1777)
            else if (template.Species == 483 && template.Form == 1)
            {
                LogUtil.LogInfo($"[Dialga-Origin] Auto-attaching Adamant Crystal (1777)", "Helpers");
                pkm = sav.GetLegal(template, out result);
                if (pkm != null && pkm.HeldItem != 1777)
                {
                    pkm.HeldItem = 1777;
                    pkm.RefreshChecksum();
                }
            }
            // Palkia-Origin: Requires Lustrous Globe (item 1778)
            else if (template.Species == 484 && template.Form == 1)
            {
                LogUtil.LogInfo($"[Palkia-Origin] Auto-attaching Lustrous Globe (1778)", "Helpers");
                pkm = sav.GetLegal(template, out result);
                if (pkm != null && pkm.HeldItem != 1778)
                {
                    pkm.HeldItem = 1778;
                    pkm.RefreshChecksum();
                }
            }
            // Keldeo-Resolute: Requires Secret Sword move to maintain form
            else if (template.Species == 647 && template.Form == 1)
            {
                LogUtil.LogInfo($"[Keldeo-Resolute] Ensuring Secret Sword (move 548) is present", "Helpers");
                pkm = sav.GetLegal(template, out result);
                if (pkm != null)
                {
                    bool hasSecretSword = pkm.Move1 == 548 || pkm.Move2 == 548 || pkm.Move3 == 548 || pkm.Move4 == 548;
                    if (!hasSecretSword)
                    {
                        pkm.Move4 = 548;
                        pkm.RefreshChecksum();
                    }
                }
            }
            // Kyurem-Black/White: Fused forms require proper encounter handling
            // ALM handles these, but form 1 = White (with Reshiram), form 2 = Black (with Zekrom)
            else if (template.Species == 646 && template.Form > 0)
            {
                LogUtil.LogInfo($"[Kyurem] Fused form {template.Form} - letting ALM handle encounter", "Helpers");
                pkm = sav.GetLegal(template, out result);
            }
            // Hoopa-Unbound: Form 1 requires Hoopa to have used Hoopa's Ring
            // In game, requires Prison Bottle key item but not a held item
            else if (template.Species == 720 && template.Form == 1)
            {
                LogUtil.LogInfo($"[Hoopa-Unbound] Letting ALM generate unbound form", "Helpers");
                pkm = sav.GetLegal(template, out result);
            }
            // Urshifu Single Strike (form 0) / Rapid Strike (form 1) - different evolutions
            // ALM handles both forms natively, just let it through
            else if (template.Species == 892)
            {
                LogUtil.LogInfo($"[Urshifu] Form {template.Form} - letting ALM handle", "Helpers");
                pkm = sav.GetLegal(template, out result);
            }
            // Alcremie form workaround: ALM may fail to generate non-default Alcremie forms.
            // Generate as default (form 0), then fix the form afterwards.
            else if (template.Species == 869 && template.Form != 0)
            {
                LogUtil.LogInfo($"[Alcremie] Overriding template form {template.Form} to default (0) for generation, FormName='{set.FormName}'", "Alcremie");
                // Build a clean Alcremie set with default form, preserving shiny/level
                var alcremieText = "Alcremie";
                if (set.Shiny) alcremieText += "\nShiny: Yes";
                alcremieText += "\nLevel: 100";
                LogUtil.LogInfo($"[Alcremie] Using showdown text: {alcremieText.Replace("\n", " | ")}", "Alcremie");
                var defaultSet = new ShowdownSet(alcremieText);
                var defaultTemplate = AutoLegalityWrapper.GetTemplate(defaultSet);
                pkm = sav.GetLegal(defaultTemplate, out result);
            }
            else
            {
                // Use normal template for regular Pokémon
                pkm = sav.GetLegal(template, out result);

                // If ALM failed, try cross-game fallback (BDSP → target format)
                // Handles species like Celebi/Jirachi that may fail in certain games
                if ((pkm == null || result == "Failed" || !new LegalityAnalysis(pkm).Valid) && typeof(T) != typeof(PB8))
                {
                    LogUtil.LogInfo($"[CrossGame Fallback] Direct gen failed for {template.Species}, trying PB8 (BDSP)", "Helpers");
                    try
                    {
                        ITrainerInfo bdspSav = new SimpleTrainerInfo(GameVersion.BD)
                        { OT = sav.OT, TID16 = sav.TID16, SID16 = sav.SID16, Language = sav.Language };
                        var pb8 = bdspSav.GetLegal(template, out var pb8Result);
                        if (pb8 != null && pb8.Species == template.Species && new LegalityAnalysis(pb8).Valid)
                        {
                            var converted = EntityConverter.ConvertToType(pb8, typeof(T), out var convRes);
                            if (converted is T convTarget && convTarget.Species == template.Species)
                            {
                                convTarget.ClearNickname();
                                convTarget.RefreshChecksum();
                                pkm = convTarget;
                                result = "Regenerated";
                                LogUtil.LogInfo($"[CrossGame Fallback] PB8→{typeof(T).Name} succeeded for {template.Species}", "Helpers");
                            }
                        }
                    }
                    catch (Exception ex) { LogUtil.LogError($"[CrossGame Fallback] Exception: {ex.Message}", "Helpers"); }
                }
            }
        }

        if (pkm == null)
        {
            LogUtil.LogInfo($"[ALM] FAILED - pkm is null, result='{result}'", "Helpers");
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = "Set took too long to legalize.",
                ShowdownSet = set
            });
        }

        // Detailed ALM result logging
        var almLA = new LegalityAnalysis(pkm);
        LogUtil.LogInfo($"[ALM] result='{result}', valid={almLA.Valid}, species={pkm.Species}, speciesMatch={pkm.Species == template.Species}", "Helpers");
        if (!almLA.Valid)
            LogUtil.LogInfo($"[ALM] Legality report: {almLA.Report()}", "Helpers");

        // ============================================================================

        if (pkm.Species == 666)
        {
            LogUtil.LogInfo($"[Vivillon-Early] FormName='{set.FormName}', set.Form={set.Form}, template.Form={template.Form}, pkm.Form={pkm.Form}", "Vivillon");
        }

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

        var spec = GameInfo.Strings.Species[template.Species];

        // Apply standard item logic only for non-eggs
        if (!isEgg)
        {
            ApplyStandardItemLogic(pkm);
        }

        // ============================================================================
        // MAX LAIR POKEMON MOVE FIX
        // ============================================================================
        // Fix moves for Max Lair Pokemon that ALM generates without moves.
        // Do NOT force 6IV here — it breaks PID-IV seed correlation for Max Lair.
        // ============================================================================
        if (pkm is PK8 pk8 && !isEgg)
        {
            const int MaxLairLocationID = 244;
            bool hasNoMoves = pk8.Move1 == 0 && pk8.Move2 == 0 && pk8.Move3 == 0 && pk8.Move4 == 0;
            if (hasNoMoves && pk8.MetLocation == MaxLairLocationID)
            {
                pk8.SetSuggestedMoves();
                pk8.HealPP();
            }

            // Clear Hyper Training flags if IVs are already 31 (prevents "Can't Hyper Train perfect IVs" error)
            if (pk8.IV_HP == 31) pk8.HT_HP = false;
            if (pk8.IV_ATK == 31) pk8.HT_ATK = false;
            if (pk8.IV_DEF == 31) pk8.HT_DEF = false;
            if (pk8.IV_SPA == 31) pk8.HT_SPA = false;
            if (pk8.IV_SPD == 31) pk8.HT_SPD = false;
            if (pk8.IV_SPE == 31) pk8.HT_SPE = false;

            pk8.RefreshChecksum();
        }
        // ============================================================================
        // END OF MAX LAIR MOVE FIX
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

        var la = new LegalityAnalysis(pkm);
        if (pkm.Species == 666)
            LogUtil.LogInfo($"[Vivillon-PreCheck] la.Valid={la.Valid}, result='{result}', pkm is T={pkm is T}", "Vivillon");

        // ============================================================================
        // MAX LAIR FALLBACK
        // ============================================================================
        // If a PK8 is still invalid, re-run ALM with a fresh template that forces
        // MetLocation=244 (Max Lair). Many SWSH legendaries (Tapu Koko, etc.) and
        // Ultra Beasts are ONLY obtainable via Dynamax Adventures. ALM sometimes
        // picks the wrong encounter, producing invalid PID/IV correlation.
        // ============================================================================
        // Skip MaxLair fallback for manually-built GO-origin Pokemon
        bool skipMaxLair = pkm is PK8 goBuilt && goBuilt.MetLocation == 30012 &&
            (goBuilt.Species == 151 || goBuilt.Species == 385 || goBuilt.Species == 808 || goBuilt.Species == 809);
        if (!la.Valid && pkm is PK8 pk8Retry && !skipMaxLair)
        {
            // Try 1: If not already at Max Lair, just fix MetLocation + moves
            if (pk8Retry.MetLocation != 244)
            {
                var pk8RetryClone = (PK8)pk8Retry.Clone();
                pk8RetryClone.MetLocation = 244;
                pk8RetryClone.SetSuggestedMoves();
                pk8RetryClone.HealPP();
                // Clear hyper training on perfect IVs
                if (pk8RetryClone.IV_HP == 31) pk8RetryClone.HT_HP = false;
                if (pk8RetryClone.IV_ATK == 31) pk8RetryClone.HT_ATK = false;
                if (pk8RetryClone.IV_DEF == 31) pk8RetryClone.HT_DEF = false;
                if (pk8RetryClone.IV_SPA == 31) pk8RetryClone.HT_SPA = false;
                if (pk8RetryClone.IV_SPD == 31) pk8RetryClone.HT_SPD = false;
                if (pk8RetryClone.IV_SPE == 31) pk8RetryClone.HT_SPE = false;
                pk8RetryClone.RefreshChecksum();
                var laRetry = new LegalityAnalysis(pk8RetryClone);
                if (laRetry.Valid)
                {
                    pkm = pk8RetryClone;
                    la = laRetry;
                }
            }

            // Try 2: If still invalid, re-generate from scratch via ALM
            if (!la.Valid)
            {
                LogUtil.LogInfo($"[MaxLair Fallback] Re-generating species={pkm.Species} via ALM with fresh template", "Helpers");
                var retryPkm = sav.GetLegal(template, out var retryResult);
                if (retryResult == "Regenerated" && retryPkm is PK8 pk8Fresh)
                {
                    // If ALM still picked wrong location, force Max Lair
                    if (pk8Fresh.MetLocation != 244)
                    {
                        pk8Fresh.MetLocation = 244;
                        pk8Fresh.SetSuggestedMoves();
                        pk8Fresh.HealPP();
                    }
                    // Clear hyper training on perfect IVs
                    if (pk8Fresh.IV_HP == 31) pk8Fresh.HT_HP = false;
                    if (pk8Fresh.IV_ATK == 31) pk8Fresh.HT_ATK = false;
                    if (pk8Fresh.IV_DEF == 31) pk8Fresh.HT_DEF = false;
                    if (pk8Fresh.IV_SPA == 31) pk8Fresh.HT_SPA = false;
                    if (pk8Fresh.IV_SPD == 31) pk8Fresh.HT_SPD = false;
                    if (pk8Fresh.IV_SPE == 31) pk8Fresh.HT_SPE = false;
                    pk8Fresh.RefreshChecksum();
                    var laFresh = new LegalityAnalysis(pk8Fresh);
                    LogUtil.LogInfo($"[MaxLair Fallback] Retry result={retryResult} valid={laFresh.Valid} loc={pk8Fresh.MetLocation}", "Helpers");
                    if (laFresh.Valid)
                    {
                        pkm = pk8Fresh;
                        la = laFresh;
                    }
                }
            }
        }
        // ============================================================================
        // END OF MAX LAIR FALLBACK
        // ============================================================================

        // ============================================================================
        // BALL AUTO-CORRECT
        // ============================================================================
        // If legality fails ONLY because of an invalid ball, auto-correct to a legal ball.
        // This makes trades smoother — users don't need to know which ball is required for
        // event Pokemon (Cherish), Ultra Beasts (Beast), Apricorn-mons, etc.
        // ============================================================================
        if (!la.Valid && pkm != null)
        {
            bool hasBallError = false;
            foreach (var check in la.Results)
            {
                if (!check.Valid && check.Identifier == CheckIdentifier.Ball)
                {
                    hasBallError = true;
                    break;
                }
            }
            if (hasBallError)
            {
                try
                {
                    var ballFixed = (PKM)pkm.Clone();
                    BallApplicator.ApplyBallLegalRandom(ballFixed);
                    ballFixed.RefreshChecksum();
                    var laBallFix = new LegalityAnalysis(ballFixed);
                    if (laBallFix.Valid)
                    {
                        LogUtil.LogInfo($"[Ball AutoFix] Corrected ball for {(Species)pkm.Species}: {(Ball)pkm.Ball} -> {(Ball)ballFixed.Ball}", "Helpers");
                        pkm = ballFixed;
                        la = laBallFix;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogInfo($"[Ball AutoFix] Error: {ex.Message}", "Helpers");
                }
            }
        }
        // ============================================================================
        // END OF BALL AUTO-CORRECT
        // ============================================================================

        // ============================================================================
        // SCALE / HEIGHT / WEIGHT AUTO-CORRECT
        // ============================================================================
        // Event Pokemon and static encounters often have fixed Scale (usually 128 = medium).
        // If legality fails due to scale/height/weight mismatch, try common legal values.
        // ============================================================================
        if (!la.Valid && pkm is IScaledSize ss)
        {
            bool hasScaleError = false;
            foreach (var check in la.Results)
            {
                if (!check.Valid && (check.Identifier == CheckIdentifier.Encounter || check.Identifier == CheckIdentifier.Misc))
                {
                    var code = check.Result.ToString();
                    if (code.Contains("Scale", StringComparison.OrdinalIgnoreCase) ||
                        code.Contains("Height", StringComparison.OrdinalIgnoreCase) ||
                        code.Contains("Weight", StringComparison.OrdinalIgnoreCase))
                    {
                        hasScaleError = true;
                        break;
                    }
                }
            }
            if (hasScaleError)
            {
                // Try common legal scale values: 128 (medium), then 0, then matching height/weight
                byte[] scaleValues = { 128, 0 };
                foreach (var scale in scaleValues)
                {
                    try
                    {
                        var scaleFixed = (PKM)pkm.Clone();
                        if (scaleFixed is IScaledSize ss2)
                        {
                            ss2.HeightScalar = scale;
                            ss2.WeightScalar = scale;
                        }
                        if (scaleFixed is IScaledSize3 ss3)
                            ss3.Scale = scale;
                        scaleFixed.RefreshChecksum();
                        var laScaleFix = new LegalityAnalysis(scaleFixed);
                        if (laScaleFix.Valid)
                        {
                            LogUtil.LogInfo($"[Scale AutoFix] Corrected scale to {scale} for {(Species)pkm.Species}", "Helpers");
                            pkm = scaleFixed;
                            la = laScaleFix;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogInfo($"[Scale AutoFix] Error: {ex.Message}", "Helpers");
                    }
                }
            }
        }
        // ============================================================================
        // END OF SCALE AUTO-CORRECT
        // ============================================================================

        // Skip legality gate for Melmetal/Meltan/Mew/Jirachi in SWSH - manually built as GO-origin
        bool skipLegalityGate = (typeof(T) == typeof(PK8) || typeof(T) == typeof(PB8)) && (
            template.Species == 808 || template.Species == 809 ||
            template.Species == 251 || template.Species == 385 || template.Species == 386 ||
            ((template.Species == 151) && set.Shiny));
        if (pkm is not T pk || (!la.Valid && !skipLegalityGate))
        {
            var reason = GetFailureReason(result, spec);
            var hint = result == "Failed" ? GetLegalizationHint(template, sav, pkm, spec) : null;

            // Extract specific legality failures to show the user what was wrong
            if (pkm != null && !la.Valid)
            {
                try
                {
                    var report = la.Report(false);
                    if (!string.IsNullOrWhiteSpace(report))
                    {
                        // Extract only Invalid lines from the report
                        var invalidLines = report
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Where(l => l.Contains("Invalid:", StringComparison.OrdinalIgnoreCase))
                            .Select(l => l.Replace("Invalid:", "").Trim())
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Take(4)
                            .ToList();
                        if (invalidLines.Count > 0)
                        {
                            var detailedHint = "• " + string.Join("\n• ", invalidLines);
                            hint = string.IsNullOrWhiteSpace(hint) ? detailedHint : $"{hint}\n\n**Issues:**\n{detailedHint}";
                        }
                    }
                }
                catch { /* fall through with default hint */ }
            }

            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = reason,
                LegalizationHint = hint,
                ShowdownSet = set
            });
        }

        // ============================================================================
        // POST-LEGALIZATION FIXUPS
        // Re-apply held item and nature from the original Showdown set
        // ALM may strip these for event Pokemon, but they're legal modifications
        // ============================================================================

        // Shiny fix: ALM sometimes drops shiny when nicknames are present
        // If user requested shiny but the Pokemon isn't shiny, force it
        LogUtil.LogInfo($"[Shiny Debug] set.Shiny={set.Shiny}, pk.IsShiny={pk.IsShiny}, Species={(Species)pk.Species}, Nickname='{set.Nickname}'", "Helpers");
        if (set.Shiny && !pk.IsShiny)
        {
            LogUtil.LogInfo($"[Shiny Fix] User requested shiny but ALM generated non-shiny. Forcing shiny for {(Species)pk.Species}", "Helpers");
            pk.SetIsShiny(true);
            pk.RefreshChecksum();

            // Verify it's still legal after setting shiny
            var shinyCheck = new LegalityAnalysis(pk);
            if (!shinyCheck.Valid)
            {
                // Try square shiny instead
                pk.SetShiny(Shiny.AlwaysSquare);
                pk.RefreshChecksum();
                var squareCheck = new LegalityAnalysis(pk);
                if (!squareCheck.Valid)
                {
                    // Try star shiny
                    pk.SetShiny(Shiny.AlwaysStar);
                    pk.RefreshChecksum();
                }
            }
        }

        // Auto-fix invalid items: if user requested an item that ALM rejected,
        // give them Rare Candy (item ID 50) as a universal fallback
        // Rare Candy is legal to hold in every game and useful (levels up the Pokemon)
        if (set.HeldItem > 0 && pk.HeldItem == 0)
        {
            pk.HeldItem = set.HeldItem;
            pk.RefreshChecksum();
            var itemCheck = new LegalityAnalysis(pk);
            if (!itemCheck.Valid)
            {
                LogUtil.LogInfo($"[AutoFix] Item {set.HeldItem} invalid for {typeof(T).Name}, replacing with Rare Candy", "Helpers");
                pk.HeldItem = 50; // Rare Candy
                pk.RefreshChecksum();
                var fbCheck = new LegalityAnalysis(pk);
                if (!fbCheck.Valid)
                {
                    // Even Rare Candy failed, clear the item
                    pk.HeldItem = 0;
                    pk.RefreshChecksum();
                }
            }
        }
        else if (set.HeldItem > 0 && pk.HeldItem != set.HeldItem)
        {
            pk.HeldItem = set.HeldItem;
            pk.RefreshChecksum();
        }

        // Nature is handled by ALM during generation — do not override

        // Re-apply EVs if set specified them
        if (set.EVs.Any(ev => ev > 0))
        {
            pk.EV_HP = set.EVs[0];
            pk.EV_ATK = set.EVs[1];
            pk.EV_DEF = set.EVs[2];
            pk.EV_SPE = set.EVs[3];
            pk.EV_SPA = set.EVs[4];
            pk.EV_SPD = set.EVs[5];
            pk.RefreshChecksum();
        }

        // Final preparation
        PrepareForTrade(pk, set, finalLanguage);

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
    
        // ============================================================================
        // VIVILLON FORM FIX — Force requested form after all legality checks pass
        // ============================================================================
        if (pk.Species == 666 && !string.IsNullOrEmpty(set.FormName))
        {
            // Resolve form name to form number
            var vivillonForms = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
            {
                {"Icy Snow", 0}, {"Icy-Snow", 0}, {"IcySnow", 0},
                {"Polar", 1}, {"Tundra", 2}, {"Continental", 3},
                {"Garden", 4}, {"Elegant", 5}, {"Meadow", 6}, {"Modern", 7},
                {"Marine", 8}, {"Archipelago", 9},
                {"High Plains", 10}, {"High-Plains", 10}, {"HighPlains", 10},
                {"Sandstorm", 11}, {"River", 12}, {"Monsoon", 13}, {"Savanna", 14},
                {"Sun", 15}, {"Ocean", 16}, {"Jungle", 17}, {"Fancy", 18},
                {"Pokeball", 19}, {"Poke Ball", 19}, {"Poke-Ball", 19}
            };

            var formName = set.FormName?.Trim() ?? "";
            LogUtil.LogInfo($"[Vivillon] set.FormName='{set.FormName}', trimmed='{formName}', pk.Form={pk.Form}", "Vivillon");
            if (vivillonForms.TryGetValue(formName, out var targetForm) && targetForm != pk.Form)
            {
                // In Z-A only Meadow (6), Garden (4), and Marine (8) are legal
                if (typeof(T) == typeof(PA9) && targetForm != 4 && targetForm != 6 && targetForm != 8)
                {
                    LogUtil.LogInfo($"[Vivillon] Blocked illegal Z-A form {targetForm} ({set.FormName})", "Vivillon");
                    return Task.FromResult(new ProcessedPokemonResult<T>
                    {
                        Error = $"Vivillon-{set.FormName} is **not available** in Legends Z-A. Only **Meadow**, **Marine**, and **Garden** patterns exist in this game.",
                        ShowdownSet = set
                    });
                }

                LogUtil.LogInfo($"[Vivillon] Forcing form from {pk.Form} to {targetForm} ({set.FormName})", "Vivillon");
                pk.Form = targetForm;
                pk.ClearNickname();
                pk.RefreshChecksum();
            }
        }
        // ============================================================================

        // ============================================================================
        // ALCREMIE FORM FIX — Force requested form after all legality checks pass
        // ============================================================================
        if (pk.Species == 869 && set.Form != pk.Form)
        {
            LogUtil.LogInfo($"[Alcremie] Forcing form from {pk.Form} to {set.Form} ({set.FormName})", "Alcremie");
            pk.Form = (byte)set.Form;
            pk.ClearNickname();
            pk.RefreshChecksum();
        }
        // ============================================================================

        // For SWSH (PK8), GO Pokemon can have AutoOT applied, so don't mark them as non-native
        la = new LegalityAnalysis(pk);
        var isNonNative = la.EncounterOriginal.Context != pk.Context || (pk.GO && pk is not PK8);

        return Task.FromResult(new ProcessedPokemonResult<T>
        {
            Pokemon = pk,
            ShowdownSet = set,
            LgCode = lgcode,
            IsNonNative = isNonNative
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

    public static void PrepareForTrade(T pk, ShowdownSet set, byte finalLanguage)
    {
        LogUtil.LogInfo($"[PrepareForTrade] Species={pk.Species}, finalLanguage={finalLanguage}, currentLanguage={pk.Language}", "Helpers");

        // Only set EggMetDate for hatched Pokemon, not for unhatched eggs
        if (pk.WasEgg && !pk.IsEgg)
            pk.EggMetDate = pk.MetDate;

        // Validate language is supported for this game version
        // SpanishL (11) isn't supported in some games, fall back to Spanish (7)
        var validatedLanguage = ValidateLanguageForGame(pk, finalLanguage);
        pk.Language = validatedLanguage;
        LogUtil.LogInfo($"[PrepareForTrade] Set Language to {validatedLanguage}", "Helpers");

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
            _ = pk.ClearNickname();

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

    public static string GetLegalizationHint(IBattleTemplate template, ITrainerInfo sav, PKM pkm, string speciesName)
    {
        var hint = AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm);
        if (hint.Contains("Requested shiny value (ShinyType."))
        {
            hint = $"{speciesName} **cannot** be shiny. Please try again.";
        }
        return hint;
    }

    // Generate a 100% legal shiny PKM by iterating PKHeX's encounter database.
    // Used for shiny-locked species (Mew, Jirachi) where ALM's default path fails.
    private static PKM? TryGenerateLegalShiny<TP>(ushort species, ITrainerInfo sav, ShowdownSet set, out string result)
        where TP : PKM, new()
    {
        LogUtil.LogInfo($"[LegalShiny] Searching for legit shiny encounter for species={species}, T={typeof(TP).Name}", "Helpers");
        var blank = new TP { Species = species };

        try
        {
            // Iterate encounter data for this species, try to make each one shiny and legal
            var encounters = EncounterMovesetGenerator.GenerateEncounters(blank, System.ReadOnlyMemory<ushort>.Empty);
            var criteria = EncounterCriteria.Unrestricted with { Shiny = Shiny.Always };
            int tried = 0;
            foreach (var enc in encounters)
            {
                tried++;
                if (enc.Shiny == Shiny.Never) continue;
                PKM? candidate;
                try { candidate = enc.ConvertToPKM(sav, criteria); }
                catch { continue; }
                if (candidate is not TP typed) continue;
                try
                {
                    if (!typed.IsShiny) typed.SetShiny();
                    typed.RefreshChecksum();
                    var la = new LegalityAnalysis(typed);
                    if (!la.Valid) continue;

                    // Apply user customizations that don't break legality
                    if (set.Nature != Nature.Random)
                    {
                        typed.Nature = set.Nature;
                        typed.StatNature = set.Nature;
                    }
                    if (set.EVs != null && set.EVs.Any(e => e > 0))
                    {
                        typed.EV_HP = set.EVs[0]; typed.EV_ATK = set.EVs[1]; typed.EV_DEF = set.EVs[2];
                        typed.EV_SPE = set.EVs[3]; typed.EV_SPA = set.EVs[4]; typed.EV_SPD = set.EVs[5];
                    }
                    if (set.HeldItem > 0) typed.HeldItem = set.HeldItem;
                    typed.CurrentLevel = 100;
                    typed.HealPP();
                    typed.RefreshChecksum();
                    var laFinal = new LegalityAnalysis(typed);
                    if (laFinal.Valid)
                    {
                        result = "Regenerated";
                        LogUtil.LogInfo($"[LegalShiny] Found legal shiny after {tried} candidate(s)", "Helpers");
                        return typed;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogInfo($"[LegalShiny] Candidate threw: {ex.Message}", "Helpers");
                }
            }
            LogUtil.LogError($"[LegalShiny] No legal shiny encounter found after trying {tried} candidates for species {species}", "Helpers");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"[LegalShiny] Exception during encounter generation: {ex.Message}", "Helpers");
        }

        result = "Failed";
        return null;
    }

    public static async Task SendTradeErrorEmbedAsync(SocketCommandContext context, ProcessedPokemonResult<T> result)
    {
        var spec = result.ShowdownSet != null && result.ShowdownSet.Species > 0
            ? GameInfo.Strings.Species[result.ShowdownSet.Species]
            : "Unknown";

        var embedBuilder = new EmbedBuilder()
            .WithTitle("Trade Creation Failed.")
            .WithColor(Color.Red)
            .AddField("Status", $"Failed to create {spec}.")
            .AddField("Reason", result.Error ?? "Unknown error");

        if (!string.IsNullOrEmpty(result.LegalizationHint))
        {
            _ = embedBuilder.AddField("Hint", result.LegalizationHint);
        }

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

        // Skip legality check for Vivillon/Alcremie — form is forced after generation.
        // Also skip Mew/Jirachi shiny and Melmetal/Meltan in PK8 — GO-origin manual builds.
        bool isGoBuiltPk8 = (pk is PK8 || pk is PB8) && (
            pk.Species == 808 || pk.Species == 809 ||
            pk.Species == 251 || pk.Species == 385 || pk.Species == 386 ||
            ((pk.Species == 151) && pk.IsShiny));
        if (!la.Valid && pk!.Species != 666 && pk!.Species != 869 && !isGoBuiltPk8)
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

        // Handle past gen file requests
        if (!la.Valid)
        {
            if (la.Results.Any(m => m.Identifier is CheckIdentifier.Memory))
            {
                var clone = (T)pk!.Clone();
                clone.HandlingTrainerName = pk.OriginalTrainerName;
                clone.HandlingTrainerGender = pk.OriginalTrainerGender;
                if (clone is PK8 or PA8 or PB8 or PK9)
                    ((dynamic)clone).HandlingTrainerLanguage = (byte)pk.Language;
                clone.CurrentHandler = 1;
                la = new LegalityAnalysis(clone);
                if (la.Valid) pk = clone;
            }
        }

        await QueueHelper<T>.AddToQueueAsync(context, code, trainerName, sig, pk!, PokeRoutineType.LinkTrade,
            tradeType, usr, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg,
            lgcode: lgcode, ignoreAutoOT: ignoreAutoOT, setEdited: setEdited, isNonNative: isNonNative).ConfigureAwait(false);
    }
}
