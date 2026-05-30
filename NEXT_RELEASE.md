# Next Release Queue

Changes accumulated since the last GitHub release. When this list gets meaningful (new
member-facing feature, a bug fix worth calling out, or a few changes worth bundling),
bump `TradeBot.cs` version and cut the release with these as the changelog.

## Current Baseline
- Last release: **v8.0.0** (commit `c75d47c`, 2026-05-28)
- Last released to all 12 trade-bot EXEs: **v8.0.0 + d5686d6** (deployed 2026-05-29)

## Queued for next release

### Improvements
- **`d5686d6` — HOME-compatible-shiny hint on Z-A Non-Native trade embeds**  
  When a Z-A bot ships a non-PA9 shiny (Kyogre/Groudon/Rayquaza/Zeraora etc.), the
  embed now appends *"For a HOME-uploadable shiny, request from Celebi-SWSH or
  Jirachi-SWSH instead."* Members get the in-game shiny they wanted and know exactly
  where to go if they want it HOME-uploadable.

### Fixes
*(none queued)*

### Internal / chore
*(none queued)*

---

## How to ship
1. Bump `SysBot.Pokemon\Helpers\TradeBot.cs` version constant (`v8.0.0` → next)
2. Commit version bump
3. Publish self-contained single-file EXE (`dotnet publish SysBot.Pokemon.WinForms/...`)
4. Deploy to all bot folders via `Desktop\Deploy-2026-05-29\deploy.ps1` + `restart.ps1`
   (or the equivalent for that release date)
5. `git tag v8.0.x && git push --tags`
6. Cut GitHub release on the tag, paste this file's "Queued" section as release notes
7. Empty this file's "Queued" sections, update the baseline
