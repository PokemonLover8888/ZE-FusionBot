# PKM Universe — Differentiators & Roadmap

What makes PKM Universe *yours* isn't the SysBot base (everyone shares that) — it's the
**platform, the HOME-legit quality bar, and the community/business** you've built on top. This is
the moat, where it lives, and where to take it.

> **The one-line thesis:** Nexus and the others ship *a bot*. You're building *a platform*. Lean
> into that gap — website, HOME-safe quality, and monetized community — because that's the part a
> fork can't copy by pulling your commits.

---

## The moat (what you have that a fork doesn't)

### 1. The website + live trade platform  🏆 *biggest moat*
`creator.pkm-universe.com` — webtrades, live bot monitor, trainer tools, HOME vault, pokedex.
A competitor with a bot has a Discord command; you have a **web app** members log into.
- **Lives in:** `ericscyndaquil/` (website), `trade-bridge-api.js` (bot↔web bridge, port 3456),
  `public/trade-hub-ultimate.html` (webtrade dispatch), `public/bot-monitor.html` (fleet monitor).
- **Roadmap:**
  - *Near:* one-click "re-run this trade," per-user trade history, live queue position on the site.
  - *Bigger:* a logged-in **member dashboard** (their trades, their HOME vault, their Premium status)
    — turn the site into the place members *live*, not just a trade button.

### 2. HOME-legit quality bar  🏆 *your reputation moat*
Most bots ship HOME-*illegal* mons that get flagged. Yours don't — that's a genuine, defensible edge.
- **Lives in:** native safety nets (`AutoLegalityWrapper.GetLegalNativeDirect`, per-game rebuilds),
  HOME-tracker preservation (AutoOT never strips), the form-sprite correctness, held-item restore,
  shiny-lock gating.
- **Roadmap:**
  - *Near:* a public **"HOME-Safe Guarantee"** page explaining *why* your mons pass HOME when others
    don't — market the quality you already engineered.
  - *Bigger:* an automated **pre-ship HOME-legality gate** that blocks any trade that wouldn't pass
    HOME, with a clear member message. Make "it always enters HOME" a promise, not a hope.

### 3. HOME tooling & the living dex
verify / legalize / gendex tools, the full shiny living dex, the HOME-Ready event library.
- **Lives in:** `home-verify/`, `home-legalize/`, `home-gendex/`, `C:\PKM-Work\National-ShinyDex\`.
- **Roadmap:** finish depositing the shiny living dex → offer **"complete living dex"** and
  **"HOME-verified collections"** as premium products. Nobody else can hand a member a HOME-tracked
  full shiny dex.

### 4. Monetized community
Ko-fi tiers (Elite Raid Bot Access $7, Elite SWSH Raid Access $11), auto role assignment, 1,100
members in months, TikTok reach.
- **Lives in:** Ko-fi + Discord role automation, the elite/premium gating in the bot
  (`RoleCanBatch` etc.), `memberships.html`.
- **Roadmap:**
  - *Near:* surface tiers **in the web app** (upgrade CTA where members already are), not just Discord.
  - *Bigger:* a **"what you unlock" matrix** page + annual plans; tie Premium to the web dashboard
    (vault size, priority queue, exclusive collections).

### 5. The raid ecosystem
SV Tera Raid bots (Paldea/Kitakami/Blueberry) + SWSH auto-rotating den bot — request exact shiny
Tera raids on demand, seed lookup at `seeds.pkm-universe.com`.
- **Roadmap:** a **web raid request form** (pick species/Tera/stars → queues the raid) mirroring the
  trade dispatcher; a public raid schedule/"what's hosting now" widget.

### 6. Polish that compounds
Move-type gem emojis, batch announce-once, steady-Online presence, the wondercard loader, the
custom GUI. Individually small; together they read as "premium product," not "hobby fork."

---

## Where to focus (honest priorities)

1. **Ship the member web dashboard.** Your single biggest un-copyable advantage is the platform.
   Every hour here widens the gap a fork can't close.
2. **Productize HOME-legit.** You already do the hard engineering; now *market* it and *guarantee* it.
   That's the trust that converts free users to paying ones.
3. **Move monetization into the web app.** Tiers currently live in Discord/Ko-fi; put the upgrade
   path where members spend time.
4. **Keep the bot boring and rock-solid.** The bot is table stakes — reliability (like the presence
   fix, the tunnel http2 fix) protects the brand. Don't chase bot features Nexus has; win on platform.

## What NOT to spend time on
- **Rewriting the SysBot core** — shared AGPL work; you'd lose legality accuracy for nothing.
- **Matching every rival bot feature** (Kook, their AI, etc.) — that's competing on their turf. Your
  turf is the platform + community + HOME quality.
- **AI showdown-gen as a headline feature** — fine to have, but it's commoditized; don't let it
  distract from the dashboard.

---

## Reality check on the competition (Nexus / others)
They have: a slick reskinned GUI, GitHub CI, auto-MGDB/ALM updates, an AI showdown generator, Kook
support, strong uptime. That's a *good bot*. What they **don't** have is your website platform,
your HOME-legit reputation, your paying membership base, or your community reach. Compete where you
already lead — don't chase them into a bot-feature arms race.
