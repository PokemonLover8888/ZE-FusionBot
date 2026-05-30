# Next Release Queue

Changes accumulated since the last GitHub release. When this list gets meaningful (new
member-facing feature, a bug fix worth calling out, or a few changes worth bundling),
bump `TradeBot.cs` version and cut the release with these as the changelog.

## Current Baseline
- Last release: **v8.0.2** (2026-05-30)
- Last released to all 12 trade-bot EXEs: **v8.0.2** (deployed 2026-05-30)

## Queued for next release

### Improvements
- **Species-aware HOME-uploadable-shiny hint on Z-A Non-Native embeds.** The hint
  now picks the right destination per species + form:
  - **SwSh-only** (Galarian birds form 1, Xerneas, Yveltal, Zeraora WC8) → Celebi-SWSH / Jirachi-SWSH
  - **BDSP-only** (Dialga, Palkia, Phione, Manaphy, Darkrai, Shaymin) → Rayquaza-BDSP / Giratina-BDSP
  - **Both routes work** (K/G/R, Mewtwo, Lugia, Ho-Oh, the Regis, Latias/Latios, Heatran,
    Regigigas, Giratina, Cresselia, Kanto birds form 0, legendary beasts) → all four bots listed
  
  Previous generic hint sent shiny-Darkrai requesters to SwSh bots that can't deliver them,
  and didn't tell shiny-K/G/R requesters that BDSP was also an option.

### Fixes
*(none queued)*

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
