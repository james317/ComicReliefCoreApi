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

**Live end-to-end test (8/30/2026):** added *Vampirella vs Darkstalkers
Special #1 Cvr E* (product code `AUG2670894`, internal `productId`
`907470`) to the open August order via `POST /ajax/AddToCart`, confirmed
it landed in both `/cart` and `/account/order/982026` immediately
(subtotal moved to $127.60), removed it via `GET /cart/delete/{productId}/page/1?returnRoute=/Cart`,
and confirmed the order was back to exactly its original 39 lines and
$123.71 subtotal. This resolves the first two open questions below and
gives a real success-response shape to code against:

```
POST /ajax/AddToCart
Body: productId=907470&quantity=1
-> 200 {"count":1,"subtotal":"$3.89","products":[{"ProductCode":"AUG2670894","Title":"...","Qty":1,"Url":"/product/..."}]}

GET /cart/delete/907470/page/1?returnRoute=/Cart
-> 200, item gone from both /cart and the order immediately
```

Note the success shape has no `error` key at all — a script should
treat `error` present-and-non-empty as failure and anything else
(a `count`/`products` body) as success, rather than keying off HTTP
status, which is 200 either way.

**Resolved:** `/cart` is not a separate staging area — `/ajax/AddToCart`
writes directly into whichever order is currently open for edits, and
`/cart` is just a live view of that same order. The removal link
(`.deletecartitem`, confirmed real markup) is
`GET /cart/delete/{productId}/page/1?returnRoute=/Cart` — a plain GET,
same as `editorder`, no antiforgery token needed.

**Still open before this becomes a real script:**
- The exact rule for when a button renders `cartbuttoninactive instock`
  vs a normal active `cartbutton` — not yet observed on a real item;
  every item checked so far (a stale relisted back issue, a true
  in-stock-section item, and a normal current solicitation) all
  rendered as active `cartbutton`.
- Rate limits / bot detection: DCBS's ToS almost certainly doesn't
  contemplate scripted cart mutations. Any real implementation should
  throttle to human-like pacing (one add at a time, real delays) rather
  than firing requests in parallel.
- Session lifetime: the `.ASPXAUTH` cookie used for this investigation
  will expire eventually (forms-auth tickets are time-limited) — a real
  implementation needs either a re-login flow or an accepted manual
  cookie-refresh step each time it's used.

### The persistent pull list is a *separate* mechanism from the cart/order

Everything above is about adding an item to one month's order. That's a
nice feature, but it's not what actually matters long-term: DCBS has a
**separate, persistent pull list** (`/account/pulllist`) that it uses to
auto-populate every future month's draft order by cross-referencing new
solicitations against titles on that list. Keeping *that* list accurate
means DCBS does the month-to-month reconciliation work itself — our app
only has to handle the residual titles DCBS's own pull list won't hold
onto. This is a materially different and more valuable target than
scripting cart adds.

**Ground truth confirmed (8/31/2026):** fetched `/account/pulllist`
directly (227 real entries, each `{seriesTitle, qty, id}`) and diffed it
against every title in the 8/29 order. Once titles are normalized (DCBS
internally strips "The" and apostrophes — its list has "Twilight Zone"
and "X-Men 97", not "The Twilight Zone" / "X-Men '97"), this ground
truth **matches our local `docs/pull-list.csv` "Unsticky" categorization
almost exactly** — strong validation that the local list's manual
sticky/unsticky calls have been accurate.

**Adding a new series to the persistent list — two different mechanisms found:**

1. `/account/pulllist` page's own search-and-add flow:
   `POST /ajax/PullListSearch` with `{search: <term>}` returns a results
   table of `{seriescode, seriestitle, currentIssueText}` rows (HTML
   fragment, not JSON). Search is a plain substring/word match, not
   fuzzy — e.g. "American Caper" and "Rocketeer" return nothing even
   though the multi-word "The" search returns 105 unrelated rows. Then
   `POST /ajax/AddPullListItem` with `{seriesCode, qtyToAdd, title}` ->
   `{"success":true}` or `{"success":false,"errorMessage":"..."}`.
   Removal is `POST /ajax/DeletePullListItem` with `{id: <plid>}` ->
   `{"success":true}`.

2. The order page's own embedded form,
   `POST /Account/UpdatePullListFromOrder/{orderId}`, with one
   `pulllistqty`+`productcode` field pair per order line (positional,
   not indexed — same repeated-field-name pattern as the cart form).
   This references items by their per-issue `AUG...` product code
   instead of the series-level code, so it may route through different
   server logic. **Not yet confirmed working** — see below.

**Confirmed genuinely-not-addable series (the real "why can't I" answer
for these, independent of any bug):** American Caper, The Rocketeer,
Batman/Superman: World's Finest, and the Vampirella vs Darkstalkers
one-shot have **no series record at all** in DCBS's pull-list search —
searched under every reasonable term (full title, first word, last
word). Also not findable, and not expected to be — none are real
per-issue "series": Marvel Previews, DC Connect, Dark Horse Monthly
Catalog, IDW Monthly Title Catalog (free catalogs), and Cult-De-Sac
(tried multiple hyphen/spacing variants, still nothing). These titles
must stay on our own external unsticky list indefinitely (or until DCBS
adds them to its own catalog) — no amount of retrying will make them
stick, because there's nothing there to stick to.

**Confirmed live server bug (8/31/2026), and the real explanation for
"the add button sometimes seems disabled":** searched and found real
series records for 5 order titles not yet on the pull list — Doom
Patrol (`761941398242`), Filthy Lambs (`601961405479`), Crowbound
(`709853045861`), Black Tower: The Raven Conspiracy (`761941390802`),
and Pathfinder Vampirella (`725130367389`, stored as "Pathfinder /
Vampirella: Blade of Darknes"). All 5 use DCBS's newer 12-13 digit
GTIN/UPC-style series codes. Attempting `AddPullListItem` with any of
them returned a raw, unhandled **HTTP 500** — not DCBS's normal graceful
JSON failure shape. To isolate the cause, the same call was retried with
a short, legacy-style series code (`149028`, 6 digits) picked from an
unrelated search result, purely as a technical probe — it succeeded
cleanly (`{"success":true}`), and was immediately removed again via
`DeletePullListItem` to leave no trace. That strongly points to a
32-bit-integer overflow server-side: **the long codes are too large for
an `Int32`**, and the server throws instead of validating and returning
a friendly error. Critically, the page's own JS `error:` callback on
this endpoint only does `console.log` — never an `alert()` — so a real
user clicking "Add" on one of these newer-catalog series sees **no
feedback of any kind**, success or failure. That almost certainly *is*
the "sometimes the add button seems disabled" experience: the button
works, the click fires, the server silently 500s, and nothing visibly
happens either way.

**Not yet tried:** whether route 2 above (`UpdatePullListFromOrder`,
keyed by product code rather than series code) avoids the same overflow
for these 5 titles — it may hit different server-side code that handles
the conversion correctly. Recommended immediate next step: the user
tries this manually in their own browser (order page's "Pull List"
column, type `1` for the 5 titles above, click "Update Pull List") since
it's the fastest way to learn the answer, and it isn't a scripted action
so it sidesteps the question of repeated automated attempts entirely.
If it works by hand, that confirms route 2 as the one worth scripting
for a real implementation (and DCBS support would be the right channel
to report the `AddPullListItem` 500 as a bug). If it *also* silently
fails, the overflow is more fundamental (e.g. baked into how the order
page resolves product code to series code) and these 5 titles would need
to stay manually tracked on our unsticky list too, at least until DCBS
fixes it server-side.

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
