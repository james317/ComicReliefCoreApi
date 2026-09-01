const log = (...args) => console.log('[candidates]', ...args);

const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const refreshBtn = document.getElementById('refreshBtn');
const message = document.getElementById('message');
const filterInput = document.getElementById('filterInput');
const results = document.getElementById('results');
const trackedHeading = document.getElementById('trackedHeading');
const trackedList = document.getElementById('trackedList');
const untrackedGroups = document.getElementById('untrackedGroups');

function showMessage(text, isError) {
  message.textContent = text;
  message.className = 'message ' + (isError ? 'error' : 'success');
}

function formatDateTime(value) {
  return value ? new Date(value).toLocaleString() : null;
}

function renderStatus(status, publisherErrors) {
  const publishers = Object.keys(status.publisherItemCounts || {});
  const errorPublishers = Object.keys(publisherErrors || {});

  if (!status.lastRefreshedAt) {
    statusDot.className = 'status-dot unknown';
    statusText.textContent = 'Never refreshed yet - click "Refresh from DCBS" to crawl current solicitations.';
    return;
  }

  statusDot.className = errorPublishers.length > 0 ? 'status-dot invalid' : 'status-dot valid';
  const parts = [
    `${status.totalItems} items across ${publishers.length} publishers, last refreshed ${formatDateTime(status.lastRefreshedAt)}.`,
  ];
  if (errorPublishers.length > 0) {
    parts.push(`Failed to crawl: ${errorPublishers.join(', ')}.`);
  }
  statusText.textContent = parts.join(' ');
}

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
      // actually opened (see renderUntracked), so native lazy-loading has nothing left to
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
  const meta = document.createElement('div');
  meta.className = 'comic-meta';
  meta.textContent = metaParts.join(' · ');
  info.appendChild(meta);

  card.appendChild(info);
  li.appendChild(card);
  return li;
}

function renderTracked(matches) {
  trackedHeading.textContent = `On Your Pull List (${matches.length})`;
  trackedList.innerHTML = '';

  for (const match of matches) {
    const li = document.createElement('li');
    li.className = 'pull-card';

    const badge = document.createElement('span');
    badge.className = 'pull-badge ' + (match.status === 'Sticky' ? 'corralled' : match.status === 'Unsticky' ? 'wanted' : 'unresolved');
    badge.textContent = match.items.length;
    li.appendChild(badge);

    const info = document.createElement('div');
    info.className = 'pull-info';

    const title = document.createElement('div');
    title.className = 'pull-title';
    title.textContent = match.pullListTitle;
    info.appendChild(title);

    // Same card treatment as the publisher sections below - covers shown eagerly here
    // (not deferred like buildGroupCards) since this list is always visible, never
    // inside a collapsed <details>.
    const ul = document.createElement('ul');
    ul.className = 'comic-list';
    for (const group of groupByIssue(match.items)) {
      ul.appendChild(issueCard(group));
    }
    info.appendChild(ul);

    li.appendChild(info);
    trackedList.appendChild(li);
  }
}

// Cards (and their cover images) are only built the first time a group is actually
// opened, not upfront for all ~1900 items. This isn't just a perf nicety - an <img> that
// starts inside a collapsed <details> (display:none) never gets loaded in some browsers
// even after the group is opened, since there's nothing there yet for the browser to
// notice becoming visible. Building on open sidesteps that entirely.
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

function renderUntracked(untracked) {
  untrackedGroups.innerHTML = '';

  const byPublisher = new Map();
  for (const solicitationItem of untracked) {
    const list = byPublisher.get(solicitationItem.publisher) || [];
    list.push(solicitationItem);
    byPublisher.set(solicitationItem.publisher, list);
  }

  const publishers = [...byPublisher.keys()].sort();
  for (const publisher of publishers) {
    const items = byPublisher.get(publisher);
    const issueGroups = groupByIssue(items);
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

    untrackedGroups.appendChild(details);
  }
}

function applyFilter() {
  const term = filterInput.value.trim().toLowerCase();

  for (const details of document.querySelectorAll('#untrackedGroups details')) {
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

async function loadStatus() {
  try {
    const res = await fetch('/api/solicitations/status');
    const status = await res.json();
    renderStatus(status);
    return status;
  } catch (err) {
    log('loadStatus failed', err);
    statusText.textContent = 'Could not reach the API.';
    return null;
  }
}

async function loadCandidates() {
  try {
    const res = await fetch('/api/solicitations/candidates');
    const data = await res.json();
    log('candidates loaded', data.trackedMatches.length, 'tracked,', data.untracked.length, 'untracked');

    if (!data.generatedAt) {
      results.hidden = true;
      filterInput.hidden = true;
      return;
    }

    renderTracked(data.trackedMatches);
    renderUntracked(data.untracked);
    results.hidden = false;
    filterInput.hidden = false;
  } catch (err) {
    log('loadCandidates failed', err);
    showMessage('Could not load candidates.', true);
  }
}

refreshBtn.addEventListener('click', async () => {
  refreshBtn.disabled = true;
  showMessage('Crawling every publisher on DCBS - this can take a minute or two…', false);
  try {
    const res = await fetch('/api/solicitations/refresh', { method: 'POST' });
    const result = await res.json();
    log('refresh complete', result);
    renderStatus(result.status, result.publisherErrors);
    showMessage('Refreshed.', false);
    await loadCandidates();
  } catch (err) {
    log('refresh failed', err);
    showMessage('Refresh failed - check the console.', true);
  } finally {
    refreshBtn.disabled = false;
  }
});

filterInput.addEventListener('input', applyFilter);

(async () => {
  await loadStatus();
  await loadCandidates();
})();
