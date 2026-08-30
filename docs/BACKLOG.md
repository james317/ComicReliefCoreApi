# Backlog

This tracks features that have been prototyped conversationally (built as
one-off scripts and self-contained HTML reports, not as real app code) plus
bugs found along the way, so future implementation work doesn't have to be
re-derived from scratch. Update this file whenever a new feature request
comes in or a bug gets fixed.

## DCBS pull-list automation — reverse-engineered contract

Goal: script "add to pull list" attempts instead of clicking through items
one by one, and understand exactly why an attempt succeeds or fails. This
was reverse-engineered live against the real site (authenticated, using a
session cookie the user extracted from their own browser and shared for
this purpose) rather than guessed. Confirmed mechanics:

**Two separate concepts: "order" vs "cart".** Each month's order (e.g.
`982026` for August 2026) is its own object at `/account/order/{id}`. It
starts locked after the stated due date ("has passed for full editing")
but stays open for *additions only* until next month's catalog goes live.
Only one order can be in this "open" state at a time — opening another
order's edit mode closes the current one. The separate `/cart` page (keyed
by the `DCBSCart` cookie) is a smaller staging concept and read 0 items
even while the order had 39 lines — the relationship between the two
still isn't fully pinned down (see open question below).

**Opening an order for edits:** `GET /order/editorder/{orderId}` (called
by clicking "Edit Order" on the order page) — this is a plain link, not a
form post, and just needs valid auth cookies. It redirects (302) back to
`/account/order/{orderId}`, which then renders "This order is currently
open for additions only."

**Adding an item:** `POST /ajax/AddToCart`, form-encoded body
`productId=<internal id>&quantity=<n>`, JSON response. This is the single
endpoint behind both the big "Add To Cart" button and every small
thumbnail "Add to cart" link on a product page — same payload shape
either way. **No anti-forgery token is required in the body** — despite
the page carrying a `__RequestVerificationToken` cookie, the add-to-cart
JS never reads or sends it, so a valid session cookie alone is
sufficient. This was confirmed by reading the actual minified JS in
`/bundles/app` rather than guessing.

**Critical gotcha:** `productId` is DCBS's own internal numeric ID (e.g.
`879257`), not the public SKU code (e.g. `AUG254492`) used everywhere
else on the site (URLs, order history, our own scraping). It's exposed
as the `data-val` attribute on the button/link element — a scraper must
pull this per-item from each product page; the SKU code alone won't work
as the request parameter.

**Confirmed failure/success signal:** on failure the JSON response
carries an `error` string, which the page surfaces as
`alert("Unable to add item to cart. " + error)`. This means DCBS
generally tells you *why* in plain text — a script should read and log
`response.error` directly rather than trying to infer failure from HTTP
status or a disabled-button heuristic.

**Confirmed hard-block case:** a button rendered with classes
`cartbuttoninactive instock` doesn't even attempt the request — clicking
it just shows a local alert: *"In-stock products can not be added to
existing orders. Please close the order edit to add this item to your
cart."* So genuinely in-stock (ships-now) inventory and preorder
additions can't be mixed while an order is open for edits. Not yet
observed live: every back-issue and in-stock-category item checked this
session (including a stale relisted item and a true in-stock-section
item) still rendered as an active, clickable `cartbutton` — so this
inactive state is narrower than "anything under /instock" and needs
more live examples to characterize precisely.

**Useful side-finding, not part of the add mechanism:** a relisted
back-issue keeps the SKU code from whenever it was *originally*
solicited (e.g. `AUG254492` — the "25" is 2025 — showing up in the
August *2026* catalog with "Expected Ship Date: 10/8/2025"). A SKU whose
embedded year/month doesn't match the current solicitation month is a
reliable, cheap stale/relist detector — arguably more reliable than
scraping the "notice" banner text, and worth using as a second signal in
`pull-list-matches` alongside it.

