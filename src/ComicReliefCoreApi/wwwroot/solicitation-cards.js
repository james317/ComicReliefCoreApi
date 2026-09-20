// Shared between candidates.html (pull-list matches) and index.html (full by-publisher
// browse) - both render the same kind of card from the same underlying data shape
// ({ publisher, item: DcbsListingItem }), so the grouping/rendering logic lives here once
// rather than being duplicated per page. Load this script before candidates.js/solicitations.js.

// Primary strategy: everything up to and including the issue number ("#1002", optionally
// with a trailing "(of N)" for minis) is the issue identity - DCBS includes this on every
// single-issue title regardless of publisher. This has to be tried first, not "Cvr X"/
// "Cover X": Marvel's own variant titles often skip that marker entirely (e.g. "Amazing
// Spider-Man #1002 Todd Nauck 4-Part Connecting Legacy Variant" has no "Cvr" anywhere),
// which meant every Marvel variant was getting its own card until this was caught live.
// Falls back to the "Cvr X"/"Cover X" split for one-shots/TPBs with no issue number at
// all (still using that marker there since DCBS does use it for those); with neither, the
// full title is its own single-item group - a group of one renders identically to the old
// one-card-per-item layout, so no special-casing needed.
function extractIssueIdentity(title) {
  const issueMatch = title.match(/^(.*?#\d+(?:\.\d+)?(?:\s*\(of\s*\d+\))?)/i);
  if (issueMatch) {
    return issueMatch[1].trim();
  }
  const coverMatch = title.match(/^(.*?)\s+(cvr|cover)\b/i);
  return coverMatch ? coverMatch[1].trim() : title.trim();
}

// DCBS's listing-page format is consistently "(W) X (A) Y (CA) Z\r\n\r\n<truncated blurb>"
// - a blank-line gap separates the credits line from the solicitation text.
function splitCreatorsAndDescription(text) {
  if (!text) {
    return { credits: null, description: null };
  }
  const parts = text.split(/\r?\n\r?\n/);
  if (parts.length >= 2) {
    return { credits: parts[0].trim(), description: parts.slice(1).join(' ').trim() };
  }
  return { credits: null, description: text.trim() };
}

function groupByIssue(items) {
  const groups = new Map();
  for (const solicitationItem of items) {
    const key = extractIssueIdentity(solicitationItem.item.title).toLowerCase();
    const list = groups.get(key) || [];
    list.push(solicitationItem);
    groups.set(key, list);
  }
  return [...groups.values()];
}

function issueCard(group) {
  const li = document.createElement('li');
  li.className = 'comic-card-wrap';

  const card = document.createElement('div');
  card.className = 'comic-card issue-card';
  if (group.some((g) => g.item.isFacsimileOrReprint)) {
    // Dimmed so a genuinely new issue (e.g. Batman #14) visually stands out at a glance
    // from same-month facsimile/reprint editions of old issues (e.g. Batman #227, #423) -
    // DCBS pull-list matching doesn't distinguish these, so this is the fix for that.
    card.classList.add('facsimile');
  }
  card.dataset.title = group.map((g) => g.item.title.toLowerCase()).join(' ');

  // Hoisted above the cover strip (rather than computed once for the meta line, as
  // before) so each thumbnail can be checked against the group's minimum while building
  // the strip - a variant costing more than the cheapest cover in its own group gets a
  // small corner badge, so a pricier premium/foil/1:25 cover doesn't get picked by
  // accident while skimming thumbnails.
  const prices = group.map((g) => g.item.price).filter((p) => p != null);
  const minPrice = prices.length > 0 ? Math.min(...prices) : null;

  const coverStrip = document.createElement('div');
  coverStrip.className = 'cover-strip';
  for (const solicitationItem of group) {
    const { item } = solicitationItem;
    const a = document.createElement('a');
    a.href = item.productUrl;
    a.target = '_blank';
    a.rel = 'noopener';
    a.title = item.title;
    if (item.thumbnailUrl) {
      const img = document.createElement('img');
      img.className = 'cover-thumb';
      img.src = item.thumbnailUrl;
      img.alt = item.title;
      // Not loading="lazy" - these are only ever created once their <details> group is
      // actually opened (see buildGroupCards), so native lazy-loading has nothing left to
      // defer. It was tried first and dropped: an img that starts inside a collapsed
      // <details> (display:none) never gets a viewport-intersection check in some browsers,
      // so it silently never loads even after the group is opened - this is what building
      // the cards on open (rather than upfront) actually fixes, not just a perf nicety.
      a.appendChild(img);
      if (minPrice != null && item.price != null && item.price > minPrice) {
        const priceBadge = document.createElement('span');
        priceBadge.className = 'cover-price-badge';
        priceBadge.textContent = '$';
        priceBadge.title = `$${item.price.toFixed(2)} (cheapest cover in this group is $${minPrice.toFixed(2)})`;
        a.appendChild(priceBadge);
      }
    } else {
      a.textContent = item.title;
    }
    coverStrip.appendChild(a);
  }
  card.appendChild(coverStrip);

  const info = document.createElement('div');
  info.className = 'comic-info';

  const title = document.createElement('a');
  title.className = 'comic-title';
  title.href = group[0].item.productUrl;
  title.target = '_blank';
  title.rel = 'noopener';
  title.textContent = extractIssueIdentity(group[0].item.title);
  info.appendChild(title);

  // Credits/description come from the listing page (already scraped into
  // CreatorsAndDescription, just never displayed until now) - "(W) X (A) Y (CA) Z\r\n\r\n
  // <truncated blurb>". Only the cover-artist credit actually varies between a group's
  // variants (the story and its writer/artist don't), so showing group[0]'s is
  // representative enough - not worth reconciling per-variant differences here.
  const { credits, description } = splitCreatorsAndDescription(group[0].item.creatorsAndDescription);
  if (credits) {
    const creditsEl = document.createElement('div');
    creditsEl.className = 'comic-credits';
    creditsEl.textContent = credits;
    info.appendChild(creditsEl);
  }
  if (description) {
    const descriptionEl = document.createElement('div');
    descriptionEl.className = 'comic-description';
    descriptionEl.textContent = description;
    info.appendChild(descriptionEl);
  }

  const metaParts = [group[0].publisher];
  if (group.length > 1) {
    metaParts.push(`${group.length} covers`);
  }
  if (prices.length > 0) {
    const max = Math.max(...prices);
    metaParts.push(minPrice === max ? `$${minPrice.toFixed(2)}` : `$${minPrice.toFixed(2)}–$${max.toFixed(2)}`);
  }
  if (group.some((g) => g.item.isRelisted)) {
    metaParts.push('Relisted');
  }
  if (group.some((g) => g.item.isFacsimileOrReprint)) {
    metaParts.push('Facsimile/Reprint');
  }
  const meta = document.createElement('div');
  meta.className = 'comic-meta';
  meta.textContent = metaParts.join(' · ');
  info.appendChild(meta);

  // Its own element (not folded into the meta line) since this is the one thing on the
  // card actually worth acting on - "in your order" means any variant in the group
  // matched, since you only ever order one cover of a given issue, not every variant DCBS
  // solicits, so requiring the exact variant shown here would flag everything as missing.
  // Both states get an explicit element (not just a warning when absent) - a silent card
  // reads the same whether it's confirmed ordered or just hasn't been checked yet, which
  // defeats the point of syncing order history in the first place. Shown on every card,
  // not just pull-list matches (candidates.js) - "did I already order this" is just as
  // useful while skimming the full by-publisher browse (index.html) for something new.
  const orderStatus = document.createElement('div');
  if (group.some((g) => g.isInLatestOrder)) {
    orderStatus.className = 'comic-order-confirmed';
    orderStatus.textContent = '✓ In your order';
  } else {
    orderStatus.className = 'comic-order-alert';
    orderStatus.textContent = 'Not in your last order';
  }
  info.appendChild(orderStatus);

  // One-click "try to get this onto the pull list" - sticky first, falling back to
  // unsticky, exactly what AddToPullListAsync already does for a manually-typed title on
  // pull-list.html. Passes the raw listing title (issue number/cover/"(MR)" and all) -
  // the backend (IssueNumberParser.TryExtractSeriesTitle) strips it down to a bare series
  // name, so this never has to duplicate that parsing here. Harmless to click on
  // something already tracked - AddToPullListAsync just re-confirms it.
  const addBtn = document.createElement('button');
  addBtn.type = 'button';
  addBtn.className = 'secondary comic-add-to-pulllist';
  addBtn.textContent = '+ Add to Pull List';
  addBtn.addEventListener('click', async () => {
    addBtn.disabled = true;
    addBtn.textContent = 'Adding…';
    try {
      const res = await fetch('/api/pulllist/add-from-listing', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ listingTitle: group[0].item.title }),
      });
      if (!res.ok) throw new Error(`Request failed (${res.status})`);
      const entry = await res.json();
      if (entry.status === 'Sticky') {
        addBtn.textContent = '✓ Sticky';
        addBtn.className = 'secondary comic-add-to-pulllist result-sticky';
      } else if (entry.status === 'Unsticky') {
        addBtn.textContent = 'Unsticky';
        addBtn.className = 'secondary comic-add-to-pulllist result-unsticky';
        addBtn.title = entry.failureReason || 'Could not confirm sticky on DCBS.';
      } else {
        addBtn.textContent = 'Added';
        addBtn.className = 'secondary comic-add-to-pulllist result-unresolved';
      }
    } catch (err) {
      console.error('[solicitation-cards] add-to-pull-list failed', err);
      addBtn.textContent = 'Failed - retry';
      addBtn.disabled = false;
    }
  });
  info.appendChild(addBtn);

  // "Not enough info to decide yet" - separate from the pull-list decision above. Posts the
  // full listing title (not stripped to a bare series name - a flag is about this specific
  // solicited item, not a recurring series) to the open-flags list the Solicitations page
  // banner reads from, so it resurfaces with a days-until-cutoff reminder instead of getting
  // forgotten once the page is closed.
  const flagBtn = document.createElement('button');
  flagBtn.type = 'button';
  flagBtn.className = 'secondary comic-flag-for-review';
  flagBtn.textContent = 'Flag for Review';
  flagBtn.addEventListener('click', async () => {
    flagBtn.disabled = true;
    flagBtn.textContent = 'Flagging…';
    try {
      const res = await fetch('/api/reviewflags/flag-from-listing', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          listingTitle: group[0].item.title,
          publisher: group[0].publisher,
          productCode: group[0].item.productCode,
          productUrl: group[0].item.productUrl,
        }),
      });
      if (!res.ok) throw new Error(`Request failed (${res.status})`);
      flagBtn.textContent = '🚩 Flagged';
      flagBtn.classList.add('result-sticky');
      document.dispatchEvent(new CustomEvent('reviewflag:added'));
    } catch (err) {
      console.error('[solicitation-cards] flag-for-review failed', err);
      flagBtn.textContent = 'Failed - retry';
      flagBtn.disabled = false;
    }
  });
  info.appendChild(flagBtn);

  card.appendChild(info);
  li.appendChild(card);
  return li;
}

