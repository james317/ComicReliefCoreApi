# Backlog

This tracks features that have been prototyped conversationally (built as
one-off scripts and self-contained HTML reports, not as real app code) plus
bugs found along the way, so future implementation work doesn't have to be
re-derived from scratch. Update this file whenever a new feature request
comes in or a bug gets fixed.

## Shipped in the app

- **Upcoming comics feed** — `GET /api/comics/upcoming`, backed by
  `ComicVineService`. Given a year/month (defaults to today + 2 months),
  paginates the Comic Vine `/issues/` endpoint filtered by `store_date` and
  returns title/issue number/store date/cover/detail URL. This is the only
  feature implemented as real, running app code so far.
- PWA shell (splash screen, manifest, icons, western theme) for the iPhone
  Home Screen install.

## Explicit pull list — canonical source of truth

[`docs/pull-list.csv`](./pull-list.csv) is the definitive, persisted list of
every title the user wants tracked, merged from **all sources**:

- DCBS's own "sticky" pull list (titles that auto-carry month to month)
- DCBS's "unsticky" list (titles that don't persist and need re-adding
  each cycle)
- Titles that turned up in DCBS order history but weren't on either list
  (`Seen in orders only`)
- Titles listed on both sticky and unsticky under near-identical names,
  merged into one row (`Sticky + Unsticky (merged)`)

255 titles total. This file must not be treated as a one-off report —
unlike the monthly solicitation reports, it's foundational reference data
and should be edited in place going forward rather than regenerated from
scratch each session.

**Known gap:** this list was built from the first CLZ collection export.
A second, more complete CLZ export was provided afterward (more fields)
but this list has not been re-reconciled against it yet — worth doing
before treating it as fully authoritative.

**Flagged rows needing action** (from the `Notes/Flags` column — 35 rows
carry a flag; most are already resolved or explained below, the rest
still need a decision):

