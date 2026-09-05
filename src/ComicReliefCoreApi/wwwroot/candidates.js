const log = (...args) => console.log('[candidates]', ...args);

const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const refreshBtn = document.getElementById('refreshBtn');
const orderStatusDot = document.getElementById('orderStatusDot');
const orderStatusText = document.getElementById('orderStatusText');
const syncOrderBtn = document.getElementById('syncOrderBtn');
const message = document.getElementById('message');
const results = document.getElementById('results');
const trackedHeading = document.getElementById('trackedHeading');
const trackedList = document.getElementById('trackedList');

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

function renderOrderStatus(status) {
  if (!status.orderId) {
    orderStatusDot.className = 'status-dot unknown';
    orderStatusText.textContent = 'No order synced yet - click "Sync Latest Order" to compare your pull list against it.';
    return;
  }
  orderStatusDot.className = 'status-dot valid';
  orderStatusText.textContent =
    `Comparing against order #${status.orderId} (${status.lineCount} items), synced ${formatDateTime(status.syncedAt)}.`;
}

// extractIssueIdentity, groupByIssue, and issueCard come from solicitation-cards.js -
// shared with the Solicitations tab's full by-publisher browse.
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

    // Same card treatment as the Solicitations tab - covers shown eagerly here (this
    // list is short and always visible, never inside a collapsed <details>).
    const ul = document.createElement('ul');
    ul.className = 'comic-list';
    for (const group of groupByIssue(match.items)) {
      ul.appendChild(issueCard(group, true));
    }
    info.appendChild(ul);

    li.appendChild(info);
    trackedList.appendChild(li);
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

async function loadOrderStatus() {
  try {
    const res = await fetch('/api/orders/status');
    const status = await res.json();
    renderOrderStatus(status);
    return status;
  } catch (err) {
    log('loadOrderStatus failed', err);
    orderStatusText.textContent = 'Could not reach the API.';
    return null;
  }
}

async function loadCandidates() {
  try {
    const res = await fetch('/api/solicitations/candidates');
    const data = await res.json();
    log('candidates loaded', data.trackedMatches.length, 'tracked matches');

    if (!data.generatedAt) {
      results.hidden = true;
      return;
    }

    renderTracked(data.trackedMatches);
    results.hidden = false;
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

syncOrderBtn.addEventListener('click', async () => {
  syncOrderBtn.disabled = true;
  showMessage('Fetching your most recent order from DCBS…', false);
  try {
    const res = await fetch('/api/orders/sync-latest', { method: 'POST' });
    const status = await res.json();
    log('order sync complete', status);
    renderOrderStatus(status);
    showMessage(status.orderId ? `Synced order #${status.orderId}.` : 'No recent orders found on DCBS.', !status.orderId);
    await loadCandidates();
  } catch (err) {
    log('order sync failed', err);
    showMessage('Order sync failed - check the console.', true);
  } finally {
    syncOrderBtn.disabled = false;
  }
});

(async () => {
  await loadStatus();
  await loadOrderStatus();
  await loadCandidates();
})();
