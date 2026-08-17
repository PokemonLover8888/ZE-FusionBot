using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Z3;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysBot.Pokemon.ConsoleApp;

public static class Program
{
    private const string ConfigPath = "config.json";

    private static void ExitNoConfig()
    {
        var bot = new PokeBotState { Connection = new SwitchConnectionConfig { IP = "192.168.0.1", Port = 6000 }, InitialRoutine = PokeRoutineType.FlexTrade };
        var cfg = new ProgramConfig { Bots = [bot] };
        var created = JsonSerializer.Serialize(new JsonSerializerOptions // Serialize the current config to json
        {
            WriteIndented = true
        });
        File.WriteAllText(ConfigPath, created);
        LogUtil.LogInfo("SysBot", "Created new config file since none was found in the program's path. Please configure it and restart the program.");
        LogUtil.LogInfo("SysBot", "It is suggested to configure this config file using the GUI project if possible, as it will help you assign values correctly.");
        LogUtil.LogInfo("SysBot", "Press any key to exit.");
        Console.ReadKey();
    }

    private static void Main(string[] args)
    {
        LogUtil.LogInfo("SysBot", "Starting up...");
        PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>();

        // MULTI-TENANT MODE: config.json paths passed as args -> run each as its own tenant
        // (own Discord token + Switch) inside THIS single process, sharing the static PKHeX data
        // that would otherwise be loaded once per process. Group only SAME-GAME bots together
        // (some statics like BatchCommandNormalizer.CurrentGameMode assume one mode per process).
        if (args.Length > 0)
        {
            var configs = new System.Collections.Generic.List<(string name, ProgramConfig cfg)>();
            foreach (var path in args)
            {
                if (!File.Exists(path))
                {
                    LogUtil.LogInfo("SysBot", $"[MultiTenant] Config not found, skipping: {path}");
                    continue;
                }
                try
                {
                    var cfg = JsonSerializer.Deserialize<ProgramConfig>(File.ReadAllText(path)) ?? new ProgramConfig();
                    var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
                    var name = Path.GetFileName(folder);
                    foreach (var b in cfg.Bots)
                        b.DataFolder = folder; // per-bot trade-code isolation (files stay in the bot's own folder)
                    configs.Add((name, cfg));
                }
                catch (Exception ex)
                {
                    LogUtil.LogInfo("SysBot", $"[MultiTenant] Failed to load {path}: {ex.Message}");
                }
            }

            if (configs.Count == 0)
            {
                LogUtil.LogInfo("SysBot", "[MultiTenant] No valid configs supplied. Exiting.");
                return;
            }

            // Different games are safe (ambient-context fix). SAME game appearing twice is NOT —
            // same-game bots collide on the static SysCord<T>.Runner (108 sites, unfixed). So the
            // rule is: at most ONE bot per game per process.
            var dupModes = configs.GroupBy(c => c.cfg.Mode).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupModes.Count > 0)
            {
                LogUtil.LogInfo("SysBot", $"[MultiTenant] REFUSING: same game appears more than once ({string.Join(", ", dupModes)}). " +
                    "Same-game bots collide on static Runner<T>. Use ONE bot per game per process.");
                return;
            }

            BotContainer.RunMany(configs);
            return;
        }

        if (!File.Exists(ConfigPath))
        {
            ExitNoConfig();
            return;
        }

        try
        {
            var lines = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<ProgramConfig>(lines) ?? new ProgramConfig();
            BotContainer.RunBots(cfg);
        }
        catch (Exception)
        {
            LogUtil.LogInfo("SysBot", "Unable to start bots with saved config file. Please copy your config from the WinForms project or delete it and reconfigure.");
            Console.ReadKey();
        }
    }
}

public static class BotContainer
{
    public static void RunBots(ProgramConfig prog)
    {
        // Set the current game mode for BatchCommandNormalizer
        BatchCommandNormalizer.CurrentGameMode = prog.Mode;

        IPokeBotRunner env = GetRunner(prog);
        foreach (var bot in prog.Bots)
        {
            bot.Initialize();
            if (!AddBot(env, bot, prog.Mode))
                LogUtil.LogInfo("SysBot", $"Failed to add bot: {bot}");
        }

        LogUtil.Forwarders.Add(ConsoleForwarder.Instance);
        env.StartAll();
        LogUtil.LogInfo("SysBot", $"Started all bots (Count: {prog.Bots.Length}).");
        LogUtil.LogInfo("SysBot", "Press any key to stop execution and quit. Feel free to minimize this window!");
        Console.ReadKey();
        env.StopAll();
    }