- *Confirmed still-active misses (add to cart now):* **The Rocketeer**
  (Infiltrator! #4, series finale) and **World's Finest** (#56) — both
  verified this session as genuinely solicited in August with no relist
  banner, and both added to `pull-list-matches-v3.html`.
- *Checked and NOT solicited this month at all* (not just "missing from
  cart" — actually absent from the full August catalog): **Odin** and
  **Altered States: Warlords**. Likely finished or on hiatus — verify
  before assuming they'll return.
- *Still need a manual check* (flagged in the original Apr–Jul vs. Aug
  reconciliation, not yet re-verified against a live catalog the way
  Rocketeer/World's Finest were): Avengers, American Caper, Cult-De-Sac,
  Denver, Black Tower: The Raven Conspiracy, Doom Patrol, Crowbound.
- *Series complete — prune or mark finished:* Lady Mechanika: The
  Mechanical Menagerie, Peril of the Brutal Dark, Solomon Kane: The Lion
  Errant, Spy Bunnies, The Cimmerian: Xuthal of the Dusk, Torpedo 1972,
  The Nice House by the Sea.
- *Discontinued — remove:* W0rldtr33 (publisher cancelled it; DCBS issued
  a credit).
- *Never seen in 4 months of orders — verify still wanted:* Sisterhood: A
  Hyde Street Story, Warhammer, John le Carré's The Circus, Deluge,
  Die!Namite (the ongoing, not the "Blood Red" one-shot which did ship),
  Pathfinder Vampirella.
- *One-time/incidental purchases — probably don't need a persistent
  entry:* Dynamite Dispatches, Ps Artbooks Magazine Psycho, Crisis
  Companion TP, Curses TP, Gatchaman TP, Devils Due Presents Lovebunny &
  Mr Hell, Vampirella X Witchblade Special (likely already covered by the
  Vampirella fuzzy-pull rule). Dark Horse Monthly Catalog is the one
  exception in this group — it's a recurring free catalog item ordered
  every month Apr–Aug and probably *should* stay on the list.

A real implementation should turn "flagged rows needing action" into an
actual workflow (a review queue the user clears month to month) instead
of a static notes column.

## Prototyped, not yet real features

Everything below currently exists only as Python scripts + generated HTML
reports in a scratchpad, built fresh each session and sent as downloadable
files. None of it has a backend endpoint, a data store, or a schedule.

### 1. Master pull list reconciliation
Merge the user's DCBS "sticky" list, "unsticky" list (items that don't
persist across months and have to be re-added), and order history into one
master list, flagging titles that were ordered in consecutive prior months
but are missing from the current month's draft cart.

- Needs: persistent storage for sticky/unsticky/order-history data (today
  it's pasted/uploaded PDFs and CSVs, re-parsed each session), a merge step
  for titles that appear on both lists under near-identical names
  (`dual_listed` mapping today), and a "ordered N months running, absent
  this month" detector.

### 2. Pull-list-matches report ("solicited but missing from cart")
Cross-references the reconciled master pull list against the current
month's live DCBS solicitations, and flags each match as a genuine new
solicitation to add vs. a stale/relisted back issue (via DCBS's own
"Instock - Relisted" banner + expected ship date).

- **Known process gap (fixed twice, likely to recur):** the verified
  version of this report was built from a hand-typed list of title
  strings to check, rather than automatically running every flagged
  master-pull-list title against the live catalog. Two genuine misses
  (*The Rocketeer Infiltrator!* #4, *Batman/Superman: World's Finest* #56)
  were dropped this way and only caught because the user asked "haven't I
  been getting X?" A real implementation must not hand-curate this list —
  every flagged title from step 1 must be checked automatically.

### 3. New-issue-#1 tracker
Crawls every non-manga publisher category on DCBS for a given month and
surfaces every new series debut (`#1` or one-shot), grouped by series
across variant covers, with full solicitation text pulled from each
product page.

### 4. Fuzzy pull suggestions
Surfaces items *not* in the cart that match the user's known interests:
- **Favorite writers:** James Tynion IV, Garth Ennis, Cullen Bunn, Jeff
  Lemire, Joe R. Lansdale, Alan Moore. Determined empirically from the
  CLZ collection export. **Explicitly excludes DC/Marvel and any
  character/franchise-driven purchase** as evidence — the user buys those
  by character or team, not writer, so it would give false credit.
- **Favorite character:** Vampirella — any new Vampirella item not
  already in the cart, regardless of writer.
- **Favorite imprint:** Vertigo — any active Vertigo-imprint solicitation
  not already in the cart.
- Today this matching is done by Claude reading solicitation text
  conversationally, not by a repeatable algorithm. A real implementation
  would need either a maintained writer/character/imprint ruleset applied
  programmatically, or an LLM-judge call (see below) with a fixed prompt
  per item.

### 5. Content notes
Per-title note surfaced only for titles that mention LGBTQ organizations
or themes/characters in their solicitation text, for the user's personal
content curation (stated reason: religious). Framed as a neutral
"content note," not a warning label, and requires confirmation from 2+
independent web sources before it's labeled "confirmed" (single-source
hits are labeled as such rather than asserted as fact).

- Needs: a real web-search step per flagged title (currently done
  manually per report), and a persistent decision on how many sources
  count as "confirmed" — 2+ was the standard set for this pass.

### 6. "Search for reviews of this comic book" link (stopgap)
Every card in the fuzzy-pulls/new-#1s report links to a Google search for
`"<exact title>" comic book review`, opened in a new tab.

- **Deferred real feature:** a dynamic prompt that fetches and summarizes
  actual review content on click. Not feasible as a static file — no
  place to hold an API key safely, and no live fetch from a downloaded
  HTML file. Would need either (a) a real backend endpoint that takes a
  title, searches, and summarizes server-side, or (b) publishing the
  report as a Claude Artifact with the "ask Claude" runtime capability
  instead of a static download.

## Report delivery format (applies to all of the above until they're real app features)
- Self-contained HTML, all cover images embedded as base64 data URIs —
  iOS's Quick Look previewer sandboxes local files and blocks remote
  image loads regardless of server-side hotlink permissions, so remote
  `<img src>` silently breaks in the Files app preview.
- Styled to match the app's western theme (parchment/leather/brass,
  price/notice badges, etc.) so a future real UI has a starting visual
  language.
- Built in scratchpad, copied into the repo only long enough to send via
  file delivery, then deleted — these are not meant to be committed
  artifacts.

## DCBS scraping gotchas (encode these into any real scraper)
- Category pages are `/products/<any-slug>/<numeric-id>` — the slug is
  cosmetic, only the numeric ID matters.
- Product pages are `/product/<code>/<any-slug>` — same deal, slug is
  ignored.
- Page size is controlled by a `ProductsPerPage` cookie (10/20/30/50/100,
  default 10) — set it to 100 to avoid unnecessary pagination.
- `/search/<term>` does full-text matching across titles, creators, *and*
  descriptions, not just titles — prone to false positives. It also
  renders an unrelated "featured items" carousel before the real results;
  the real results live inside `<ul class="thumblist">`. Any regex
  scoped to "first match on the page" instead of "first match inside
  `thumblist`" will silently grab the wrong item. Hit this bug twice.
- Full solicitation descriptions only exist on product pages
  (`<div class="detaildatacol"><p>...</p>`) — listing pages truncate.
- Relisted/back-issue status: `<div class="instock notice">` or
  `<div class="relist notice">` banner text plus an "Expected Ship Date"
  — a past date means genuinely stale, a future date is just
  informational (e.g. a real upcoming reprint).
- Writer field: `<li>Writer:</li>` on the product page. Price:
  `<li class="dcbsprice">DCBS Price: $X.XX</li>`.
- Cover images: `https://media.dcbservice.com/{small|xlarge}/{CODE}.jpg`
  — no hotlink protection, but iOS Quick Look still needs them
  base64-embedded (see above).
- Series-name matching must normalize before comparing (strip
  punctuation, compare word sets) — a literal substring match on
  `"Dune House Corrino"` will never match the actual stored title
  `"Dune: House Corrino"` because of the colon. Caused one confirmed
  false "not in your collection" claim to the user.
- Pagination should stop when a page returns zero regex matches, not on
  a page-size heuristic (a "page has <90 items" guess was too fragile).

## Open questions for a real implementation
- Where does pull-list/order-history/CLZ data live persistently, and how
  does it get updated (re-upload each month vs. an integration)?
- Does fuzzy-pull matching become a fixed rule engine, or a per-item LLM
  judge call (Haiku 4.5 via the Claude API) — and if the latter, what's
  the cost/latency budget for a ~2000-item monthly crawl?
- Does this surface inside the PWA itself (requires a backend + scheduled
  scrape job) or stay a periodically-generated report?
- Reviews feature: static search link (current stopgap) vs. real
  summarization via a backend endpoint or an Artifact with Claude access?