// Cards (and their cover images) are only built the first time a group is actually
// opened, not upfront for every item on the page. This isn't just a perf nicety - an <img>
// that starts inside a collapsed <details> (display:none) never gets loaded in some
// browsers even after the group is opened, since there's nothing there yet for the browser
// to notice becoming visible. Building on open sidesteps that entirely.
function buildGroupCards(details) {
  if (details.dataset.built === 'true') {
    return;
  }
  const ul = document.createElement('ul');
  ul.className = 'comic-list';
  for (const group of details.issueGroups) {
    ul.appendChild(issueCard(group));
  }
  details.appendChild(ul);
  details.dataset.built = 'true';
}

// DCBS's own site-wide footer nav (present on every page, under "Preorders") lists
// publishers in this exact order - confirmed live 9/2026 by fetching the footer HTML
// directly, not guessed. This is that list reversed (so "Other" comes first, "DC Comics"
// last), restricted to the publishers this app actually crawls - DcbsPublisherCategories
// deliberately excludes the manga-only ones (Kodansha, Seven Seas, Tokyopop, VIZ, Yen
// Press) and the generic "Manga" category itself, so they never appear as a group here
// regardless of this list.
const PUBLISHER_DISPLAY_ORDER = [
  'Other',
  'Vault Comics',
  'Valiant Entertainment',
  'Udon Entertainment',
  'TwoMorrows Publishing',
  'Titan Comics',
  'Scout Comics',
  'Papercutz',
  'Oni Press',
  'IDW Publishing',
  'Fantagraphics',
  'Dynamite Entertainment',
  'Drawn & Quarterly',
  'Dark Horse',
  'Cinebook',
  'Boom! Studios',
  'Archie Comics Publications',
  'Image Comics',
  'Marvel Comics',
  'DC Comics',
];

