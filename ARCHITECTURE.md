# PKM Universe Bot — Architecture Guide

Your own map of the codebase, so you can maintain and extend it without depending on anyone.
This is a fork of the SysBot.NET ecosystem (AGPL-3.0 — see LICENSE); the low-level Switch
communication and PKHeX legality engine are shared community work, everything above them is
where *your* bot lives.

---

## 1. The 30-second mental model

A trade happens in five layers, top to bottom:

```
Discord (user types $t ...)                     -> SysBot.Pokemon.Discord
  -> Legality/generation (make a legal PKM)      -> SysBot.Pokemon (Helpers/AutoLegalityWrapper + deps/PKHeX)
    -> Queue (wait your turn)                     -> SysBot.Pokemon/TradeHub + /Queues
      -> Bot routine (drive the game)             -> SysBot.Pokemon/{SV,SWSH,BDSP,LA,LGPE,PLZA}
        -> Switch connection (button presses)     -> SysBot.Base (sys-botbase over WiFi/USB)
```

The GUI (WinForms) and the website talk to the same core through a small HTTP control panel.

---

## 2. Projects (what each one is for)

| Project | Role |
|---|---|
| **SysBot.Base** | Lowest level. Talks to the Switch via **sys-botbase** (WiFi/USB): connect, read/write memory, press buttons. You rarely touch this. |
| **SysBot.Pokemon** | The core engine. Per-game trade routines, the queue/hub, and the **legality pipeline**. This + Discord is where 95% of your work happens. |
| **SysBot.Pokemon/deps/** | The **PKHeX.Core** + **PKHeX.Core.AutoMod** DLLs — the legality/legalization library. NOT source you edit; you swap versions here. (SV bots run v26.4.11.0, everything else v26.5.6.0.) |
| **SysBot.Pokemon.Discord** | The Discord bot: gateway/presence, commands (`$t`, `$bt`, `$me`, etc.), embeds, queue display. |
| **SysBot.Pokemon.WinForms** | The desktop GUI (`PKM-Universe Bot.exe`), plus the **web control panel API** the website calls. |
| **SysBot.Pokemon.Z3** | Seed checking (Z3 solver) for SWSH raids/seed requests. |
| **SysBot.Pokemon.Twitch / .YouTube** | Alternate command front-ends. Not used by your Discord fleet. |
| **SysBot.Pokemon.ConsoleApp** | Headless runner (no GUI). Not what your bots use. |
| **SysBot.Tests** | Unit tests. |
| **home-verify / home-legalize / home-gendex / home-itemtest, probe/, Tools/EventIndexer** | **Your** custom tooling — legality probes, dex generation, the form-sprite map generator, event indexer. Standalone; reference SysBot.Pokemon + deps. |

---

## 3. How a `$t` trade flows (the path to know)

1. **Command** — `SysBot.Pokemon.Discord/Commands/Bots/TradeModule.cs` receives `$t <showdown>`.
2. **Generation** — it calls into `SysBot.Pokemon.Discord/Helpers/TradeModule/Helpers.cs` →
   `ProcessShowdownSetAsync`, which uses
   `SysBot.Pokemon/Helpers/AutoLegalityWrapper.cs` (`GetLegalForTrade`, `GetLegalNativeDirect`,
   `EnsureInitialized`) + the PKHeX deps to build a **legal PKM**.
   **This file is where almost all your legality fixes live** (native safety nets, held-item
   restore, Level clamps, marks, shiny-lock gating, etc.).
3. **Embed + enqueue** — `Helpers/QueueHelper.cs` builds the trade embed
   (`Helpers/TradeEmbedDataBuilder.cs` for the sprite/data, `Helpers/DetailsExtractor.cs` for
   the field text + type emojis) and adds a `PokeTradeDetail<T>` to the queue
   (`SysBot.Pokemon/TradeHub/` + `SysBot.Pokemon/Queues/`).
4. **Execution** — a running bot of the matching game type pulls the trade and drives the game:
   `SysBot.Pokemon/<GAME>/BotTrade/PokeTradeBot<GAME>.cs`
   (`PokeTradeBotSV`, `PokeTradeBotSWSH`, `PokeTradeBotBS`, `PokeTradeBotLA`, `PokeTradeBotPLZA`;
   LGPE uses PB7). These call `PokeRoutineExecutor<n><GAME>.cs` for the game-specific navigation.
5. **Trade-partner data / AutoOT** — inside the per-game bot, `ApplyAutoOT` stamps the partner's
   OT/TID and (critically) **keeps HOME trackers** (never strips them).
6. **Completion** — `SysBot.Pokemon.Discord/Helpers/DiscordTradeNotifier.cs` /
   `TradeFinished` posts the completion embed and reports to the website bridge.

---

## 4. "I want to change X — go here"

| Want to change… | File(s) |
|---|---|
| How a requested Pokémon is generated / made legal | `SysBot.Pokemon.Discord/Helpers/TradeModule/Helpers.cs` (`ProcessShowdownSetAsync`) + `SysBot.Pokemon/Helpers/AutoLegalityWrapper.cs` |
| Trade embed look (sprite, fields, emojis) | `TradeEmbedDataBuilder.cs`, `DetailsExtractor.cs`, `QueueHelper.cs` |
| Alt-form sprite mapping | `TradeEmbedDataBuilder.cs` `FormSpriteId` (regenerate via `home-itemtest`) |
| A Discord command's behavior / gating | `SysBot.Pokemon.Discord/Commands/**` (e.g. `Bots/TradeModule.cs`, `Bots/MysteryEggModule.cs`) |
| Bot online/offline **Discord presence** | `SysBot.Pokemon.Discord/SysCord.cs` (`MonitorStatusAsync`, `LoadLoggingAndEcho`) |
| Online/offline **status embed** | `SysCord.cs` `AnnounceBotStatus` (+ `GetBotPokemonSprite` for the thumbnail) |
| Per-game navigation / timing | `SysBot.Pokemon/<GAME>/BotTrade/PokeTradeBot<GAME>.cs` + `PokeRoutineExecutor*<GAME>.cs` |
| GUI window / title / update button | `SysBot.Pokemon.WinForms/Main.cs`, `UpdateChecker.cs` (auto-update is disabled) |
| Web control-panel API the site calls | `SysBot.Pokemon.WinForms/WebApi/BotServer.cs` (port = `Hub.WebServer.ControlPanelPort`) |
| Legality settings / toggles | `SysBot.Pokemon/Settings/LegalitySettings.cs` (surfaced in each bot's `config.json`) |
| Version string | `SysBot.Pokemon/Helpers/TradeBot.cs` (`Version`) |

---

## 5. Build & deploy model (how it actually ships)

- Each bot folder on the Desktop runs a **self-contained single-file** `PKM-Universe Bot.exe`
  (~224 MB). It bundles the .NET runtime + all DLLs; the loose DLLs you may see in older folders
  are vestigial.
- **Build:** `dotnet publish SysBot.Pokemon.WinForms/... -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-sc`
- **Deploy:** copy the exe into each bot folder, restart the bot. (Building alone does nothing to
  running bots.)
- **Deps split (critical):** SV bots (Mew-SV, Meloetta-SV) run PKHeX **v26.4.11.0**; all others run
  **v26.5.6.0**. To build the SV variant, swap `SysBot.Pokemon/deps` from `probe/old-deps`, build to
  `publish-sv`, then **restore deps** to v26.5.6.0 (always verify).
- **config.json** (per bot folder) is the runtime config: Discord token, Switch IP/port, control-panel
  port, channels, roles, emojis, legality flags. One `Bots[]` entry per Switch console.

---

## 6. What's genuinely *yours* (the differentiators)

Not part of upstream — built for PKM Universe:
- The whole **website + webtrade + bot-monitor** stack (`creator.pkm-universe.com`, `trade-bridge-api.js`).
- **Native safety nets** (SV/BDSP/Z-A rebuild catchable species that leak Non-Native).
- **Form-sprite map** (verified species+form → PokeAPI HOME sprite).
- **HOME tooling** (verify/legalize/gendex, shiny living dex).
- **Elite/Premium gating** for batch + Mystery Egg.
- Move-type **gem emojis**, held-item restore, batch announce-once, steady-Online presence.
- The **wondercard loader** for mythicals.

---

## 7. External dependencies (the AGPL family — keep credited)

- **PKHeX** (kwsch) — legality core, shipped as `deps/PKHeX.Core.dll`.
- **PKHeX.Core.AutoMod** (ALM) — legalization, `deps/PKHeX.Core.AutoMod.dll`.
- **sys-botbase** (olliz0r) / **usb-botbase** (Koi-3088) — the Switch-side homebrew the bot talks to.
- **SysBot.NET** (kwsch) → **PokeBot** (hexbyt3) → **ZE-FusionBot** (Secludedly) → this fork.

These stay credited in `README.md` and `LICENSE` — that's an AGPL requirement, not optional.
