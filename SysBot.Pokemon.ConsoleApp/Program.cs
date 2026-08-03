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
                LogUtil.LogInfo("SysBot", $"[MultiTenant] '{name}' started.");
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo("SysBot", $"[MultiTenant] '{name}' FAILED to start: {ex.Message}");
            }
        }

        LogUtil.LogInfo("SysBot", $"[MultiTenant] {envs.Count}/{configs.Count} tenant(s) running in ONE process. Press any key to stop all.");
        Console.ReadKey();
        foreach (var env in envs)
        {
            try { env.StopAll(); } catch { /* best effort */ }
        }
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