// Renders one collapsible <details> per publisher into containerEl, cards built lazily on
// open (see buildGroupCards). Returns nothing - wires up its own toggle listeners.
function renderByPublisher(containerEl, items) {
  containerEl.innerHTML = '';

  const byPublisher = new Map();
  for (const solicitationItem of items) {
    const list = byPublisher.get(solicitationItem.publisher) || [];
    list.push(solicitationItem);
    byPublisher.set(solicitationItem.publisher, list);
  }

  // Anything not in PUBLISHER_DISPLAY_ORDER (there shouldn't be any, given the crawl list
  // above, but a newly-added category would otherwise vanish silently) sorts after every
  // known publisher, alphabetically among themselves, rather than being dropped.
  const publishers = [...byPublisher.keys()].sort((a, b) => {
    const ia = PUBLISHER_DISPLAY_ORDER.indexOf(a);
    const ib = PUBLISHER_DISPLAY_ORDER.indexOf(b);
    if (ia === -1 && ib === -1) return a.localeCompare(b);
    if (ia === -1) return 1;
    if (ib === -1) return -1;
    return ia - ib;
  });
  for (const publisher of publishers) {
    const issueGroups = groupByIssue(byPublisher.get(publisher));
    const details = document.createElement('details');
    details.className = 'candidate-group';
    details.issueGroups = issueGroups;

    const summary = document.createElement('summary');
    summary.textContent = `${publisher} (${issueGroups.length})`;
    details.appendChild(summary);

    details.addEventListener('toggle', () => {
      if (details.open) {
        buildGroupCards(details);
      }
    });

    containerEl.appendChild(details);
  }
}

// Filters the <details> groups built by renderByPublisher against containerEl by title.
function filterByPublisher(containerEl, term) {
  for (const details of containerEl.querySelectorAll('details')) {
    if (!term) {
      details.hidden = false;
      for (const wrap of details.querySelectorAll('.comic-card-wrap')) {
        wrap.hidden = false;
      }
      continue;
    }

    // Match against the underlying data, not the DOM - a still-collapsed group has no
    // cards built yet, so this has to work without them.
    const matchingIndexes = details.issueGroups
      .map((group, i) => (group.some((g) => g.item.title.toLowerCase().includes(term)) ? i : -1))
      .filter((i) => i >= 0);

    details.hidden = matchingIndexes.length === 0;
    if (matchingIndexes.length === 0) {
      continue;
    }

    buildGroupCards(details);
    details.open = true;
    const matchingSet = new Set(matchingIndexes);
    details.querySelectorAll('.comic-card-wrap').forEach((wrap, i) => {
      wrap.hidden = !matchingSet.has(i);
    });
  }
}
