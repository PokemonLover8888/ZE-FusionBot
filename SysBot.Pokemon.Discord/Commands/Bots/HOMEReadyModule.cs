using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace SysBot.Pokemon.Discord.Modules
{
    public class HOMEReadyModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private static T? GetRequest(Download<PKM> dl)
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

        private string HOMEFolder => SysCord<T>.Runner.Config.Folder.HOMEReadyPKMFolder;

        private string Prefix => SysCord<T>.Runner.Config.Discord.CommandPrefix;

        // ============================================================================
        //  INSTRUCTIONS
        // ============================================================================
        [Command("homeready")]
        [Alias("hr")]
        [Summary("Displays instructions on how to use the HOME-Ready module.")]
        [RequireQueueRole(nameof(DiscordManager.RolesClone))]
        private async Task HomeReadyInstructionsAsync()
        {
            if (string.IsNullOrWhiteSpace(HOMEFolder))
            {
                await ReplyAsync("This bot does not have the HOME-Ready module configured.").ConfigureAwait(false);
                return;
            }

            // Count what's available so members see the perk's scale
            int totalFiles = 0;
            try { totalFiles = Directory.EnumerateFiles(HOMEFolder).Count(); } catch { }

            var embed = new EmbedBuilder()
                .WithAuthor(a => a
                    .WithName("PKM Universe — HOME-Ready Module")
                    .WithIconUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png"))
                .WithTitle("✨ Request ANY HOME-Tracked Event Pokémon ✨")
                .WithDescription(
                    $"Browse a curated library of **{totalFiles:N0}** event Pokémon with real HOME trackers — " +
                    "legendaries, mythicals, raid distributions, anniversary events, every regional Mystery Gift. " +
                    "Pick by index and the bot ships it straight to your save.\n\n" +
                    "*Note: each event Pokémon has a target game (SwSh / SV / BDSP / LGPE). " +
                    "The bot tells you which one in the list — use the matching bot to trade for it.*")
                .WithColor(new Color(0xFF, 0xD7, 0x00)) // PKM Universe gold
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png")
                .AddField($"🔍  Search — `{Prefix}ll <Pokémon>`",
                    $"List every available file for a species.\n*Example: `{Prefix}ll Mewtwo`*\n_Alias: `{Prefix}hrl`_",
                    inline: false)
                .AddField($"📄  Pages — `{Prefix}ll <page> <Pokémon>`",
                    $"Jump to a specific results page when there are lots of variants.\n*Example: `{Prefix}ll 3 Charmander`*",
                    inline: false)
                .AddField($"🎁  Request — `{Prefix}lr <number>`",
                    $"Queue the file shown at that index from your most recent list.\n*Example: `{Prefix}lr 682`*\n_Alias: `{Prefix}hrr`_",
                    inline: false)
                .WithFooter($"Tip: list first, then request by index. Auto-deletes in 90s.",
                    "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/exclamation.gif")
                .WithCurrentTimestamp();

            var msg = await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);

            _ = Task.Run(async () =>
            {
                await Task.Delay(90_000);
                try { await msg.DeleteAsync(); } catch { }
            });
        }

        // ============================================================================
        //  REQUEST
        // ============================================================================
        [Command("homereadyrequest")]
        [Alias("hrr", "lr")]
        [Summary("Downloads a HOME-ready PKM and queues it for trade. `lr` short alias = 'legendary request'.")]
        [RequireQueueRole(nameof(DiscordManager.RolesClone))]
        private async Task HOMEReadyRequestAsync(int index)
        {
            if (string.IsNullOrWhiteSpace(HOMEFolder))
            {
                await ReplyAsync("This bot does not have the HOME-Ready module configured.").ConfigureAwait(false);
                return;
            }

            var userID = Context.User.Id;
            if (SysCord<T>.Runner.Hub.Queues.Info.IsUserInQueue(userID))
            {
                await ReplyAsync("You're already in a trade queue. Finish that one first.").ConfigureAwait(false);
                return;
            }

            try
            {
                var files = Directory.GetFiles(HOMEFolder)
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                {
                    await ReplyAsync("No HOME-ready PKM files found.").ConfigureAwait(false);
                    return;
                }

                if (index < 1 || index > files.Count)
                {
                    await ReplyAsync("Invalid entry number. Choose a valid file.").ConfigureAwait(false);
                    return;
                }

                var filePath = files[index - 1];
                var data = await File.ReadAllBytesAsync(filePath);
                var entity = EntityFormat.GetFromBytes(data);

                if (entity == null)
                {
                    await ReplyAsync("Could not convert the file to a valid PKM.").ConfigureAwait(false);
                    return;
                }

                var download = new Download<PKM>
                {
                    Data = entity,
                    Success = true
                };

                var pk = GetRequest(download);
                if (pk == null && entity is PKM rawPkm)
                {
                    pk = EntityConverter.ConvertToType(rawPkm, typeof(T), out _) as T;
                }

                if (pk == null)
                {
                    await ReplyAsync("Failed to convert the HOME-ready file to your trade format.").ConfigureAwait(false);
                    return;
                }

                var code = SysCord<T>.Runner.Hub.Queues.Info.GetRandomTradeCode(userID);
                var lgcode = SysCord<T>.Runner.Hub.Queues.Info.GetRandomLGTradeCode();
                var sig = Context.User.GetFavor();

                await ReplyAsync($"**HOME-Ready Pokémon added to the queue.**").ConfigureAwait(false);

                await Helpers<T>.AddTradeToQueueAsync(
                    context: Context,
                    code: code,
                    trainerName: Context.User.Username,
                    pk: pk,
                    sig: sig,
                    usr: Context.User,
                    isBatchTrade: false,
                    batchTradeNumber: 1,
                    totalBatchTrades: 1,
                    isHiddenTrade: false,
                    isMysteryEgg: false,
                    lgcode: lgcode ?? SysCord<T>.Runner.Hub.Queues.Info.GetRandomLGTradeCode(),
                    tradeType: PokeTradeType.Specific,
                    ignoreAutoOT: false,
                    setEdited: false,
                    isNonNative: false
                );
            }
            catch (Exception ex)
            {
                await ReplyAsync($"**Error:** {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (Context.Message is IUserMessage msg)
                        await msg.DeleteAsync().ConfigureAwait(false);
                }
                catch { }
            }
        }


        // ============================================================================
        //  LIST
        // ============================================================================
        [Command("homereadylist")]
        [Alias("hrl", "ll")]
        [Summary("Lists available HOME-Ready files with filtering + pagination. `ll` short alias = 'legendary list'.")]
        [RequireQueueRole(nameof(DiscordManager.RolesClone))]
        private async Task HOMEListAsync([Remainder] string args = "")
        {
            if (string.IsNullOrWhiteSpace(HOMEFolder))
            {
                await ReplyAsync("This bot does not have the HOME-Ready module configured.");
                return;
            }

            const int itemsPerPage = 10;

            var files = Directory.GetFiles(HOMEFolder)
                .Select(Path.GetFileName)
                .OrderBy(x => x)
                .ToList();

            if (files.Count == 0)
            {
                await ReplyAsync("No HOME-ready files found.");
                return;
            }

            // Parse filter + page. Accept both orderings:
            //   `ll Mewtwo`        → filter "Mewtwo", page 1
            //   `ll 3 Mewtwo`      → page 3, filter "Mewtwo"  (Sec's documented form)
            //   `ll Mewtwo 3`      → filter "Mewtwo", page 3  (legacy)
            //   `ll 3`             → page 3, no filter
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string filter = "";
            int page = 1;

            if (parts.Length > 0)
            {
                if (parts.Length >= 2 && int.TryParse(parts.First(), out int leadingPage))
                {
                    page = leadingPage;
                    filter = string.Join(" ", parts.Skip(1));
                }
                else if (parts.Length >= 2 && int.TryParse(parts.Last(), out int trailingPage))
                {
                    page = trailingPage;
                    filter = string.Join(" ", parts.Take(parts.Length - 1));
                }
                else if (parts.Length == 1 && int.TryParse(parts[0], out int solePage))
                {
                    page = solePage;
                }
                else
                {
                    filter = string.Join(" ", parts);
                }
            }

            var filtered = files
                .Where(f => string.IsNullOrWhiteSpace(filter) ||
                            f.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filtered.Count == 0)
            {
                await ReplyAsync($"No HOME-ready files found for '{filter}'.");
                return;
            }

            var pageCount = (int)Math.Ceiling(filtered.Count / (double)itemsPerPage);
            page = Math.Clamp(page, 1, pageCount);

            var pageItems = filtered
                .Skip((page - 1) * itemsPerPage)
                .Take(itemsPerPage)
                .ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"HOME-Ready Files — '{filter}'")
                .WithDescription($"Page **{page}** of **{pageCount}**")
                .WithColor(Color.Blue);

            // Map file extensions to game names
            var extensionToGame = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                 { ".pb7", "LGPE" },
                 { ".pk8", "SWSH" },
                 { ".pb8", "BDSP" },
                 { ".pa8", "PLA" },
                 { ".pa9", "PLZA" },
                 { ".pk9", "SV" }
            };

            // Parse event metadata out of filenames like:
            //   Mewtwo--FEB2012-F0B42724-ENG.pk8        →  event "FEB2012",  lang "ENG"
            //   Mewtwo--Giovanni-01E338A7-CHS.pb7        →  event "Giovanni", lang "CHS"
            //   Pikachu--Movie21-...-ESP.pk8             →  event "Movie21",  lang "ESP"
            static (string species, string evt, string lang) ParseMeta(string filename)
            {
                var nameNoExt = Path.GetFileNameWithoutExtension(filename);
                var dashSplit = nameNoExt.Split("--", 2);
                if (dashSplit.Length < 2) return (nameNoExt, "", "");
                var species = dashSplit[0];
                var meta = dashSplit[1];
                var parts = meta.Split('-');
                if (parts.Length == 0) return (species, "", "");
                var evt = parts[0];
                var lang = (parts.Length >= 3 && parts[^1].Length == 3 && parts[^1].All(char.IsLetter))
                    ? parts[^1].ToUpperInvariant()
                    : "";
                return (species, evt, lang);
            }

            foreach (var item in pageItems)
            {
                var index = files.IndexOf(item) + 1;
                var ext = Path.GetExtension(item)?.Trim().ToUpperInvariant() ?? "";
                string game = extensionToGame.TryGetValue(ext, out var g) ? g : "Unknown";
                var (species, evt, lang) = ParseMeta(item);

                // Build a compact, human-readable title:
                //   "1. Mewtwo • FEB2012 • ENG • SWSH"
                var titleBits = new List<string> { $"{index}. {species}" };
                if (!string.IsNullOrEmpty(evt))  titleBits.Add(evt);
                if (!string.IsNullOrEmpty(lang)) titleBits.Add(lang);
                titleBits.Add(game);
                var title = string.Join(" • ", titleBits);

                embed.AddField(
                    title,
                    $"`{Prefix}lr {index}` — request   ·   `{Prefix}hrv {index}` — view   ·   `{Prefix}hrd {index}` — download"
                );
            }

            var embedMsg = await ReplyAsync(embed: embed.Build());

            await Task.Delay(20_000);

            try
            {
                await embedMsg.DeleteAsync();
            }
            catch { }
        }

        // ============================================================================
        //  VIEW
        // ============================================================================
        [Command("homereadyview")]
        [Alias("hrv")]
        [Summary("Views a HOME-ready PKM in Showdown format before downloading.")]
        [RequireQueueRole(nameof(DiscordManager.RolesClone))]
        private async Task HOMEReadyViewAsync(int index)
        {
            if (string.IsNullOrWhiteSpace(HOMEFolder))
            {
                await ReplyAsync("This bot does not have the HOME-Ready module configured.").ConfigureAwait(false);
                return;
            }

            try
            {
                var files = Directory.GetFiles(HOMEFolder)
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                {
                    await ReplyAsync("No HOME-ready PKM files found.").ConfigureAwait(false);
                    return;
                }

                if (index < 1 || index > files.Count)
                {
                    await ReplyAsync("Invalid entry number. Choose a valid file.");
                    return;
                }

                var filePath = files[index - 1];
                var raw = await File.ReadAllBytesAsync(filePath);

                var entity = EntityFormat.GetFromBytes(raw);
                if (entity == null)
                {
                    await ReplyAsync("Could not read the PKM file.").ConfigureAwait(false);
                    return;
                }

                // Convert to correct PKM type for this bot
                PKM? typed = entity as T
                    ?? EntityConverter.ConvertToType(entity, typeof(T), out _) as T;

                if (typed == null)
                {
                    await ReplyAsync("File loaded, but could not convert to your game generation.").ConfigureAwait(false);
                    return;
                }

                // Generate showdown text
                string showdown = ShowdownParsing.GetShowdownText(typed);

                // ============================
                // METADATA
                // ============================

                string otName = typed.OriginalTrainerName;
                string tid = typed.TrainerTID7.ToString();

                string versionName = GameInfo.GetVersionName(typed.Version);

                string metDate = typed.MetDate?.ToString("yyyy-MM-dd") ?? "Unknown";

                string metLocStr = GameInfo.Strings.GetLocationName(
                    isEggLocation: false,
                    location: typed.MetLocation,
                    format: typed.Format,
                    generation: typed.Generation,
                    version: typed.Version
                );

                string game = versionName;
                ulong homeTracker = GetHomeTrackerSafe(typed);
                string homeTrackerStr = homeTracker == 0
                    ? $"N/A - Just trade with a **{game}** bot."
                    : $"{homeTracker:X16}";

                string details =
                $@"**Additional Details**
                • OT: {otName}
                • TID: {tid}
                • Game: {versionName}
                • Met Location: {typed.MetLocation} ({metLocStr})
                • Met Date: {metDate}
                • Tracker: {homeTrackerStr}";


                string finalText =
                $"```text\n{showdown}\n```\n{details}";

                var embed = new EmbedBuilder()
                    .WithTitle($"Viewing HOME-Ready Entry #{index}")
                    .WithDescription(finalText)
                    .WithColor(Color.Magenta)
                    .WithFooter($"Use {Prefix}hrr {index} to request this Pokémon for trade.\n" +
                    $"Use {Prefix}hrd {index} to download this PKM file.");

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await ReplyAsync($"**Error:** {ex.Message}");
            }
            finally
            {
                try { if (Context.Message is IUserMessage m) await m.DeleteAsync(); } catch { }
            }
        }

        private static ulong GetHomeTrackerSafe(PKM pkm)
        {
            var type = pkm.GetType();
            var prop = type.GetProperty("Tracker");

            if (prop == null)
                return 0; // This format doesn’t support HOME tracking

            object? value = prop.GetValue(pkm);
            if (value is ulong tracker)
                return tracker;

            return 0;
        }


        // ============================================================================
        //  DOWNLOAD FILE
        // ============================================================================
        [Command("homereadydownload")]
        [Alias("hrd")]
        [Summary("Downloads a HOME-ready PKM file by its number from the list.")]
        [RequireQueueRole(nameof(DiscordManager.RolesClone))]
        private async Task HOMEReadyDownloadAsync(int index)
        {
            if (string.IsNullOrWhiteSpace(HOMEFolder))
            {
                await ReplyAsync("This bot does not have the HOME-Ready module configured.").ConfigureAwait(false);
                return;
            }

            try
            {
                var files = Directory.GetFiles(HOMEFolder)
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                {
                    await ReplyAsync("No HOME-ready PKM files found.").ConfigureAwait(false);
                    return;
                }

                if (index < 1 || index > files.Count)
                {
                    await ReplyAsync("Invalid entry number. Choose a valid file.");
                    return;
                }

                var filePath = files[index - 1];
                var fileName = Path.GetFileName(filePath);

                // Send file directly
                await using (var fs = File.OpenRead(filePath))
                {
                    var msg = await Context.Channel.SendFileAsync(
                        stream: fs,
                        filename: fileName,
                        text: $"Here's your HOME-Ready Pokémon file: **{fileName}**"
                    );
                }
            }
            catch (Exception ex)
            {
                await ReplyAsync($"**Error:** {ex.Message}");
            }
            finally
            {
                try { if (Context.Message is IUserMessage m) await m.DeleteAsync(); } catch { }
            }
        }
    }
}
