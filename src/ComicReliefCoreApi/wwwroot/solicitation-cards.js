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

  const prices = group.map((g) => g.item.price).filter((p) => p != null);
  const metaParts = [group[0].publisher];
  if (group.length > 1) {
    metaParts.push(`${group.length} covers`);
  }
  if (prices.length > 0) {
    const min = Math.min(...prices);
    const max = Math.max(...prices);
    metaParts.push(min === max ? `$${min.toFixed(2)}` : `$${min.toFixed(2)}–$${max.toFixed(2)}`);
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

  const publishers = [...byPublisher.keys()].sort();
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
