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
   the current month's solicitations against the account's real, persistent
   sticky pull list (`/account/pulllist`) — that's what "DCBS
   auto-cart-from-pull-list" means in the user's own outline. The app's
   job is to keep that sticky list accurate (the Pull List tab's whole
   purpose); DCBS does the population itself.
3. *(not yet confirmed: variant-cover swaps, cart glance-through, #1s
   check, checkout, pull-list touch-up — these are named in the user's own
   outline above but haven't been walked through in detail yet)*

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

- What does "variant-cover swaps" involve in practice - swapping a
  default cover for a preferred variant DCBS already added automatically,
  or something else?
- What's being checked during "cart glance-through" - price, quantity,
  something DCBS might have gotten wrong?
- Does "#1s check" mean cross-referencing against the app's own New #1s
  view (Solicitations tab), or a separate manual pass?
- What does "pull-list touch-up" after checkout involve - archiving
  one-shots that shipped, correcting anything the month's experience
  revealed?

Update this file as these get answered, in the same direct,
first-person-verified style as above — not as a guess.
