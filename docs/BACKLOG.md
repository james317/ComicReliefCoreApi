# Backlog

This tracks features that have been prototyped conversationally (built as
one-off scripts and self-contained HTML reports, not as real app code) plus
bugs found along the way, so future implementation work doesn't have to be
re-derived from scratch. Update this file whenever a new feature request
comes in or a bug gets fixed.

## Naming: keep "comic-relief" scoped to backend code, not the public app

Deliberate split, confirmed 8/31/2026: `ComicReliefCoreApi` (repo name,
`.csproj`/`.sln`, every C# namespace) is the backend's own internal
identity - it should never become the name a user sees or the public
container/deployment identity. `"If You Pull, Don't Miss"` is the actual
product name (PWA manifest `name`/`short_name`, browser tab title,
splash screen).

The one place this took a real fix rather than just being descriptive:
`fly.toml`'s `app` name doubles as the live container name *and* the
default URL (`{app}.fly.dev`), and it was `comic-relief-api` - meaning
the backend's internal name was leaking into the public-facing
deployment identity. Renamed to `if-you-pull-dont-miss` so the
container/URL carries the product name instead. The GitHub Actions
workflow reads the app name from `fly.toml` dynamically, so it needed no
change; README.md's Fly CLI examples were updated to match.

**Sharper split, 8/31/2026:** the user's own framing is "comic-relief
api has no business logic - just backend operations for DCBS and the
data layer; the app is 'If You Pull, Don't Miss', contains all business
logic, and heavily uses the comic-relief api." That's a real code-layer
distinction, not just a display-name one, and the pull-list feature
initially violated it: `PullListService` (the search-then-fall-back-to-
order-form-then-verify *algorithm*) was sitting in the same project as
`DcbsClient` (pure DCBS operations, no decisions). Split into three
projects, still one deployed process/container (one Fly app, one
Docker image - see the question below about why not two services):
- `ComicReliefCoreApi.Api` - "comic-relief api" proper. `DcbsClient`,
  `ComicReliefDbContext`, the `PullListEntry`/`PullListAddAttempt`
  models, `DcbsOptions`. No decisions get made in this project - it
  only executes operations it's told to.
- `ComicReliefCoreApi.App` - "If You Pull, Don't Miss" business logic.
  `PullListService` (the actual algorithm) and `TitleNormalizer`
  (title-matching is a judgment call, not a raw operation). References
  `.Api`, never the other way around.
- `ComicReliefCoreApi` (unchanged name) - the one web host: `Program.cs`,
  `Controllers/`, `wwwroot/`. Wires up both layers via DI but shouldn't
  itself accumulate business logic or DCBS/data code as new features
  land - new decision-making goes in `.App`, new raw operations in
  `.Api`.

Chose one process over two genuinely separate deployed services
(separate Fly apps talking over HTTP) deliberately: this is a
single-user hobby project, and a real network hop between "api" and
"app" would add cost, latency, and a second thing that can
independently break, for a separation that's really about code
organization, not scaling or independent deployability. Revisit only if
a real reason to deploy them separately shows up later.

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

**Resolved (8/31/2026): route 2 works.** The user tried the manual
workaround — typing `1` into the order page's "Pull List" column for
all 5 titles and clicking "Update Pull List" — and got no visible
feedback either way (confirming this page also gives zero UI
confirmation on success, same silence problem as route 1's missing
`alert()`). Re-fetching `/account/pulllist` directly settled it: entry
count went from 227 to 232, with fresh distinct plids for all 5 —
`Doom Patrol` (707339), `Filthy Lambs` (707338), `Crowbound` (707340),
`Black Tower The Raven Conspiracy` (707341), and `Pathfinder /
Vampirella: Blade of Darknes` (707337). **So `UpdatePullListFromOrder`
correctly resolves product-code-to-series internally and sidesteps
whatever throws on the long codes in `AddPullListItem`.** This is the
route worth building into a real implementation — `AddPullListItem`
should be avoided entirely for any series using a long code, or at
minimum wrapped in a fallback to this order-form path on a 500. Also
worth reporting the raw 500 to DCBS support separately, independent of
our own workaround, since a real user hand-clicking "Add" on one of
these series gets nothing and no explanation.

Updated `docs/pull-list.csv`: all 5 moved from `Unsticky` to `Sticky`,
each annotated with its new plid. Filthy Lambs carries an extra note —
it's a 5-issue limited series, so it should be manually removed from
the DCBS pull list once #5 ships rather than sitting there indefinitely
with nothing left to solicit.

**Swept the rest of the Unsticky list (8/31/2026)** — tried every
remaining unsticky title the same way, split by whether it was actually
a line item in order 982026:

*In the order, tried via `UpdatePullListFromOrder`:* DC Connect, Marvel
Previews, IDW Monthly Title Catalog, World's Finest, The Rocketeer,
American Caper, Cult-De-Sac. **None stuck** — pull-list count stayed at
232 after submission (previous 5 additions confirmed still intact, so
the submission itself is safe/idempotent for unrelated rows). This is a
stronger result than the earlier search-based failure: since the
order-form route resolves a product code to its series entity through
different server logic than `AddPullListItem`/`PullListSearch` and
*still* found nothing to attach a pull-list entry to, these titles have
no backing series record in DCBS's system at all — not a search-index
gap, not a code-format bug. They're permanently unaddable via any
mechanism found so far and have to stay on manual unsticky tracking.
(Denver was deliberately skipped — its series just finished with #3 of
3, so there'd be nothing left to auto-solicit even if it could be added.)

*Not in the order at all* (so no product-code line to submit):
Warhammer, Sisterhood, Vampirella Archives, John Le Carré's The Circus,
Deluge, Die!Namite, Vampirella vs. Red Sonja Red City. Checked the only
route available to them — `PullListSearch` — under several term
variants each; none returned a relevant match (a few returned unrelated
Vampirella hits, most returned nothing at all). Consistent with all of
them being flagged "never seen in 4+ months of orders" — they appear to
have dropped out of DCBS's currently-solicited catalog entirely, which
would explain why neither the sticky pull list nor its search can find
them. Same verdict: unaddable right now, manual tracking only, revisit
if one of them turns up newly solicited again.

**Net result:** of the 16 non-complete unsticky titles that existed
before this session, 5 are now genuinely sticky on DCBS's own pull list
(no longer our problem to track month-to-month) and 11 are confirmed,
not just assumed, permanently unaddable — either no series record
exists at all, or the title hasn't been solicited recently enough to
appear anywhere in DCBS's own systems. `docs/pull-list.csv` reflects
per-title findings so this doesn't need re-investigating later.

**Pruned (8/31/2026):** removed `The Nice House By The Sea` (#12 of 12)
and `Denver` (#3 of 3) from `docs/pull-list.csv` entirely — both series
finished, so there's nothing left to track or solicit and no reason to
keep them on the unsticky list going forward.

**Found and removed a duplicate entry:** `Vampirella Archives` (no
"TP") had been sitting on the unsticky list, flagged dormant — but
DCBS's general catalog search for "Vampirella Archives" turns up
exactly one product line, `Vampirella Archives TP` (currently Vol. 08,
the one in the 8/29 order), which is already `Sticky`. Per the user:
this was very likely their own earlier attempt to add the series to
sticky that failed for some reason, followed by manually noting an
incomplete-named version on the external unsticky list rather than
retrying. Removed the duplicate row — the real series is already
correctly tracked under its sticky entry.

**`Sisterhood` confirmed and removed (8/31/2026):** per the user, this
is the same series as the sticky `Sisterhood A Hyde Street Story` —
another incomplete-name duplicate from the same original mistake
pattern. Removed from `docs/pull-list.csv`.

**`Vampirella vs. Red Sonja Red City` confirmed and removed
(8/31/2026)** — the user didn't remember either way, and DCBS's search
couldn't settle it since both names are dormant. Resolved with a
different technique: grepped every past order (all 20, back to
909945) for "red sonja". Found exactly one hit across the user's entire
order history — `Vampirella vs Red Sonja Red City #1` (product code
`JUN264649`, ordered 6/16/2026). No un-subtitled "Vampirella vs Red
Sonja" purchase exists anywhere. Combined with DCBS's established habit
of truncating subtitles on the persistent pull-list display (same
pattern as "Twilight Zone" and "X-Men 97" above), this is conclusive:
the sticky `Vampirella vs Red Sonja` entry (plid `645469`) *is* this
Red City series, just displayed short. Removed the duplicate.

**General technique worth keeping:** when a live DCBS search can't
disambiguate two similar names because both are currently dormant,
grep the full order history for the distinguishing word(s) instead —
whatever the user actually bought last is definitive, and DCBS's
persistent-list display can't be trusted for anything more than a
simplified/truncated series name.

### Full order-history census (8/31/2026)

The original reconciliation (start of this session's work) was scoped
to "Apr-Jul 2026" order history, plus this session's own sweep of the
August order — 5 of the account's 20 total orders. Order history
actually goes back to **1/29/2025** (order `909945`), roughly monthly,
so 15 months (Jan 2025-Mar 2026) had never been checked against the
pull list at all. Prompted by the user asking whether this was worth
doing, fetched all 20 orders (694 line items total), extracted a
series name per item (stripping issue numbers, cover variants, format
suffixes), and diffed against every title in `docs/pull-list.csv`
(normalized, with containment matching to absorb "The"/apostrophe/
subtitle differences).

Of 139 distinct series across the full 20-order history, only 6
recurring (2+ orders) titles didn't match anything on file:
- `Nice House By The Sea`, `Denver` — expected; both were deliberately
  pruned this session for being complete.
- `Solomon Kane The Serpent Ring`, `The Last Day(s) of H.P. Lovecraft`
  — false positives from imperfect title-matching; both already
  tracked (as `Solomon Kane Serpent Ring` and `The Last Days of Hp
  Lovecraft`, both Sticky).
- `Zatanna` — a real gap, but the series completed at #6 of 6 back in
  May 2025 (over a year ago). Nothing to add or fix, just confirms it
  fell through the cracks of documentation at the time.
- **`Devil On My Shoulder`** — the one real, actionable finding. Ran
  #1-4 (Aug-Dec 2025) with no "of N" ever printed, then nothing since.
  **Resolved (8/31/2026):** fetched the full solicitation text for #4
  (`DEC254055`) directly — *"Join us for the final issue of Devil on My
  Shoulder... A new dark horror **four issue series**... Series
  finale!"* Always planned as a 4-issue mini by Kyle Starks and Piotr
  Kowalski; it concluded exactly on schedule. Not a cancellation or a
  quiet drop — the "of 4" just never made it into the cover-listing
  title, which is why the "of N" heuristic missed it. Pruned from
  `docs/pull-list.csv` entirely, same as Nice House By The Sea and
  Denver — nothing left to track.

**Conclusion for the user's question:** a full historical re-audit was
worth doing once, but turned up only one real, actionable gap out of
~20 months and 694 line items — the existing Apr-Aug monthly-sweep
habit (check each new order as it's placed, the way August was handled)
is sufficient going forward. No standing need to re-run this full
20-order census again; a fresh order needs checking against the list
once, not the whole history re-checked every time.

Needs the user's own memory/judgment to resolve, the same way they
settled the Vampirella Archives case — DCBS's own data doesn't
disambiguate either of these right now.

### Old (already-shipped) orders support pull-list adds too

Tested whether `UpdatePullListFromOrder` only works on the currently
"open for additions" order, or on any past order. **It works on old,
already-filled orders too** — the same form is present on order
975866 (July 2026, order date 7/12/2026, long since shipped), and
submitting it worked exactly like the current order.

Test case: the user only ever buys **Gatchaman in TP** (trade
paperback), never single issues. `PullListSearch` for "Gatchaman" only
turns up one series (`150840`), tied to the single-issue periodical
(`Gatchaman #22`); searching "Gatchaman TP" directly returns nothing at
all. Adding the single-issue series would have been wrong — it would
have started auto-soliciting individual issues the user doesn't want.

Submitted the July order's own TP line item (`JUL2671008`, Gatchaman TP
Vol 04) through `UpdatePullListFromOrder` instead, as a reversible
probe. **It landed as a distinct `"Gatchaman TP"` pull-list entry**
(plid `707346`) — a different series from the single-issue one search
had found. So the order-form route is more format-aware than the
search box: it resolves a specific purchased product to its correct
underlying series (TP vs. periodical), even when that series isn't
independently searchable by name. Kept rather than removed, since it
appears to correctly track the format the user actually buys — moved
from `Seen in orders only` to `Sticky` in `docs/pull-list.csv`.

**Implication for a real implementation:** when a title is bought in a
non-single-issue format (TP, HC, etc.), prefer resolving its series via
a specific already-purchased product's product code (through
`UpdatePullListFromOrder`, current or past order) over the generic
`PullListSearch` + `AddPullListItem` flow — the latter may only expose
the single-issue series even when a format-specific one exists
server-side.

## Shipped in the app

- **Upcoming comics feed** — `GET /api/comics/upcoming`, backed by
  `ComicVineService`. Given a year/month (defaults to today + 2 months),
  paginates the Comic Vine `/issues/` endpoint filtered by `store_date` and
  returns title/issue number/store date/cover/detail URL.
- PWA shell (splash screen, manifest, icons, western theme) for the iPhone
  Home Screen install.
- **Reliable pull-list add** — `POST /api/pulllist/add {title}` /
  `GET /api/pulllist`. The first real app implementation of the DCBS
  reverse-engineering below, rather than a scratchpad script: given a
  title, searches DCBS's pull-list index and tries adding it directly,
  skips straight to the order-form fallback when the series code matches
  the known long-code overflow pattern, and only ever declares success
  after re-fetching the real `/account/pulllist` to confirm it actually
  stuck. Falls back to a specific, stored `FailureReason` (not a generic
  error) when neither route works. `ComicReliefCoreApi.Api/Services/Dcbs/DcbsClient.cs`
  is the reusable HTTP layer; `ComicReliefCoreApi.App/Services/PullListService.cs`
  is the workflow (see the naming section above for why they're in
  separate projects); `PullListEntry`/`PullListAddAttempt` (SQLite via EF Core, persisted on a
  Fly volume — see README.md's "One-time setup for the pull-list feature")
  replace `docs/pull-list.csv` as the live source of truth going forward.
  Written without a local .NET SDK available in this session — the Docker
  build (which does have the full SDK) is the first real compile check, so
  treat this as unverified until a build actually runs.

  **First real build attempt (8/31/2026, via GitHub Codespaces) caught a
  real bug**, confirming that caution was warranted: `PullListService.cs`
  used `ILogger<PullListService>` with no `using Microsoft.Extensions.Logging;`
  and no package reference for it. It compiled fine as written because
  everything started life inside the one `Microsoft.NET.Sdk.Web` project,
  which implicitly imports that namespace *and* gets an implicit
  `FrameworkReference` to the whole `Microsoft.AspNetCore.App` shared
  framework for free. Neither applies to `ComicReliefCoreApi.Api` or
  `ComicReliefCoreApi.App` (plain `Microsoft.NET.Sdk` class libraries,
  correctly, since they aren't web projects) - the split surfaced a gap
  the original single-project build had been silently covering for.
  Fixed by adding the explicit `using` plus an explicit
  `Microsoft.Extensions.Logging.Abstractions` package reference on
  `.App`, and (proactively, same reasoning, though this one built fine as-is)
  an explicit `Microsoft.Extensions.Options` reference on `.Api` for its
  `IOptions<DcbsOptions>` usage - don't rely on a `Microsoft.Extensions.*`
  type arriving incidentally via another package's transitive dependency
  chain in a plain class library; reference it directly.

  **Confirmed fixed (8/31/2026):** `dotnet build` succeeded in the
  Codespace after pulling the fix. The pull-list feature (three-project
  split included) is now verified to actually compile, not just
  reviewed by eye - first real confirmation since it was written.

  General lesson for future class-library splits in this repo: audit
  every file for implicit-usings-covered types once it leaves a
  `Microsoft.NET.Sdk.Web` project - the base implicit usings
  (`System`, `System.Collections.Generic`, `System.Linq`,
  `System.Net.Http`, `System.Threading`, `System.Threading.Tasks`) are
  shared by every SDK type, but `Microsoft.Extensions.Logging`,
  `.Configuration`, `.DependencyInjection`, `.Hosting`, and the
  `Microsoft.AspNetCore.*` namespaces are Web-SDK-only.

- **Runtime-settable DCBS session cookie** — `GET /api/dcbs-session/status`,
  `POST /api/dcbs-session`, `POST /api/dcbs-session/revalidate`, backed by
  `wwwroot/dcbs-session.html`. The DCBS cookie used to only be settable via
  the `Dcbs__SessionCookie` Fly secret (config-time, needs a redeploy to
  change) - since it's a forms-auth ticket with a real expiry that needs
  refreshing periodically, that was real ongoing friction. Now it's stored
  in a single-row `DcbsSession` table and can be pasted in from a small
  page at any time. `DcbsClient` reads the cookie fresh from
  `IDcbsSessionStore` (`.Api`) on every request rather than fixing it at
  startup, so a new paste takes effect immediately with no restart.
  `IsSessionValidAsync` (`.Api` - a raw operation, just reports whether
  DCBS redirected to its login page) backs `DcbsSessionManager` (`.App` -
  the actual judgment call of "is this cookie any good", used both right
  after a paste and on demand via a "Check Current Cookie" button).
  `DcbsOptions.SessionCookie` and the `Dcbs__SessionCookie` secret are
  superseded by this — `DcbsOptions` now only holds `BaseUrl`.

  **How to actually get a cookie (documented here since `dcbs-session.html`
  promises this file has it, and it didn't yet):** log into dcbservice.com
  in a normal browser tab, open DevTools (F12 - identical steps in any
  Chromium browser: Edge, Chrome, Brave), go to the **Network** tab,
  reload the page, click any request to dcbservice.com in the list, find
  **Cookie:** under **Request Headers** on the right, and copy that whole
  value (a long `name=value; name2=value2; ...` string, not just one
  cookie). Paste the entire thing into `/dcbs-session.html`. Don't use a
  browser's Application/Storage tab for this - it lists cookies
  individually, and hand-reconstructing the full header string from that
  is error-prone; the Network tab's Cookie header is the exact string the
  browser actually sent. The auth cookie itself is `.ASPXAUTH` (see
  "Session lifetime" above) but the app needs the full header, not just
  that one name/value pair.

- **Pull List UI + cross-app nav + workflow-driven theming pass (8/31/2026)** —
  the pull-list backend had shipped with no UI beyond a raw API, and the
  three pages (`index.html`, the new one, `dcbs-session.html`) had no way
  to navigate between them. Decisions, tied to the user's stated 8-step
  monthly ordering cycle (catalogs → DCBS auto-cart-from-pull-list →
  variant-cover swaps → cart glance-through → #1s check → checkout →
  pull-list touch-up):
  - **`wwwroot/pull-list.html` + `pull-list.js`** — steps 1 and 8 (dogear
    from catalogs and add to sticky; catch anything new at the end) had no
    surface at all before this. Add-a-title form posts to the existing
    `POST /api/pulllist/add`; results group into three lists matching
    `PullListStatus`: **Corralled** (`Sticky` — actually confirmed stuck on
    DCBS), **Still Wanted** (`Unsticky` — shown with its stored
    `FailureReason` so the user knows what to do by hand during a catalog
    pass instead of just seeing "failed"), and **Just Rode In**
    (`Unresolved` — added but not attempted yet).
  - **Cross-reference badge on the solicitations feed** (`index.html` /
    `app.js`) — badges any solicited title already tracked on the pull list
    ("On Your List"). Directly serves steps 1–3 (recognizing a followed
    title while skimming catalogs/solicitations, before deciding whether a
    variant cover is worth chasing). **Corrected same day**: this first
    shipped with a client-side copy of `TitleNormalizer`'s matching rules
    in `app.js`, which the user correctly flagged as a business-logic leak
    into the UI layer - a future change to DCBS's naming quirks would've
    had to be updated in two places, in two languages, and could silently
    drift out of sync. Fixed by moving the decision server-side:
    `IPullListService.GetTrackedNormalizedTitlesAsync()` (`.App`) exposes
    normalized tracked titles, `ComicsController` (web host) combines them
    with `TitleNormalizer.Normalize()` (the same single source of truth
    `PullListService` itself uses) to set a new `ComicIssue.OnPullList`
    bool per comic, and `app.js` now just reads that field - no matching
    logic in the UI at all. General rule going forward: the UI layer reads
    `IPullListService`/`IDcbsClient`-shaped data through controllers and
    renders it; any comparison, matching, or status decision belongs in
    `.App` (or `.Api` for raw DCBS facts), never reimplemented in JS.
  - **Shared `.trail-nav`** — a sticky three-tab bar (Solicitations / Pull
    List / DCBS Session) added to all three pages; previously each page was
    an island with no link to the others.
  - **De-duplicated component CSS** — `dcbs-session.html` had its own
    embedded `<style>` block for buttons/textarea/status-card/message that
    only that page could use. Promoted into `styles.css` as shared classes
    so `pull-list.html` (and anything added later) gets the same button/
    card/badge language for free instead of re-declaring it per page.
  - **`Program.cs`**: added `JsonStringEnumConverter` to the controllers'
    JSON options, so `PullListStatus`/`PullListFormat`/`PullListAddMethod`
    serialize as `"Sticky"`/`"Unsticky"`/etc. instead of raw integers — the
    new pages read these directly in JS to decide which group/badge an
    entry belongs in, and a magic number would've been unreadable and
    fragile against enum reordering.
  - Kept explicit "gunfighter" copy (Corralled/Still Wanted/Just Rode In,
    "Telegraph Office" for the session page) light-touch rather than
    pervasive — the existing splash/masthead/Rye-font/flame-flicker theming
    already carries most of the visual identity; new copy just extends the
    same voice without turning functional status text into a puzzle.
  - Not yet build-verified locally (no local .NET SDK in this session, same
    situation as the pull-list feature itself) — the change to `Program.cs`
    is small (one `using` + one `.AddJsonOptions()` call) but should still
    go through the same Codespace `dotnet build` check before relying on it.

- **Archived titles + "Boot Hill" view (8/31/2026)** — after the CSV
  import landed 232 real entries, several Sticky titles were long-finished
  series (confirmed via explicit "Series complete at #X of X" notes from
  this session's order-history research, not guessed from general comics
  knowledge - deliberately excluded `Altered States: Warlords`, whose note
  only says "likely concluded... or on hiatus"). `PullListEntry.ArchivedAt`
  (nullable) hides a title from the default `/pull-list.html` view without
  touching DCBS at all - archiving is purely a display decision.
  `POST /api/pulllist/{id}/archive` / `/unarchive`; `GET /api/pulllist`
  defaults to `archived=false`, `?archived=true` shows only archived ones
  (rendered as "Boot Hill" - `pull-list.html?archived=true`, linked from
  the main page).
  **Real schema-evolution gotcha hit here:** `Database.EnsureCreated()`
  (used throughout since no `dotnet-ef` tooling exists in this session)
  only builds a schema for a brand-new database - it does **not** apply
  incremental changes to one that already has data, which the production
  SQLite file on the Fly volume now does (the 232 imported entries).
  Adding `ArchivedAt` to the C# model alone would've compiled fine and
  then thrown "no such column" against the live database. Fixed with an
  idempotent hand-written `ALTER TABLE PullListEntries ADD COLUMN
  ArchivedAt TEXT NULL` in `Program.cs`, guarded by catching SQLite's
  duplicate-column error so it's safe to run on every startup. **General
  rule going forward: any new column on an existing table needs this
  treatment (or a real tracked migration) - editing the EF model is not
  enough once production has real data.**

- **CLZ collection import, refreshable via upload (8/31/2026)** — the user
  wants a "last purchased" ship-date signal per pull-list title as an
  archiving aid, sourced from their CLZ (Comic Book Collector) export CSV rather
  than DCBS itself. DCBS was considered first but rejected as a source for
  this specific feature: it's an ordering catalog, not a comics database,
  and nothing in this session's DCBS reverse-engineering confirms it
  exposes a clean per-series issue-history-with-dates fact anywhere
  (`SearchSeriesAsync`'s `CurrentIssueText` is the closest candidate, but
  untested and blocked anyway - no DCBS session cookie has been set on the
  production deploy at all).
  New table `ClzSeriesSummary` (`.Api`) - one row per series, aggregated
  at import time to `LastReleaseDate` (max across owned issues) and
  `IssueCount`, keyed by `NormalizedSeries` (via the relocated
  `TitleNormalizer` - see below). `ClzCsvParser` (`.Api`) hand-rolls a
  quoted-CSV line reader (real CLZ exports have commas inside quoted
  fields, e.g. "Variant Description") rather than adding a dependency;
  handles the two date formats actually seen ("Jan 17, 2024" and
  year-only "1996" for old back issues). `POST /api/clz/import` (multipart
  file upload) fully replaces the stored snapshot every time - a CLZ
  export is always a complete dump, so "refresh" means replace, not merge.
  `GET /api/clz/status` and the upload control both live in a collapsed
  `<details>` on `pull-list.html` to stay out of the way day-to-day.
  **Matching is exact-normalized-title only, deliberately not fuzzy** -
  this session's own quick substring-match experiment (see the "18+
  months" CLZ discussion above) produced real false positives (`Batman`
  matching an unrelated 2019 `Archie Meets Batman 66` one-shot just from
  word overlap), so an unmatched pull-list title just shows no date
  rather than a wrong one.
  **Labeled "last purchased" everywhere in the UI, never "last
  shipped"** - this is purchase history, not publisher fact. A DCBS
  silently dropping a title from auto-cart (the exact failure this whole
  app exists to catch, confirmed repeatedly this session: Black Tower,
  Doom Patrol, Crowbound, Pathfinder Vampirella) looks identical in this
  data to a series that actually ended. It's meant as a signal for the
  user's own archiving judgment, never an auto-archive trigger.
  **`TitleNormalizer` relocated `.App` → `.Api`** (namespace
  `ComicReliefCoreApi.Api.Services`) so both layers can share the one
  normalization implementation - `.Api`'s CLZ import needs it now too,
  and `.Api` can't depend on `.App` (wrong direction). All callers
  (`PullListService`, `ComicsController`) updated to the new namespace.
  **Second occurrence of the EnsureCreated table-not-column gap:** a
  brand-new table needs the same hand-written-DDL treatment as a new
  column, just `CREATE TABLE IF NOT EXISTS` instead of a caught
  duplicate-column exception - SQLite handles "already exists" natively
  for table/index creation, no try/catch needed there.

- **DCBS `CurrentIssueText` tested live and ruled out as a "last shipped
  issue" source (8/31/2026)** — once a real DCBS session cookie was
  finally set on production, built `GET /api/pulllist/dcbs-search?term=`
  (read-only passthrough to `SearchSeriesAsync`, left in place as a
  harmless diagnostic/future preview tool) and tested it directly:
  - `Torpedo 1972` (one of the 6 titles archived this session as
    confirmed-complete) returned **zero search results** - not even a
    stale entry, it's just gone from DCBS's index.
  - `Batman` returned 17 series-code matches, but the actual flagship
    ongoing monthly series (code `136313`, unambiguously still shipping)
    came back with `currentIssueText: null` - identical in shape to how a
    dead series looks. Only entries with something solicited in the
    *current* catalog cycle (e.g. a tie-in mini's specific issue) had text.
  - `Vampirella` similarly only returned the specific issue currently
    up for order (`Vampirella (2026) #7 ... (AUG2670877)`), not a history.

  **Conclusion: `CurrentIssueText` reflects "what's solicited this
  catalog cycle," not a series' shipping history.** It goes blank for a
  healthy ongoing series between solicitations exactly as it does for a
  series that's permanently over - there's no way to tell the two apart
  from this field. Confirms the reasoning that led to building the CLZ
  import in the first place: DCBS is an ordering catalog with no concept
  of issue history, not a comics database. No DCBS-side alternative to
  the CLZ "last purchased" signal exists; don't re-investigate this
  without new information (e.g. a different DCBS endpoint surfacing).

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
- **Favorite franchise/brand:** Warhammer — any new Warhammer-related
  title from any publisher, not already in the cart. Moved here from the
  pull list 8/31/2026: there's no ongoing "Warhammer" series currently
  solicited (hence its removal from `docs/pull-list.csv`), but the user
  wants to keep watching for new Warhammer-branded comics going forward
  rather than tracking one specific series.
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
  cosmetic, only the numeric ID matters. IDs found via the homepage nav
  (9/2026): dc-comics=1, dark-horse=2, image-comics=3, marvel-comics=4,
  other=6, previews-catalog=34 (magazines/catalogs, not comics — small,
  ~29 items), trading-cards=10, boom-studios=36, idw-publishing=37,
  dynamite-entertainment=38, manga=42, scout-comics=46, vault-comics=47,
  titan-comics=48, archie-comics-publications=49, cinebook=50, drawn-
  quarterly=52, fantagraphics=53, oni-press=54, papercutz=55, udon-
  entertainment=56, viz-media-llc=57, yen-press=58, kodansha-comics=59,
  tokyopop=60, valiant-entertainment=61, seven-seas-entertainment=62,
  twomorrows-publishing=63, calendars=35, specials (no numeric id seen).
- Product pages are `/product/<code>/<any-slug>` — same deal, slug is
  ignored.
- Page size is controlled by a `ProductsPerPage` cookie. **Tested live
  9/2026 (not just read off the UI dropdown):** values well past the
  dropdown's documented 10/20/30/50/100 max are genuinely honored, not
  capped at 100 — `/products/dc-comics/1` returned 35/125/275/325 items
  for cookie values 10/100/250/500 respectively, and `/products/marvel-
  comics/4` returned 125/255 items for 100/500. Both plateaued once
  requesting past the category's actual current inventory (325 for DC,
  255 for Marvel at test time) — increasing the cookie further (1000,
  5000, 99999) returned the same plateaued count, not an error or a
  truncation at a rounder number. So there is no hard 100-item
  server-side cap; a real crawler can set this to something safely large
  (e.g. 1000+) and fetch each publisher category in one request instead
  of paginating.
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
- **No volume/series-generation field anywhere** — checked live 9/2026
  on both a current issue's product page and a facsimile reprint's,
  neither exposes anything like "Volume 3" or a generation number. This
  matters because DCBS's own pull-list matching doesn't distinguish a
  genuinely new issue from a same-month facsimile reprint of an old one
  (e.g. Batman #14 solicited alongside Batman #227 and #423 "Facsimile
  Edition" reprints, all three same month) — the only signal DCBS gives
  for this is the literal phrase "Facsimile Edition" in the title
  itself. Surfaced as `DcbsListingItem.IsFacsimileOrReprint`, computed
  at scrape time same as `IsRelisted`. Nearby text that looked like it
  might also signal a reprint but doesn't: "Anniversary" (e.g. "Kingdom
  Come 30th Anniversary" is a cover-variant theme on an otherwise
  perfectly current issue, not a reprint marker) and bare `(20XX)` year
  mentions (show up on ordinary current-year variant titles too) — both
  ruled out live against the full crawl before shipping this.
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
- A completed series doesn't always print "of N" in its cover-listing
  title — `Devil On My Shoulder` #4 never did, even though it was
  explicitly billed as a 4-issue mini and its final issue's own
  solicitation text said "Series finale!" outright. Detecting
  completion reliably needs to check the solicitation description, not
  just pattern-match the title.

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
