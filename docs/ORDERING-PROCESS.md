# Monthly ordering process — the user's real workflow

Captured for the same reason as `docs/SHIPMENT-PROCESS.md`: this is a
distinct recurring process (building/placing *next* month's order) from
that file's subject (processing a *just-arrived* shipment) — don't conflate
the two or merge them into one doc.

**Status: partial**, same convention as `SHIPMENT-PROCESS.md` — only
confirmed steps below; don't invent the rest.

## The user's own 8-step outline (first stated, per `docs/BACKLOG.md`, 8/31/2026)

> catalogs → DCBS auto-cart-from-pull-list → variant-cover swaps → cart
> glance-through → #1s check → checkout → pull-list touch-up

Quoted directly rather than paraphrased, since the exact phrase
"DCBS auto-cart-from-pull-list" is load-bearing — see below.

## Steps confirmed so far

1. **Review publisher catalogs** (Solicitations tab in the app) for new
   titles to add to the pull list. Confirmed live 10/9: a pass can turn up
   nothing new — that's a legitimate outcome, not a sign something's
   broken.
2. **Go to DCBS directly and open the new month's order.** This is *not*
   a workaround for a missing app feature — it's the intended design.
   DCBS's own site auto-populates the draft order/cart by cross-referencing
   the current month's solicitations against the account's pull list —
   that's what "DCBS auto-cart-from-pull-list" means in the user's own
   outline. The app's job is to keep the real sticky pull list
   (`/account/pulllist`) accurate (the Pull List tab's whole purpose).

   **Correction, 10/10**: the paragraph above used to claim this
   cross-reference is purely against the real sticky pull list. That's
   now confirmed wrong — see `docs/BACKLOG.md`'s 10/10/2026 entry. A real
   side-by-side of the same month's order-build page and the real
   `/account/pulllist` page showed four titles (Death Vigil, Monstress,
   That Texas Blood, Die Loaded) matched into "Pull List Matches" that
   are **not** on the sticky list at all. DCBS's matching logic on that
   page is broader than the sticky list alone - possibly also drawing on
   order history (the real pull-list page's own copy mentions adding
   items "directly from your order history" as a separate path) - exact
   mechanism unconfirmed. **Practical effect: don't treat a "Pull List
   Matches" row as proof something is genuinely on the sticky list.**
   Cross-check the real `/account/pulllist` page, or run
   `/api/pulllist/reconcile`, before trusting it.

   **Hard ordering requirement, confirmed 10/9, still holds for the
   sticky-list half of the matching**: DCBS reads whatever actually is
   sticky *at the moment it builds that page* — there's no later
   reconciliation pass for that part. So step 7 of
   `docs/SHIPMENT-PROCESS.md` (hand-adding real ongoing series out of the
   Pull List tab's **Just Rode In** group) still has to be done *before*
   this step for the current cycle, or that series' next issue won't
   appear as a genuine sticky-list match this time around.

   **Confirmed 10/9 from a real printed copy of this page** (DCBS's
   "Create Order from Pull List" page, September 2026 Preorders). Page
   copy reads: "Review the pull list and variant matches below and make
   any needed quantity adjustments. Click the 'Add Pull List to Cart'
   button to add the products to your shopping cart." It has three
   distinct sections, in this order:
   - **"Pull List Matches"** — one row per series on the pull list that
     has a new solicitation this month, **default cover only**, with
     **Qty to Add pre-set to 1**. 43 items this month (Doom Patrol #3,
     Batman #15, Odin #5, several Star Wars trades, Comic Shop News
     issues at $0.50 each, etc.).
   - **"Variants and Incentives"** — every *other* cover of those same
     matched titles (virgin/incentive/connecting variants, 1:10s, 1:25s,
     1:100s...), **Qty to Add pre-set to 0** by default. 99 items this
     month. This is the real mechanism behind "variant-cover swaps" (see
     below).
   - **"Pull List Titles without a Match"** — a plain list (Series
     Title + DCBS's internal Series Code, no prices/quantities) of pull
     list series that have **no** solicitation this month at all. 95
     series this month, and looking at them they're overwhelmingly old,
     completed, or cancelled minis (e.g. Batman: Three Jokers, Dark
     Nights: Death Metal, Darth Maul, Harrow County, Immortal Iron
     Fist) rather than ongoing series just skipping a month. This is the
     real mechanism behind "pull-list touch-up" (see below).
3. **Variant-cover swaps** (confirmed 10/9, from the page structure
   above): for any matched title where the user prefers a variant over
   the default cover, the swap is manually zeroing out "Qty to Add" on
   the default-cover row in "Pull List Matches" and setting it to 1 on
   the preferred row in "Variants and Incentives" — DCBS doesn't guess
   which variant is wanted, it always defaults to the plain cover.
4. *(not yet confirmed: cart glance-through, #1s check, checkout — named
   in the user's own outline above but not walked through in detail yet)*
5. **Pull-list touch-up** (partially confirmed 10/9): the "Pull List
   Titles without a Match" section above is a direct, built-in prompt for
   this step — it's DCBS surfacing every pull-list series that didn't
   generate a match this cycle, by title and series code, specifically so
   stale/completed/cancelled series can be identified and removed from
   the sticky pull list. *(Not yet confirmed: whether the user actually
   uses this section for that purpose today, or does pull-list cleanup
   some other way.)* The other confirmed half of this step: checking the
   Pull List tab's **Just Rode In** group (new issue #1s the app's
   one-shot/special heuristic deliberately left untouched during the
   order sync that happens on checkout) and manually adding anything
   that's actually an ongoing series — see `docs/SHIPMENT-PROCESS.md`
   step 7 for the full mechanism, since the same order-sync endpoint and
   heuristic apply regardless of which tab triggers the sync.

## Why the app can't push to the cart itself — the real architecture

- **The persistent pull list and the cart/order are two separate DCBS
  mechanisms.** The app reliably reads and writes the sticky pull list
  (`/ajax/AddPullListItem`, `/account/pulllist`) and reliably reads real
  placed orders (`/account/order/{id}`) — both confirmed to work
  regardless of which backend instance serves the app's request. The cart
  is different.
- **A one-off scripted cart add was tested and did work** (`docs/BACKLOG.md`,
  8/30/2026 entry): `POST /ajax/AddToCart` with DCBS's internal numeric
  `productId` (not the public SKU) added a real item, confirmed in both
  `/cart` and the open order, then removed cleanly. So cart mutation isn't
  impossible in principle.
- **It was deliberately never turned into a real feature**, for two
  reasons found later:
  - DCBS appears to run multiple backend instances with server-side
    session state - a request from the app's own independently-initiated
    HTTP client isn't guaranteed to land on the same instance as the
    user's live browser session. This is exactly why the cart's "In Cart"
    indicator also reads as empty/unreliable when checked from the app
    (confirmed live earlier this session) even when the user's own
    browser shows items in it.
  - The persistent pull list turned out to be the more valuable target
    anyway: once it's accurate, DCBS's own site does the cart population
    for free every month. There was no real need to also fight the cart's
    session-routing problem on top of that.

## Open questions — ask the user, don't guess

- What's being checked during "cart glance-through" - price, quantity,
  something DCBS might have gotten wrong?
- Does "#1s check" mean cross-referencing against the app's own New #1s
  view (Solicitations tab), or a separate manual pass?
- Does the user actually use the "Pull List Titles without a Match"
  section (see above) to remove stale/completed series from the pull
  list, or handle that cleanup some other way?
- What actually puts a title in "Pull List Matches" when it's not on
  the real sticky list (10/10 finding, `docs/BACKLOG.md`)? Order history
  depth/recency, a stale sticky-list cache, a silently-rolled-forward
  series code from an old relaunch, something else? DCBS gives no way to
  ask directly - would need a deliberate test (e.g. watch what happens
  to a title immediately after manually removing it from the sticky
  list) to pin down.

Update this file as these get answered, in the same direct,
first-person-verified style as above — not as a guess.
