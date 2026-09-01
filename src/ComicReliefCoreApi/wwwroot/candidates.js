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

function renderStatus(status) {
  const publishers = Object.keys(status.publisherItemCounts || {});
  const errorPublishers = Object.keys(status.publisherErrors || {});

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

// DCBS's own title convention inserts "Cvr X" (or "Cover X") right after the
// series/issue identity for every variant of the same issue - splitting there groups
// "Absolute Batman #25 Cvr F..." and "...Cvr G..." under one "Absolute Batman #25" card
// instead of one card per cover. Items with no cover marker (most TPBs/HCs, one-shots
// without variants) just fall back to their own single-item group - no special-casing
// needed since a group of one renders identically to the old one-card-per-item layout.
function extractIssueIdentity(title) {
  const match = title.match(/^(.*?)\s+(cvr|cover)\b/i);
  return match ? match[1].trim() : title.trim();
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
      img.loading = 'lazy';
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

    const meta = document.createElement('div');
    meta.className = 'pull-meta';
    meta.textContent = match.items
      .map((i) => {
        const priceText = i.item.price != null ? ` ($${i.item.price.toFixed(2)})` : '';
        return `${i.item.title}${priceText}${i.item.isRelisted ? ' [Relisted]' : ''}`;
      })
      .join(' • ');
    info.appendChild(meta);

    li.appendChild(info);
    trackedList.appendChild(li);
  }
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

    const summary = document.createElement('summary');
    summary.textContent = `${publisher} (${issueGroups.length})`;
    details.appendChild(summary);

    const ul = document.createElement('ul');
    ul.className = 'comic-list';
    for (const group of issueGroups) {
      ul.appendChild(issueCard(group));
    }
    details.appendChild(ul);

    untrackedGroups.appendChild(details);
  }
}

function applyFilter() {
  const term = filterInput.value.trim().toLowerCase();
  const cards = document.querySelectorAll('#untrackedGroups .comic-card');
  let anyVisibleByGroup = new Map();

  for (const card of cards) {
    const matches = !term || card.dataset.title.includes(term);
    card.closest('.comic-card-wrap').hidden = !matches;
    if (matches) {
      const group = card.closest('details');
      anyVisibleByGroup.set(group, (anyVisibleByGroup.get(group) || 0) + 1);
    }
  }

  for (const details of document.querySelectorAll('#untrackedGroups details')) {
    const count = anyVisibleByGroup.get(details) || 0;
    details.hidden = term.length > 0 && count === 0;
    if (term.length > 0 && count > 0) {
      details.open = true;
    }
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
    const status = await res.json();
    log('refresh complete', status);
    renderStatus(status);
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