**Open questions before this becomes a real script:**
- How `/cart` (the `DCBSCart` cookie) and the currently-open `/account/order/{id}`
  actually relate — does `/ajax/AddToCart` write into the open order
  directly, or into the cart with a separate merge step? Needs a live
  add + inspect of both pages to confirm.
- How to remove/undo an add — `/cart` page JS references a
  `.deletecartitem` control and a `/Cart/Update/{productId}?qty={n}`
  quantity-change URL, but no actual delete link was present to inspect
  because the cart currently shows 0 items. Needs to be captured during
  a live add test.
- The exact rule for when a button renders `cartbuttoninactive instock`
  vs a normal active `cartbutton` — not yet observed on a real item.
- Rate limits / bot detection: DCBS's ToS almost certainly doesn't
  contemplate scripted cart mutations. Any real implementation should
  throttle to human-like pacing (one add at a time, real delays) rather
  than firing requests in parallel.

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

258 titles total. This file must not be treated as a one-off report —
unlike the monthly solicitation reports, it's foundational reference data
and should be edited in place going forward rather than regenerated from
scratch each session.

**Known gap:** this list was built from the first CLZ collection export.
A second, more complete CLZ export was provided afterward (more fields)
but this list has not been re-reconciled against it yet — worth doing
before treating it as fully authoritative.

**Validated against the actual 8/29/2026 final order:** every one of the
8 "Add — confirmed" items from `pull-list-matches-v3.html` (American
Caper, Black Tower, Crowbound, Cult-De-Sac, Denver, Doom Patrol, The
Rocketeer, World's Finest) was genuinely in the cart, and all 6 items the
report marked "stale/skip" (Avengers, Dune House Corrino, Arkham Horror,
Racer X, Speed Racer, Hyde Street) were correctly absent — good end-to-end
confirmation that the reconciliation methodology works.

**3 titles found in that order with no entry anywhere on the list —
added:**
- **You'll Never Leave This Place Alive** — new #1 debut (IDW, MR),
  open-ended with no announced issue count. Needs a check next month for
  whether DCBS carries it forward automatically.
- **Filthy Lambs** — new #1 (of 5) debut (IDW), a 5-issue limited series.
- **Vampirella vs Darkstalkers Special** — one-shot; probably doesn't need
  its own persistent entry since the Vampirella character fuzzy-pull rule
  already catches items like this (same pattern as Vampirella X
  Witchblade Special, below).

**Flagged rows needing action** (from the `Notes/Flags` column):

- *Checked and NOT solicited this month at all* (not just "missing from
  cart" — actually absent from the full August catalog): **Odin** and
  **Altered States: Warlords**. Likely finished or on hiatus — verify
  before assuming they'll return.
- *Series complete — prune or mark finished:* Lady Mechanika: The
  Mechanical Menagerie, Peril of the Brutal Dark, Solomon Kane: The Lion
  Errant, Spy Bunnies, The Cimmerian: Xuthal of the Dusk, Torpedo 1972,
  The Nice House by the Sea, and now also Denver (series finale shipped
  in this order).
- *Discontinued — remove:* W0rldtr33 (publisher cancelled it; DCBS issued
  a credit).
- *Never seen in orders — verify still wanted:* Sisterhood: A Hyde Street
  Story, Warhammer, John le Carré's The Circus, Deluge, Die!Namite (the
  ongoing, not the "Blood Red" one-shot which did ship). Pathfinder
  Vampirella no longer belongs in this bucket — its #1 debut shipped in
  the 8/29/2026 order.
- *One-time/incidental purchases — probably don't need a persistent
  entry:* Dynamite Dispatches, Ps Artbooks Magazine Psycho, Crisis
  Companion TP, Curses TP, Gatchaman TP, Devils Due Presents Lovebunny &
  Mr Hell, Vampirella X Witchblade Special, Vampirella vs Darkstalkers
  Special (both likely already covered by the Vampirella fuzzy-pull
  rule). Dark Horse Monthly Catalog is the one exception in this group —
  it's a recurring free catalog item ordered every month and probably
  *should* stay on the list.

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
