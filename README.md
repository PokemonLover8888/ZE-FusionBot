<h1 align="center">
  ✨ PKM Universe Reborn — Trade Bot ✨
</h1>

<p align="center">
  <i>A premium, gold-themed SysBot.NET fork powering the PKM Universe Reborn community — full Pokémon HOME-compatible trading across LGPE, SWSH, BDSP, PLA, SV, and Legends: Z-A.</i>
</p>

<h3 align="center">
  🌐 Visit <a href="https://pkm-universe.com">pkm-universe.com</a> to join the community!
</h3>

> ⚠️ **Font Notice:** If the program's fonts are not displaying properly, download them [here](https://github.com/PokemonLover8888/ZE-FusionBot/blob/main/.extra/Fonts.7z) and install them on your machine.

> 🧬 **Heritage:** PKM Universe Reborn's bot is a branded fork of **[Secludedly's ZE-FusionBot](https://github.com/Secludedly/ZE-FusionBot)**, which itself builds on **[hexbyt3's PokeBot](https://github.com/hexbyt3/PokeBot)** and the wider SysBot.NET ecosystem. Full credits below — none of this exists without them.

---

## 🚀 Introduction

This is the trade-bot engine behind **PKM Universe Reborn**, a community running a fleet of themed Pokémon trade bots (Celebi-SWSH, Jirachi-SWSH, Mew-SV, Meloetta-SV, Rayquaza-BDSP, Giratina-BDSP, Flareon-LGPE, plus the Legends: Z-A lineup and more). It pairs a polished gold WinForms GUI with a deep, HOME-aware legality pipeline so members get clean, HOME-uploadable Pokémon every time.

Built on top of Secludedly's ZE-FusionBot, this fork adds PKM Universe branding, a curated HOME-Ready event library, smart fair-cooldowns, species-aware HOME-upload guidance, and a steady stream of per-game legality fixes.

---

## 🧬 Community Roots & Inspirations

> **This bot is a fusion by design — built from years of shared ideas, forks, experiments, and respect for the SysBot.NET ecosystem.**

PKM Universe Reborn's bot stands directly on **[Secludedly's ZE-FusionBot](https://github.com/Secludedly/ZE-FusionBot)** — the immediate parent of this fork — which in turn was created, inspired, and upgraded over years through the collaboration of many developers. The projects below represent the strongest influences in the SysBot.NET space and the inspiration behind calling this a **Fusion** bot.

<details>
<summary><strong>🧬 Click to view credits & inspirations</strong></summary><br />

### 🧬 Direct Upstream

- **[ZE-FusionBot](https://github.com/Secludedly/ZE-FusionBot)**
  Created by **[@Secludedly](https://github.com/Secludedly)** — the direct parent of this fork.
  *The entire GUI, module set, and legality foundation PKM Universe builds on.*

### 🧬 Foundational Projects

- **[SysBot.NET](https://github.com/kwsch/SysBot.NET)**
  Created by **[@kwsch](https://github.com/kwsch)** — also the creator of PKHeX.
  *The origin of everything.*

- **[ForkBot.NET](https://github.com/Koi-3088/ForkBot.NET)**
  Developed by **[@Koi-3088](https://github.com/Koi-3088)**.
  One of the earliest and most influential forks. TradeCord will never be forgotten.

- **[SysBot.NET (berichan fork)](https://github.com/berichan/SysBot.NET)**
  An insightful and clever fork by **[@berichan](https://github.com/berichan)** that helped shape many later ideas.

- **[SysBot.NET (Lusamine fork)](https://github.com/Lusamine/SysBot.NET)**
  A respected fork that stayed close to the original vision, maintained by **[@Lusamine](https://github.com/Lusamine)**.

- **[SysBot.NET (santacrab fork)](https://github.com/santacrab2/SysBot.NET)**
  A long-followed fork by **[@santacrab2](https://github.com/santacrab2)**.

---

### 🔧 Evolutionary & Community-Driven Bots

- **[PokeBot](https://github.com/hexbyt3/PokeBot)**
  Created by **[@hexbyt3](https://github.com/hexbyt3)** — the **primary foundation of ZE-FusionBot**, and therefore of this fork. Much of the structure, philosophy, and stability originates here.

- **[MergeBot](https://github.com/Paschar1/MergeBot)**
  Originally created by **[@bakakaito](https://github.com/bakakaito)**, possibly preserved by **[@Paschar1](https://github.com/Paschar1)**.

---

### 🚀 Additional Inspirations

- **[SysBot.NET (easyworld fork)](https://github.com/easyworld/SysBot.NET)** — by **[@easyworld](https://github.com/easyworld)**
- **[ManuBot.NET](https://github.com/Manu098vm/ManuBot.NET)** — by **[@Manu098vm](https://github.com/Manu098vm)**
- **[ManuBot.NET (9B1td0 fork)](https://github.com/9B1td0/ManuBot.NET)** — by **[@9B1td0](https://github.com/9B1td0)**
- **[DudeBot.NET](https://github.com/Havokx89/DudeBot.NET)** — by **[@Havokx89](https://github.com/Havokx89)**
- **[ZenBot.NET](https://github.com/Omni-KingZeno/ZenBot.NET)** — by **[@Omni-KingZeno](https://github.com/Omni-KingZeno)**
- **[TradeBot](https://github.com/jonklee99/Tradebot)** — by **[@jonklee99](https://github.com/jonklee99)** with **[@joseph11024](https://github.com/joseph11024)**

</details>

---

## ✨ PKM Universe Highlights

These are the enhancements layered on top of ZE-FusionBot for the PKM Universe community:

- **🏠 HOME-Ready Event Library** — request from a curated library of 13,000+ HOME-tracked event Pokémon with `ll <species>` (list) and `lr <number>` (request). Aliases for the classic `hrl` / `hrr`.
- **🛡️ Smart Fair-Cooldowns** — cooldowns only apply to *completed* trades. Format mistakes, illegal sets, and cancelled trades are free retries, with a clear "No Cooldown Applied" banner on every failed request.
- **🧭 Species-aware HOME guidance** — when a Z-A bot ships a non-native shiny that can't enter HOME, the embed points members to the right SwSh / BDSP bot for a HOME-uploadable version.
- **🎯 Clean HOME-uploadable legendaries** — Max Lair (SwSh) and Ramanas Park (BDSP) `tracker=0` generation so members' shinies upload to HOME and get a real tracker assigned.
- **🩹 Per-game legality fixes** — correct met locations (Mesprit → Lake Verity), full movesets (Keldeo), proper move-type / gender emoji rendering, and more.
- **🔱 Branded fleet** — themed bots per game, gold UI, gold embeds, region-routed raid posting.

### Inherited ZE-FusionBot Features

- One-click Game Restart, Hot Reload, and Updater.
- Batch trades via Showdown format or `.zip` archives.
- Mystery Pokémon & Eggs, Battle-Ready, HOME-Ready, and Event trading modules.
- Full GUI control for SysDVR and Switch Remote for PC integration.
- Smart Auto-Correct and Auto-Legalization.
- DM embeds with GIFs, Channel Status notifications, Announcement System.
- Built-in metrics: queue tracking, trade counters, medal system.
- Multi-language request support and live/real-time log searches.

---

## 🖥️ GUI Features

- Gold-themed, hover-responsive panel buttons for Bots / Hub / Logs.
- Custom form-builder configuration UI with drill-down and search.
- Real-time dashboard with timer ring, sparkline, and live trade stats.
- Bot controller with Address, Status, Trade Type, and Log time sections.
- Frameless, minimal, modern design — drag by the top panel.
- Animated glow + progress bar in the controller during trades.

---

## 🖼️ GUI Previews

> 🎬 *PKM Universe gold-UI captures are being recorded — previews below reflect the underlying ZE-FusionBot GUI this fork is built on and will be refreshed.*

<p align="center">
    <img src="https://raw.githubusercontent.com/PokemonLover8888/ZE-FusionBot/main/.readme/README_GeneralOverlook2.gif" style="max-width: 100%; height: auto;">
</p>
<p align="center">
    <img src="https://raw.githubusercontent.com/PokemonLover8888/ZE-FusionBot/main/.readme/README_Environment2.gif" style="max-width: 100%; height: auto;">
</p>
<p align="center">
    <img src="https://raw.githubusercontent.com/PokemonLover8888/ZE-FusionBot/main/.readme/README_Themes2.gif" style="max-width: 100%; height: auto;">
</p>
<p align="center">
    <img src="https://raw.githubusercontent.com/PokemonLover8888/ZE-FusionBot/main/.readme/README_Starting2.gif" style="max-width: 100%; height: auto;">
</p>

---

# 📖 Command Reference

## ⚡ Basic Commands

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `trade` | t | Trade a Pokémon from Showdown Set or PKM file. | `trade <Showdown Format>` or `<upload pkm>` | Everyone |
| `trade true` | t true | Trade a Pokémon from a PKM file, without AutoOT | `trade true <upload pkm>` | Everyone |
| `tradeUser` | tu, tradeOther | Trade the mentioned user the attached file. | `tradeuser @user` | Everyone |
| `hidetrade` | ht | Same as trade, but hides the embed. | `hidetrade <Showdown Format>` | Everyone |
| `clone` | c | Clone the Pokémon you show via Link Trade. | `clone` | Everyone |
| `dump` | d | Dump the Pokémon you show via Link Trade. | `dump` | Everyone |
| `egg` | Egg | Trade an egg via provided Pokémon set. | `egg <Showdown Format>` | Everyone |
| `seed` | checkMySeed, checkSeed, seedCheck, s, sc | Check a Pokémon seed. | `seedCheck` | Everyone |
| `itemTrade` | it, item | Trade a Pokémon holding a requested item. | `it <Leftovers>` | Everyone |
| `fixOT` | fix, f | Fix OT and Nickname of a Pokémon if an advert is detected. | `fixOT` | Everyone |
| `convert` | showdown | Convert a Showdown Set to RegenTemplate. | `convert <set>` | Everyone |
| `legalize` | alm | Attempt to legalize PKM data. | `legalize <pkm>` | Everyone |
| `validate` | lc, check, verify | Verify PKM legality. | `validate <pkm>` | Everyone |
| `verbose` | lcv | Verify PKM legality with verbose output. | `verbose <pkm>` | Everyone |
| `findFrame` | ff, GetFrameData | Prints next shiny frame from seed. | `findFrame <seed>` | Everyone |
| `deleteTradeCode` | dtc | Deletes the stored Link Trade Code for the user. | `dtc` | Everyone |
| `changeTradeCode` | ctc | Change your stored Link Trade Code. | `ctc 12345678` | Everyone |

## 🏠 HOME-Ready Event Library

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `homeReady` | hr | Displays instructions for HOME-Ready trading. | `homeReady` | Everyone |
| `homeReadylist` | hrl, **ll** | Lists available HOME-Ready event files (by event + language + game). | `ll Mewtwo` / `ll 3 Mewtwo` | Everyone |
| `homeReadyRequest` | hrr, **lr** | Request a HOME-Ready file by its list number. | `lr 3` | Everyone |
| `homeReadyView` | hrv | View the Showdown set of a HOME-Ready file. | `hrv 3` | Everyone |
| `homeReadyDownload` | hrd | Download the raw PKM file. | `hrd 3` | Everyone |

> 🔑 **HOME-Ready files keep their original event OT** (e.g. `HOME`, `GF`, `PCNYc`) — AutoOT is intentionally not applied, which is what keeps the HOME tracker legitimate. Use the bot matching your save (SWSH / SV / BDSP / LGPE).

## 🎯 Advanced Trade Features

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `textTrade` | tt, text | Upload a .txt/.csv of Showdown sets for batch trading. | `tt <upload .txt/.csv file>` | Everyone |
| `textView` | tv | View a specific Pokémon from your pending TextTrade file. | `tv 2` | Everyone |
| `listEvents` | le | Lists available event files via DM. | `le <species> <page2>` | Everyone |
| `eventRequest` | er | Downloads event attachments and adds to trade queue. | `eventRequest <file>` | Everyone |
| `battleReadyList` | brl | Lists available battle-ready files via DM. | `brl <species> <page2>` | Everyone |
| `battleReadyRequest` | br, brr | Downloads battle-ready attachments and adds to trade queue. | `battleReadyRequest <file>` | Everyone |
| `pokepaste` | pp, Pokepaste, PP | Generates a team from a PokePaste URL. | `pp <URL>` | Everyone |
| `dittoTrade` | dt, ditto | Trade a Ditto with requested stats, language, and nature. | `dt <LinkCode> <IVToBe0> <Lang> <Nature>` | Everyone |
| `mysteryegg` | me | Get a random shiny 6IV egg. | `mysteryegg` | Everyone |
| `mysterymon` | mm, mystery, surprise | Get a fully random Pokémon. | `mysterymon` | Everyone |
| `randomTeam` | rt, RandomTeam, Rt | Generates a random team. | `randomTeam` | Everyone |
| `specialRequest` | sr, srp | Lists Wondercard events or requests specific ones. | `srp <game> <page2>` | Everyone |
| `getEvent` | ge, gep | Downloads the requested event as a PKM file. | `getEvent <eventID>` | Everyone |

## 📦 Batch Trading

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `batchTrade` | bt | Trade multiple Pokémon (max 6) from a list. | `bt <Set1> --- <Set2>` | Everyone |
| `batchTradeZip` | btz | Trade multiple Pokémon from a ZIP file. | `btz <file.zip>` | Everyone |
| `batchInfo` | bei | Get info about a batch property. | `batchInfo <prop>` | Everyone |
| `batchValidate` | bev | Validate a batch property. | `batchValidate <prop>` | Everyone |

## 📊 Queue Management

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `queueMode` | qm | Change queue control (manual/threshold/interval). | `qm manual` | Everyone |
| `queueClearAll` | qca, tca | Clear all users from all queues. | `qca` | Sudo, Owner |
| `queueClear` | qc, tc | Remove yourself from the queue. | `qc` | Everyone |
| `queueClearUser` | qcu, tcu | Clear a specified user (sudo required). | `qcu @user` | Sudo, Owner |
| `queueStatus` | qs, ts | Check your position in the queue. | `qs` | Everyone |
| `queueToggle` | qt | Enable/disable queue joining. | `qt` | Sudo, Owner |
| `queueList` | ql | DM the full queue list. | `ql` | Sudo, Owner |
| `tradeList` | tl | Show users currently in trade queue. | `tl` | Sudo, Owner |

## 🛠 Admin Tools

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `addSudo` | — | Add a user to global sudo. | `addSudo <ID>` | Owner |
| `removeSudo` | — | Remove a user from global sudo. | `removeSudo <ID>` | Owner |
| `blacklist` | — | Blacklist a Discord user. | `blacklist @user` | Sudo, Owner |
| `unblacklist` | — | Remove a user from blacklist. | `unblacklist @user` | Sudo, Owner |
| `banTrade` | bant | Ban a user from trading with reason. | `bant @user <reason>` | Sudo, Owner |
| `blacklistServer` | bls | Adds a server ID to the server blacklist. | `blacklistServer <ID>` | Sudo, Owner |

## 🎮 Switch Control

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `setScreenOn` | screenOn, scrOn | Turn on screen. | `setScreenOn` | Sudo, Owner |
| `setScreenOff` | screenOff, scrOff | Turn off screen. | `setScreenOff` | Sudo, Owner |
| `peek` | repeek | Take and send a screenshot. | `peek` | Sudo, Owner |
| `video` | Video | Record a GIF from the Switch. | `video` | Sudo, Owner |
| `startSysdvr` | dvr, stream | Start SysDVR streaming. | `startSysdvr` | Owner |
| `startController` | controller, sbr | Start Switch Remote controller. | `startController` | Owner |

## 📡 Bot Management

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `ping` | — | Ping the bot to check if it's running. | `ping` | Sudo, Owner |
| `help` | — | Show all commands. | `help` | Everyone |
| `info` | about, whoami, owner, bot | Show bot information. | `info` | Everyone |
| `botStart` | — | Start the bot. | `botStart` | Sudo, Owner |
| `botStop` | — | Stop the bot. | `botStop` | Sudo, Owner |
| `botRestart` | — | Restart the bot(s). | `botRestart` | Sudo, Owner |
| `status` | stats | Get the bot environment status. | `status` | Sudo, Owner |
| `kill` | shutdown | Shutdown the bot. | `kill` | Owner |

## 🎲 Misc & Fun

| Command | Aliases | Summary | Example | Permission |
|---------|---------|---------|---------|------------|
| `joke` | lol, insult | Tell a random joke. | `joke` | Everyone |
| `hello` | hi, hey, yo | Say hello to the bot. | `hello` | Everyone |
| `mi` | ml | View personal profile card w/ trainer info. | `myinfo` | Everyone |

## 🧠 Passive Features

- Use filename code like `Great Tusk-Tera(Steel)-03760382.pk9` to auto-set trade code.
- Paste a PKM in chat to receive info + legal formats.
- Thank the bot — it might reply!

---

## 🧭 Slash Command Support

Modern Discord Slash Commands for fast, clean Pokémon creation across all supported games, integrated directly with the legality pipeline and AutoOT logic.

| Slash Command | Game |
|--------------|------|
| `/create-sv` | Scarlet / Violet |
| `/create-swsh` | Sword / Shield |
| `/create-bdsp` | Brilliant Diamond / Shining Pearl |
| `/create-pla` | Legends: Arceus |
| `/create-plza` | Legends: Z-A |
| `/create-lgpe` | Let's Go Pikachu / Eevee |

---

## ⚙️ Bot Functions

### 🧑‍🎓 AutoOT
The bot automatically applies your **trainer information** based on the save file you're currently using.
- Your **OT / TID / SID / OTGender** are applied automatically.
- To keep the trainer info in your own files, attach them with `t true`.
- For Showdown Sets, include the OT/TID/SID you want — AutoOT will then be disabled.

### 🔗 Link Trade Codes
You're assigned a **personal static Link Trade Code** on your first trade.
- Reset it with `dtc` (next trade gives a new random code).
- Customize it with `ctc 12345678` (sets your permanent code).

### 🏅 Medals & Milestones
Every completed trade is tracked, and your **trade count** shows in the embed footer.
- For every **50 trades**, you earn a new medal 🥇.
- Check your medals anytime with the `mi` command.

### 🛡️ Smart Fair-Cooldowns
Cooldowns only apply when a trade actually **completes** on the Switch.
- Format mistakes, illegal sets, blocked species, and cancelled trades **don't** count toward your daily limit.
- A clear "No Cooldown Applied" banner appears on every failed-request embed.

---

## 🔗 PKM Universe Projects

- [**pkm-universe.com**](https://pkm-universe.com) — community hub.
- **PKM Universe Seed Finder** — raid seed search, IV/reward filtering, and live host tracking.
- **PKM Universe Verification** — secure member verification with captcha, risk scoring, and role assignment.

## 🙏 Built On

This fork exists thanks to **[Secludedly's ZE-FusionBot](https://github.com/Secludedly/ZE-FusionBot)** and **[hexbyt3's PokeBot](https://github.com/hexbyt3/PokeBot)**. Please support the upstream projects.
