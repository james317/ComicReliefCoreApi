# Monthly shipment processing — the user's real workflow

Captured here specifically so it survives context compaction and new
sessions — a prior version of this app (built 9/12–9/16) was designed around
this workflow without the steps ever being written down anywhere durable,
and by the time a later session was asked about it, nothing of the
conversation that produced it was still readable. Read this file in full
before touching anything shipment-related.

**Status: partial.** Only the steps the user has actually described are
below. Don't invent or assume steps that aren't written here — ask instead,
and add them once confirmed.

## Steps confirmed by the user (9/21... continuing)

1. A box arrives from DCBS.
2. Scan all the issues in that shipment into the CLZ (Comic Book Collector)
   app.
3. Export to CSV — **confirmed 10/8: this export is scoped to just the new
   shipment**, not the whole collection (so "Does 'export the collection'
   mean the whole library or a shipment-scoped subset?" below is answered:
   it's shipment-scoped). Real header from the user's own export:
   `Series,Issue,"Variant Description",Publisher,"Cover Date","Storage Box",Title,"Added Date","Release Date"`.
   Verified against the real parser (`ClzCsvParser.ParsePerIssueRows`,
   confirmed by porting its exact rules and running them against the
   user's actual file): all rows parsed cleanly, multiple covers of one
   issue (e.g. two different Vampirella #6 variants) correctly collapse to
   one row. The CSV's own "Added Date" column happened to read today's
   date for every row on this export (everything scanned in one sitting) -
   worth noting only because it's what made the date-based assumption feel
   plausible; confirmed (again) that column still isn't read by the parser
   at all.
4. Upload via the **shipment-scoped** import on the Shipments tab
   (`/api/clz/import-shipment-issues`) - see the mechanics below for why
   this (not the Pull List upload) is the correct one.
5. *(not yet confirmed: what happens after upload - does the user pick the
   shipment and read in release-date order immediately, or something else
   first? what does "done" look like?)*

## How the app actually behaves at step 4 — verified against the real code 9/21

**The user's own assumption going in was wrong, and it's worth stating
plainly so it doesn't get re-assumed later:** uploading a CLZ CSV does
**not** make the app figure out "this is the new shipment" from any
date-added/date-created field in CLZ. `ClzCsvParser` (`src/ComicReliefCoreApi.Api/Services/Clz/ClzCsvParser.cs`)
never even reads such a column — every row in whatever file is uploaded is
parsed, full stop.

Here's what's actually true instead:

- **Shipment membership comes entirely from DCBS, not from CLZ.**
  `ShipmentTrackingService.GetRecentShipmentsAsync` scrapes
  `/account/shipments` live, on every page load — no sync button, no upload,
  nothing CLZ-related needed for a shipment to show up. If the box has
  shipped, DCBS already has the record, and the Shipments tab will show it.
- **The CLZ CSV's only job is to supply real per-issue release dates**, so
  the app can group a shipment's items into actual reading order. Matching
  is by **(series name, issue number)** against whatever's in the
  `ClzIssueReleases` table (`ShipmentTrackingService.GetReadingOrderAsync`,
  `TitleNormalizer.IsLikelySeriesMatch`) — never by when that CSV was
  uploaded, and never by anything resembling "today."
- Because matching is per-issue, it doesn't matter whether the uploaded CSV
  is scoped to just this shipment or is the user's entire collection export
  — either one populates the same lookup table, and an issue gets its
  release date the moment *any* upload has included it.

### The two upload endpoints — use the shipment-scoped one for a new box

- `POST /api/clz/import` — **full collection import.** Wholesale *replaces*
  the per-series "last purchased" snapshot (`ClzCollectionService`/
  `ClzSeriesSummary`). A real incident this app already hit once: uploading
  a shipment-scoped (partial) export through this endpoint wiped the full
  collection snapshot down to just that shipment's issues. Never use this
  one for a single box.
- `POST /api/clz/import-shipment-issues` — **shipment-scoped import.**
  Merges into the per-issue release-date table (`ClzIssueReleases`) only,
  and never touches the full-collection snapshot. Safe to re-run. **This is
  the one to use after a new shipment arrives**, whether the CSV is scoped
  to just the box or is a full export — upload UI for it is on
  `pull-list.html` (full import) vs. `shipments.html` (shipment-scoped,
  look for "Just did a CLZ export of one shipment's issues instead of your
  whole collection? Upload that on the Shipments tab, not here.").

### The actual correct sequence for a just-arrived box

1. Scan the shipment's issues into CLZ (user does this outside the app).
2. Export to CSV from CLZ (whole collection or just this shipment —
   either works).
3. Open the **Shipments** tab in the app. The new shipment should already
   be listed (DCBS already has the record; nothing to sync first).
4. Upload the CSV via the **shipment-scoped** import control on the
   Shipments page (`/api/clz/import-shipment-issues`) — not the one on
   Pull List.
5. Pick the shipment in the list; the app shows its items grouped by real
   release date (reading order), cross-referencing what was just uploaded.
6. Per the Shipments page's own existing copy: once everything's scanned
   in, sync order history (Candidates tab) so a missed-issue check can flag
   anything that should have shown up in this shipment but didn't
   (`IssueContinuityService`/Missed Issues).
7. **Confirmed 10/9 - a step the user had been skipping over when
   describing this process:** that same order sync (`/api/orders/sync-recent`,
   fired from the Shipments, Candidates, *and* Solicitations tabs - all
   three call the same endpoint) also runs
   `PullListService.DetectAndTrackNewFirstIssuesAsync`. For every real
   issue #1 in a newly-synced order, it checks the extracted series title
   against a one-shot/special heuristic (`one-shot`/`special` in the
   title). If it matches, the app deliberately does **not** try adding it
   to DCBS's real sticky pull list - it's tracked as `Unresolved`
   ("Just Rode In" on the Pull List tab) with the note "Looks like a
   one-shot/special from its title - left untracked rather than
   auto-added." The step: after syncing, check the Pull List tab's **Just
   Rode In** group, and for anything that's actually an ongoing series
   (not a true one-shot), hit the **"Try adding to Pull List"** button on
   its card - that runs it through the same `AddToPullListAsync` DCBS-add
   attempt the heuristic skipped, resolving it to Sticky (added for real)
   or Still Wanted (didn't take) same as any other add.
   **Deliberately no bulk "try all" here** - unlike Still Wanted's
   one-click "Retry all" (`/api/pulllist/retry-unsticky`), where every
   entry is already believed to be a real series. Most of what lands in
   Just Rode In genuinely are one-shots/specials the heuristic correctly
   caught; a "try all" would just churn through a wall of expected
   failures. Per-card is the per-title judgment call this step actually
   needs.

## Open questions — ask the user, don't guess

- What happens physically before scanning into CLZ (unboxing against a
  packing list? sorting by pull-list priority? something else)?
- What does the user consider "done" for a shipment — all items read? All
  items shelved? Something else?
- Anything the user does with the missed-issue check results once they see
  them?

Update this file as these get answered, in the same direct, first-person-
verified style as above — not as a guess.
