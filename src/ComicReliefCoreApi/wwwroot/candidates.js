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

function itemCard(solicitationItem) {
  const { publisher, item } = solicitationItem;
  const li = document.createElement('li');
  li.className = 'comic-card-wrap';

  const a = document.createElement('a');
  a.className = 'comic-card';
  a.href = item.productUrl;
  a.target = '_blank';
  a.rel = 'noopener';
  a.dataset.title = item.title.toLowerCase();

  if (item.thumbnailUrl) {
    const img = document.createElement('img');
    img.className = 'comic-cover';
    img.src = item.thumbnailUrl;
    img.alt = '';
    img.loading = 'lazy';
    a.appendChild(img);
  }

  const info = document.createElement('div');
  info.className = 'comic-info';

  const title = document.createElement('div');
  title.className = 'comic-title';
  title.textContent = item.title;
  info.appendChild(title);

  const metaParts = [publisher];
  if (item.price != null) {
    metaParts.push(`$${item.price.toFixed(2)}`);
  }
  if (item.isRelisted) {
    metaParts.push('Relisted');
  }
  const meta = document.createElement('div');
  meta.className = 'comic-meta';
  meta.textContent = metaParts.join(' · ');
  info.appendChild(meta);

  a.appendChild(info);
  li.appendChild(a);
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
    const details = document.createElement('details');
    details.className = 'candidate-group';

    const summary = document.createElement('summary');
    summary.textContent = `${publisher} (${items.length})`;
    details.appendChild(summary);

    const ul = document.createElement('ul');
    ul.className = 'comic-list';
    for (const item of items) {
      ul.appendChild(itemCard(item));
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
