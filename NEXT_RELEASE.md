# Next Release Queue

Changes accumulated since the last GitHub release. When this list gets meaningful (new
member-facing feature, a bug fix worth calling out, or a few changes worth bundling),
bump `TradeBot.cs` version and cut the release with these as the changelog.

## Current Baseline
- Last release: **v8.0.4** (2026-05-31)
- Last released to all 12 trade-bot EXEs: **v8.0.4** (deployed 2026-05-31)

## Queued for next release

### Improvements
*(none queued)*

### Fixes
- **SwSh Keldeo: full 4-move set + no fake HOME tracker.** Two related issues fixed for Celebi-SWSH / Jirachi-SWSH:
  - PKHeX's `EncounterStatic8` Keldeo (Crown Tundra Ballimere Lake / Sword of Justice catch) defines only `Aqua Jet` as its catch-default move. ALM was shipping a level-100 Keldeo with one move and three empty slots. Post-ALM PK8 fix fills slots 2-4 with Sacred Sword / Hydro Pump / Swords Dance (all level-up legal, PKHeX-Valid) when the broken catch-default state is detected.
  - The bot was deliberately injecting a `Random.Shared.NextBytes` HOME tracker into the Mythicals/events list (incl. Keldeo) — fabricated, so HOME would reject on upload, and AutoOT was force-skipped. Added `isSWSHNativeCatch = pkm is PK8 && pkm.MetLocation is > 0 and < 30000` exemption matching the existing BDSP / Z-A native exemptions. SwSh-native fresh catches now ship without a tracker, AutoOT applies normally, member receives Keldeo with their own OT, and HOME assigns a real tracker on first upload from their SwSh save.

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
7. Attach the built `PKM-Universe.Bot.exe` to the release via `gh release upload`
8. Empty this file's "Queued" sections, update the baseline
