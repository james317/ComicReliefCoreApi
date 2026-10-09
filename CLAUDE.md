# ComicReliefCoreApi / "If You Pull, Don't Miss"

ASP.NET Core backend + static web app for tracking a DCBS (Discount Comic
Book Service) comic-subscription pull list. Deployed at
https://if-you-pull-dont-miss.fly.dev, auto-deployed via
`.github/workflows/fly-deploy.yml` on every push to `master` — develop and
push directly to `master`, no PRs needed for this project's own workflow.

**Read these before starting work, not just when the topic comes up —
context gets compacted or dropped between sessions, and these are the parts
that don't survive that on their own:**

- `docs/BACKLOG.md` — running engineering log: real findings about DCBS's
  own site behavior/bugs, architectural decisions, and gaps. Long; search it
  rather than reading start to end.
- `docs/SHIPMENT-PROCESS.md` — the user's real, physical monthly
  shipment-processing workflow (CLZ scan → export → upload → reading order),
  as they've actually described it, plus how the app's shipment-matching
  code genuinely behaves (verified against source, not assumed). Keep this
  updated the moment the user describes a new or corrected step - don't
  let it go stale the way it did before this file existed.
- `docs/ORDERING-PROCESS.md` — the separate monthly process of building and
  placing *next* month's order (catalogs → DCBS auto-cart-from-pull-list →
  variant-cover swaps → cart glance-through → #1s check → checkout →
  pull-list touch-up) - don't confuse this with SHIPMENT-PROCESS.md, which
  is about a shipment that already arrived. Also explains why the app
  can't push to DCBS's cart itself (session-routing, not a missing
  feature) - see that file before re-deriving or re-litigating this.

After any `dotnet build`-worthy change: build before committing, push to
`master`, then verify the change live (curl the deployed app / API) rather
than trusting the deploy succeeded silently.
