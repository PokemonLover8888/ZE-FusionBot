# Next Release Queue

Changes accumulated since the last GitHub release. When this list gets meaningful (new
member-facing feature, a bug fix worth calling out, or a few changes worth bundling),
bump `TradeBot.cs` version and cut the release with these as the changelog.

## Current Baseline
- Last release: **v8.0.8** (2026-06-26)
- Last released to all 12 trade-bot EXEs: behavior live; footers update on next redeploy

## Queued for next release

### Improvements
*(none queued)*

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
7. Attach the built `PKM-Universe.Bot.exe` to the release via `gh release upload`
8. Empty this file's "Queued" sections, update the baseline

---

## v8.0.8 changelog (2026-06-26)

### Improvements
- **DM buttons now actually work:** "Re-queue" re-submits a canceled trade automatically, and "Trade Again" re-submits the same Pokémon — no retyping.
- Completed trades now show action buttons (Trade Again / Dismiss) in the DM.
- The "Trade Complete" DM now shows **your real OT** instead of the generation default.
- Clearer "mistakes are free" wording so it can't be misread as getting a cooldown.
- When a requested level is too low for the chosen moves, the bot now tells you why and ships the lowest legal level.

### Fixes
- **Legends Z-A native fix:** Z-A-catchable Pokémon (Scizor, Absol, Ceruledge, Froakie, etc.) now arrive native / HOME-able instead of false "Non-Native" — shiny and non-shiny.
- **Scarlet/Violet native fix:** Rowlet/Dartrix, Kyurem, etc. now arrive from their native SV encounter instead of a GO/Max Lair transfer (no more false "Non-Native").
- **BDSP native fix:** Darkrai, Rayquaza, etc. now arrive from their native BDSP encounter instead of an event/transfer (no more false "Non-Native").
- **Shiny Zeraora** now ships the correct event (met location "a lovely place") instead of an illegal native shiny; shiny vs non-shiny Zeraora no longer swapped.
- **Cooldown:** typing a wrong Pokémon name / illegal set no longer counts against the web rate limit — mistakes are truly free.
- **IVs:** your requested IV spread is honored whenever it's legal (Marks seed-lock IVs, so a Mark + custom IVs still can't coexist — game limitation).

### Internal / chore
- Generalized direct-NativeOnly generation helper (`GetLegalNativeDirect`) powering the Z-A/SV/BDSP native safety nets.