    public static void RunMany(System.Collections.Generic.List<(string name, ProgramConfig cfg)> configs)
    {
        // All configs are the same game mode (validated by caller). PKHeX static data loads once
        // for this whole process; each tenant gets its own Hub/Discord(SysCord)/Switch connections.
        BatchCommandNormalizer.CurrentGameMode = configs[0].cfg.Mode;
        LogUtil.Forwarders.Add(ConsoleForwarder.Instance);

        // A host must never die silently — but log crashes to a LOCAL FILE, not LogUtil, which
        // echoes to the bot's Discord logs channel. Discord.Net's frequent transient REST timeouts
        // on fire-and-forget sends surface as "unobserved task exceptions"; they're harmless (trades
        // still complete) and echoing them floods the channel. Real terminating crashes -> file.
        var hostCrashLog = System.IO.Path.Combine(AppContext.BaseDirectory, "host-crash.log");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        { try { System.IO.File.AppendAllText(hostCrashLog, $"{DateTime.UtcNow:u} UNHANDLED (terminating): {e.ExceptionObject}\n"); } catch { } };
        // Observe transient fire-and-forget failures silently (same as Discord.Net's default) — no spam.
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();

        var envs = new System.Collections.Generic.List<IPokeBotRunner>();
        foreach (var (name, prog) in configs)
        {
            LogUtil.LogInfo("SysBot", $"===== [MultiTenant] Starting '{name}' (mode {prog.Mode}, {prog.Bots.Length} bot(s)) =====");
            try
            {
                IPokeBotRunner env = GetRunner(prog);
                foreach (var bot in prog.Bots)
                {
                    bot.Initialize();
                    if (!AddBot(env, bot, prog.Mode))
                        LogUtil.LogInfo("SysBot", $"[MultiTenant] {name}: failed to add bot {bot}");
                }
                env.StartAll();
                envs.Add(env);
                // Serve /api/bot/instances on this bot's own ControlPanelPort so the website's
                // trade-bridge (polls each bot's fixed port) sees it — no Node changes needed.
                // Also accept POST /api/web-trade for the browser Trade Portal, dispatched to the
                // right game type so WebTradeService queues on THIS bot.
                System.Func<string, string, ulong, int, bool, System.Threading.Tasks.Task<SysBot.Pokemon.Discord.WebTradeResult>>? webTrade = prog.Mode switch
                {
                    ProgramMode.SWSH => (s, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PK8>.QueueAsync(s, un, uid, tc, sh),
                    ProgramMode.BDSP => (s, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PB8>.QueueAsync(s, un, uid, tc, sh),
                    ProgramMode.LA   => (s, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PA8>.QueueAsync(s, un, uid, tc, sh),
                    ProgramMode.SV   => (s, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PK9>.QueueAsync(s, un, uid, tc, sh),
                    ProgramMode.LGPE => (s, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PB7>.QueueAsync(s, un, uid, tc, sh),
                    ProgramMode.PLZA => (s, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PA9>.QueueAsync(s, un, uid, tc, sh),
                    _ => null
                };
                // Live queue for THIS bot in the WinForms /api/bot/queue/list shape — the bridge
                // aggregates every bot's list into /queue so the website can track a web trade's
                // position + completion (else it's stuck at 14%).
                System.Func<string>? queueList = prog.Mode switch
                {
                    ProgramMode.SWSH => SysBot.Pokemon.Discord.WebTradeService<PK8>.GetQueueListJson,
                    ProgramMode.BDSP => SysBot.Pokemon.Discord.WebTradeService<PB8>.GetQueueListJson,
                    ProgramMode.LA   => SysBot.Pokemon.Discord.WebTradeService<PA8>.GetQueueListJson,
                    ProgramMode.SV   => SysBot.Pokemon.Discord.WebTradeService<PK9>.GetQueueListJson,
                    ProgramMode.LGPE => SysBot.Pokemon.Discord.WebTradeService<PB7>.GetQueueListJson,
                    ProgramMode.PLZA => SysBot.Pokemon.Discord.WebTradeService<PA9>.GetQueueListJson,
                    _ => null
                };
                // The Forge: live legality + HOME-ready preview for a Showdown set (no trade queued).
                System.Func<string, System.Threading.Tasks.Task<SysBot.Pokemon.Discord.WebValidateResult>>? validate = prog.Mode switch
                {
                    ProgramMode.SWSH => s => SysBot.Pokemon.Discord.WebTradeService<PK8>.ValidateAsync(s),
                    ProgramMode.BDSP => s => SysBot.Pokemon.Discord.WebTradeService<PB8>.ValidateAsync(s),
                    ProgramMode.LA   => s => SysBot.Pokemon.Discord.WebTradeService<PA8>.ValidateAsync(s),
                    ProgramMode.SV   => s => SysBot.Pokemon.Discord.WebTradeService<PK9>.ValidateAsync(s),
                    ProgramMode.LGPE => s => SysBot.Pokemon.Discord.WebTradeService<PB7>.ValidateAsync(s),
                    ProgramMode.PLZA => s => SysBot.Pokemon.Discord.WebTradeService<PA9>.ValidateAsync(s),
                    _ => null
                };
                // One-code batch: queue a whole box under a single link code (web version of $bt).
                System.Func<System.Collections.Generic.List<string>, string, ulong, int, bool, System.Threading.Tasks.Task<SysBot.Pokemon.Discord.WebTradeResult>>? batchTrade = prog.Mode switch
                {
                    ProgramMode.SWSH => (l, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PK8>.QueueBatchAsync(l, un, uid, tc, sh),
                    ProgramMode.BDSP => (l, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PB8>.QueueBatchAsync(l, un, uid, tc, sh),
                    ProgramMode.LA   => (l, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PA8>.QueueBatchAsync(l, un, uid, tc, sh),
                    ProgramMode.SV   => (l, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PK9>.QueueBatchAsync(l, un, uid, tc, sh),
                    ProgramMode.LGPE => (l, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PB7>.QueueBatchAsync(l, un, uid, tc, sh),
                    ProgramMode.PLZA => (l, un, uid, tc, sh) => SysBot.Pokemon.Discord.WebTradeService<PA9>.QueueBatchAsync(l, un, uid, tc, sh),
                    _ => null
                };
                try { TenantStatusServer.Start(prog.Hub.WebServer.ControlPanelPort, name, env, webTrade, queueList, validate, batchTrade); }
                catch (Exception sx) { LogUtil.LogInfo("SysBot", $"[MultiTenant] status endpoint for '{name}' failed: {sx.Message}"); }
                LogUtil.LogInfo("SysBot", $"[MultiTenant] '{name}' started.");
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo("SysBot", $"[MultiTenant] '{name}' FAILED to start: {ex.Message}");
            }
        }

        LogUtil.LogInfo("SysBot", $"[MultiTenant] {envs.Count}/{configs.Count} tenant(s) running in ONE process (headless — no console input needed).");
        // Block FOREVER with zero CPU. Do NOT use Console.ReadKey(): on a hidden/windowless
        // console it can spuriously return or throw, which would fall through to a graceful
        // StopAll and SILENTLY kill the whole host (the exact "vanished with no log" failure).
        // The bots run on their own tasks; this thread just parks until the process is stopped.
        new System.Threading.ManualResetEventSlim(false).Wait();
    }

    private static bool AddBot(IPokeBotRunner env, PokeBotState cfg, ProgramMode mode)
    {
        if (!cfg.IsValid())
        {
            LogUtil.LogInfo("SysBot", $"{cfg}'s config is not valid.");
            return false;
        }

        PokeRoutineExecutorBase newBot;
        try
        {
            newBot = env.CreateBotFromConfig(cfg);
        }
        catch
        {
            LogUtil.LogInfo("SysBot", $"Current Mode ({mode}) does not support this type of bot ({cfg.CurrentRoutineType}).");
            return false;
        }
        try
        {
            env.Add(newBot);
        }
        catch (ArgumentException ex)
        {
            LogUtil.LogInfo("SysBot", ex.Message);
            return false;
        }

        LogUtil.LogInfo("SysBot", $"Added: {cfg}: {cfg.InitialRoutine}");
        return true;
    }

    private static IPokeBotRunner GetRunner(ProgramConfig prog) => prog.Mode switch
    {
        ProgramMode.SWSH => new PokeBotRunnerImpl<PK8>(new PokeTradeHub<PK8>(prog.Hub), new BotFactory8SWSH(), prog),
        ProgramMode.BDSP => new PokeBotRunnerImpl<PB8>(new PokeTradeHub<PB8>(prog.Hub), new BotFactory8BS(), prog),
        ProgramMode.LA => new PokeBotRunnerImpl<PA8>(new PokeTradeHub<PA8>(prog.Hub), new BotFactory8LA(), prog),
        ProgramMode.SV => new PokeBotRunnerImpl<PK9>(new PokeTradeHub<PK9>(prog.Hub), new BotFactory9SV(), prog),
        ProgramMode.LGPE => new PokeBotRunnerImpl<PB7>(new PokeTradeHub<PB7>(prog.Hub), new BotFactory7LGPE(), prog),
        ProgramMode.PLZA => new PokeBotRunnerImpl<PA9>(new PokeTradeHub<PA9>(prog.Hub), new BotFactory9PLZA(), prog),
        _ => throw new IndexOutOfRangeException("Unsupported mode."),
    };
}

/// <summary>
/// One tiny HttpListener per bot, on that bot's own ControlPanelPort, serving exactly the
/// /api/bot/instances shape the website's trade-bridge polls for (webPort, botStatuses[],
/// discordConnected, switchReady, tradeReady). This restores website visibility for bots that
/// share a process, without any change to the Node trade-bridge.
/// </summary>
public static class TenantStatusServer
{
    private static string JsonEsc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

    public static void Start(int port, string botName, IPokeBotRunner runner,
        System.Func<string, string, ulong, int, bool, System.Threading.Tasks.Task<SysBot.Pokemon.Discord.WebTradeResult>>? webTrade = null,
        System.Func<string>? queueList = null,
        System.Func<string, System.Threading.Tasks.Task<SysBot.Pokemon.Discord.WebValidateResult>>? validate = null,
        System.Func<System.Collections.Generic.List<string>, string, ulong, int, bool, System.Threading.Tasks.Task<SysBot.Pokemon.Discord.WebTradeResult>>? batchTrade = null)
    {
        if (port <= 0)
        {
            LogUtil.LogInfo("SysBot", $"[MultiTenant] '{botName}' has no ControlPanelPort — website won't see it.");
            return;
        }

        // Raw TcpListener (loopback socket) — needs NO admin / http.sys URL reservation, unlike
        // HttpListener which silently fails without one. Answers any request with the exact
        // /api/bot/instances JSON the trade-bridge polls for.
        System.Net.Sockets.TcpListener tcp;
        try
        {
            tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            tcp.Start();
        }
        catch (Exception ex)
        {
            LogUtil.LogInfo("SysBot", $"[MultiTenant] status port {port} for '{botName}' failed: {ex.Message}");
            return;
        }
        LogUtil.LogInfo("SysBot", $"[MultiTenant] status endpoint '{botName}' -> 127.0.0.1:{port}");

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            while (true)
            {
                System.Net.Sockets.TcpClient client;
                try { client = await tcp.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch { break; }
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        using (client)
                        {
                            var stream = client.GetStream();
                            var reqBuf = new byte[8192];
                            int read = 0;
                            try { read = await stream.ReadAsync(reqBuf, 0, reqBuf.Length).ConfigureAwait(false); } catch { }
                            var reqText = System.Text.Encoding.UTF8.GetString(reqBuf, 0, read);

                            string method = "GET", pathReq = "/";
                            int sp1 = reqText.IndexOf(' ');
                            if (sp1 > 0) { method = reqText.Substring(0, sp1); int sp2 = reqText.IndexOf(' ', sp1 + 1); if (sp2 > sp1) pathReq = reqText.Substring(sp1 + 1, sp2 - sp1 - 1); }

                            async System.Threading.Tasks.Task Respond(int codeNum, string codeText, string payload)
                            {
                                var b = System.Text.Encoding.UTF8.GetBytes(payload);
                                var h = System.Text.Encoding.ASCII.GetBytes(
                                    "HTTP/1.1 " + codeNum + " " + codeText + "\r\nContent-Type: application/json\r\n" +
                                    "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                    "Access-Control-Allow-Headers: Content-Type\r\nContent-Length: " + b.Length +
                                    "\r\nConnection: close\r\n\r\n");
                                await stream.WriteAsync(h, 0, h.Length).ConfigureAwait(false);
                                if (b.Length > 0) await stream.WriteAsync(b, 0, b.Length).ConfigureAwait(false);
                                await stream.FlushAsync().ConfigureAwait(false);
                            }

                            if (method == "OPTIONS") { await Respond(204, "No Content", "").ConfigureAwait(false); return; }

                            // GET /api/bot/queue/list — this bot's live queue (WinForms shape) so the
                            // bridge/website can track a web trade's position + completion.
                            if (method == "GET" && queueList != null && pathReq.StartsWith("/api/bot/queue/list"))
                            {
                                string q;
                                try { q = queueList(); } catch (Exception ex) { q = "{\"success\":false,\"queueCount\":0,\"queue\":[],\"error\":\"" + JsonEsc(ex.Message) + "\"}"; }
                                await Respond(200, "OK", q).ConfigureAwait(false);
                                return;
                            }

                            // POST /api/trade/batch — one-code batch: a whole box under a single link code.
                            // MUST be before /api/trade (StartsWith-matches this path too).
                            if (method == "POST" && batchTrade != null && pathReq.StartsWith("/api/trade/batch"))
                            {
                                string bJson;
                                try
                                {
                                    int bs = reqText.IndexOf("\r\n\r\n");
                                    string bodyStr = bs >= 0 ? reqText.Substring(bs + 4) : "";
                                    using var doc = System.Text.Json.JsonDocument.Parse(bodyStr);
                                    var r = doc.RootElement;
                                    var sets = new System.Collections.Generic.List<string>();
                                    if (r.TryGetProperty("sets", out var sv) && sv.ValueKind == System.Text.Json.JsonValueKind.Array)
                                        foreach (var el in sv.EnumerateArray()) { var s = el.GetString(); if (!string.IsNullOrWhiteSpace(s)) sets.Add(s); }
                                    string uname = r.TryGetProperty("username", out var un) ? (un.GetString() ?? "WebUser") : "WebUser";
                                    ulong uid = 0;
                                    if (r.TryGetProperty("discordUserId", out var uv))
                                        uid = uv.ValueKind == System.Text.Json.JsonValueKind.String ? (ulong.TryParse(uv.GetString(), out var pu) ? pu : 0) : (uv.TryGetUInt64(out var nu) ? nu : 0);
                                    int tcode = 0;
                                    if (r.TryGetProperty("tradeCode", out var tv))
                                        tcode = tv.ValueKind == System.Text.Json.JsonValueKind.String ? (int.TryParse(tv.GetString(), out var pt) ? pt : 0) : (tv.TryGetInt32(out var it) ? it : 0);
                                    bool shiny = r.TryGetProperty("forceShiny", out var fv) && fv.ValueKind == System.Text.Json.JsonValueKind.True;

                                    var result = await batchTrade(sets, uname, uid, tcode, shiny).ConfigureAwait(false);
                                    bJson = result.Success
                                        ? "{\"success\":true,\"tradeId\":" + result.TradeId + ",\"tradeCode\":" + result.Code + ",\"position\":" + result.Position + ",\"pokemon\":\"" + JsonEsc(result.Species) + "\"}"
                                        : "{\"success\":false,\"error\":\"" + JsonEsc(result.Error) + "\"}";
                                }
                                catch (Exception ex) { bJson = "{\"success\":false,\"error\":\"" + JsonEsc("Could not read the request: " + ex.Message) + "\"}"; }
                                await Respond(200, "OK", bJson).ConfigureAwait(false);
                                return;
                            }

                            // POST /api/trade/validate — The Forge: legality + HOME-ready preview, NO queue.
                            // MUST be checked before /api/trade (which StartsWith-matches this path too).
                            if (method == "POST" && validate != null && pathReq.StartsWith("/api/trade/validate"))
                            {
                                string vJson;
                                try
                                {
                                    int bs = reqText.IndexOf("\r\n\r\n");
                                    string bodyStr = bs >= 0 ? reqText.Substring(bs + 4) : "";
                                    using var doc = System.Text.Json.JsonDocument.Parse(bodyStr);
                                    string set = doc.RootElement.TryGetProperty("showdownSet", out var sv) ? (sv.GetString() ?? "") : "";
                                    var r = await validate(set).ConfigureAwait(false);
                                    var sb = new System.Text.StringBuilder();
                                    sb.Append("{\"legal\":").Append(r.Legal ? "true" : "false")
                                      .Append(",\"homeReady\":").Append(r.HomeReady ? "true" : "false")
                                      .Append(",\"species\":\"").Append(JsonEsc(r.Species)).Append('"')
                                      .Append(",\"shiny\":").Append(r.Shiny ? "true" : "false")
                                      .Append(",\"ball\":\"").Append(JsonEsc(r.Ball)).Append('"')
                                      .Append(",\"level\":").Append(r.Level)
                                      .Append(",\"issues\":[");
                                    for (int i = 0; i < r.Issues.Length; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(JsonEsc(r.Issues[i])).Append('"'); }
                                    sb.Append(']');
                                    if (r.HomeAdvice != null) sb.Append(",\"homeAdvice\":\"").Append(JsonEsc(r.HomeAdvice)).Append('"');
                                    if (r.Error != null) sb.Append(",\"error\":\"").Append(JsonEsc(r.Error)).Append('"');
                                    sb.Append('}');
                                    vJson = sb.ToString();
                                }
                                catch (Exception ex) { vJson = "{\"legal\":false,\"error\":\"" + JsonEsc("Could not read the request: " + ex.Message) + "\"}"; }
                                await Respond(200, "OK", vJson).ConfigureAwait(false);
                                return;
                            }

                            // POST /api/trade (WinForms/frontend shape) or /api/web-trade (our shape).
                            if (method == "POST" && webTrade != null &&
                                (pathReq.StartsWith("/api/trade") || pathReq.StartsWith("/api/web-trade")))
                            {
                                bool apiTrade = pathReq.StartsWith("/api/trade");
                                string respJson;
                                try
                                {
                                    int bs = reqText.IndexOf("\r\n\r\n");
                                    string bodyStr = bs >= 0 ? reqText.Substring(bs + 4) : "";
                                    using var doc = System.Text.Json.JsonDocument.Parse(bodyStr);
                                    var r = doc.RootElement;
                                    string set = r.TryGetProperty("showdownSet", out var sv) ? (sv.GetString() ?? "") : "";
                                    string uname = r.TryGetProperty("username", out var un) ? (un.GetString() ?? "WebUser")
                                                 : (r.TryGetProperty("discordUsername", out var dn) ? (dn.GetString() ?? "WebUser") : "WebUser");
                                    ulong uid = 0;
                                    if (r.TryGetProperty("discordUserId", out var uv))
                                        uid = uv.ValueKind == System.Text.Json.JsonValueKind.String ? (ulong.TryParse(uv.GetString(), out var pu) ? pu : 0) : (uv.TryGetUInt64(out var nu) ? nu : 0);
                                    int tcode = 0;
                                    if (r.TryGetProperty("tradeCode", out var tv))
                                        tcode = tv.ValueKind == System.Text.Json.JsonValueKind.String ? (int.TryParse(tv.GetString(), out var pt) ? pt : 0) : (tv.TryGetInt32(out var it) ? it : 0);
                                    bool shiny = r.TryGetProperty("forceShiny", out var fv) && fv.ValueKind == System.Text.Json.JsonValueKind.True;

                                    var result = await webTrade(set, uname, uid, tcode, shiny).ConfigureAwait(false);
                                    if (result.Success)
                                        respJson = apiTrade
                                            ? "{\"success\":true,\"tradeId\":" + result.TradeId + ",\"tradeCode\":" + result.Code + ",\"position\":" + result.Position + ",\"pokemon\":\"" + JsonEsc(result.Species) + "\"}"
                                            : "{\"success\":true,\"code\":" + result.Code + ",\"tradeId\":" + result.TradeId + ",\"position\":" + result.Position + ",\"species\":\"" + JsonEsc(result.Species) + "\"}";
                                    else
                                        respJson = "{\"success\":false,\"error\":\"" + JsonEsc(result.Error) + "\"}";
                                }
                                catch (Exception ex) { respJson = "{\"success\":false,\"error\":\"" + JsonEsc("Could not read the request: " + ex.Message) + "\"}"; }
                                await Respond(200, "OK", respJson).ConfigureAwait(false);
                                return;
                            }

                            // Default: the /api/bot/instances status JSON the trade-bridge polls.
                            bool running = false;
                            try { running = runner.Bots.Count > 0 && runner.Bots.Any(b => b.IsRunning); } catch { }
                            var status = running ? "Idle" : "Stopped";
                            var safeName = botName.Replace("\"", "'").Replace("\\", "/");
                            var json = "{\"instances\":[{\"webPort\":" + port
                                + ",\"name\":\"" + safeName + "\""
                                + ",\"botStatuses\":[{\"status\":\"" + status + "\"}]"
                                + ",\"discordConnected\":" + (running ? "true" : "false")
                                + ",\"discordLatencyMs\":0,\"switchReady\":true,\"tradeReady\":" + (running ? "true" : "false")
                                + "}]}";
                            await Respond(200, "OK", json).ConfigureAwait(false);
                        }
                    }
                    catch { }
                });
            }
        });
    }
}
