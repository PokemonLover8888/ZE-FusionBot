# Next Release Queue

Changes accumulated since the last GitHub release. When this list gets meaningful (new
member-facing feature, a bug fix worth calling out, or a few changes worth bundling),
bump `TradeBot.cs` version and cut the release with these as the changelog.

## Current Baseline
- Last release: **v8.0.1** (2026-05-30)
- Last released to all 12 trade-bot EXEs: **v8.0.1** (deployed 2026-05-30)

## Queued for next release

### Improvements
*(none queued)*

### Fixes
- **Stop blocking shiny Mewtwo/Heatran/Darkrai/Xerneas/Yveltal on Z-A bots.** Yesterday's
  un-block was partial — only K/G/R/Z came out of `IsHomeRejectingShinyZALegendary`,
  leaving the other five Z-A shiny-locked legendaries still erroring out at request time.
  They're in the same boat: bot can ship the file for in-game use, the embed's
  Non-Native warning points to Celebi-SWSH for HOME upload. Now they all behave
  consistently. The list is fully empty.

### Internal / chore
*(none queued)*

---

## How to ship
1. Bump `SysBot.Pokemon\Helpers\TradeBot.cs` version constant
2. Commit version bump
3. Publish self-contained single-file EXE (`dotnet publish SysBot.Pokemon.WinForms/...`)
4. Deploy to all bot folders via `Desktop\Deploy-2026-05-29\deploy.ps1` + `restart.ps1`
5. `git tag v8.0.x && git push --tags`
6. Cut GitHub release on the tag, paste this file's "Queued" section as release notes
7. Empty this file's "Queued" sections, update the baseline
