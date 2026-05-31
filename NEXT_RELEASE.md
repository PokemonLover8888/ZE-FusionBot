# Next Release Queue

Changes accumulated since the last GitHub release. When this list gets meaningful (new
member-facing feature, a bug fix worth calling out, or a few changes worth bundling),
bump `TradeBot.cs` version and cut the release with these as the changelog.

## Current Baseline
- Last release: **v8.0.3** (2026-05-30)
- Last released to all 12 trade-bot EXEs: **v8.0.3** (deployed 2026-05-30)

## Queued for next release

### Improvements
- **HOME-Ready module: `lr` / `ll` aliases + revamped UX.** `$lr` (legendary request) and `$ll` (legendary list) added as short aliases for `$hrr` / `$hrl`. `$ll`'s parser now accepts `ll 3 Mewtwo` (page-first) as documented, in addition to legacy `ll Mewtwo 3`. List rows show event + language + game at a glance (`1. Mewtwo • FEB2012 • ENG • SWSH`) instead of raw filenames. `$hr` instructions redesigned as a single gold rich-embed with thumbnail, file count, and command cards.
- **`SendTradeErrorEmbedAsync`: fair-cooldown reassurance.** Every failed-request embed now includes a 🛡️ "No Cooldown Applied" field telling members that format mistakes / illegal mons / cancelled trades don't burn their daily slot. The bot already gated cooldowns on real `TradeFinished` completion via the trade-bridge — this just surfaces the existing protection to members.

### Fixes
- **`GetHomeUploadRoute` default → no hint (`""`).** Z-A bot Non-Native embeds were appending a "go to Celebi-SWSH" hint for *every* unlisted species, including regular Charmander / Pikachu / etc. Now only the explicitly-classified shiny-locked legendaries see the redirect; common Pokémon get the plain Non-Native notice with no misleading bot recommendation.
- **`DetailsExtractor` move-emoji placeholder filter.** Some legacy configs shipped with literal `"?"` or `"??"` strings as `CustomTypeEmojis`/`UsePlusMoveEmoji.EmojiString`, which Discord rendered as `?` next to every move. Added `IsUsableEmojiCode` to ignore all-question-mark strings so the Unicode fallback (🔥💧⚡🌿…) kicks in. Per-bot config files have also been cleaned (separately, not in source control).
- **Mesprit BDSP met location.** PKHeX's BDSP encounter table lists Mesprit at met location 197 (Valley Windworks), which is geographically wrong — Mesprit's lore-accurate location is Lake Verity. Post-ALM override on PB8 species 481 now patches loc 197 → 325 (Lake Verity / Verity Cavern). Uxie (Lake Acuity, loc 331) and Azelf (Lake Valor, loc 328) were already correct.

---

## How to ship
1. Bump `SysBot.Pokemon\Helpers\TradeBot.cs` version constant
2. Commit version bump
3. Publish self-contained single-file EXE (`dotnet publish SysBot.Pokemon.WinForms/...`)
4. Deploy to all bot folders via `Desktop\Deploy-2026-05-29\deploy.ps1` + `restart.ps1`
5. `git tag v8.0.x && git push --tags`
6. Cut GitHub release on the tag, paste this file's "Queued" section as release notes
7. Empty this file's "Queued" sections, update the baseline
