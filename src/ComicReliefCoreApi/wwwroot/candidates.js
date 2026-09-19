const log = (...args) => console.log('[candidates]', ...args);

const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const refreshBtn = document.getElementById('refreshBtn');
const orderStatusDot = document.getElementById('orderStatusDot');
const orderStatusText = document.getElementById('orderStatusText');
const syncOrderBtn = document.getElementById('syncOrderBtn');
const message = document.getElementById('message');
const results = document.getElementById('results');
const unstickyHeading = document.getElementById('unstickyHeading');
const unstickyList = document.getElementById('unstickyList');
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

function renderOrderStatus(status, orderErrors) {
  const errorOrders = Object.keys(orderErrors || {});

  if (!status.lastSyncedAt) {
    orderStatusDot.className = 'status-dot unknown';
    orderStatusText.textContent = 'No orders synced yet - click "Sync Order History" to compare your pull list against everything you\'ve ordered.';
    return;
  }
  orderStatusDot.className = errorOrders.length > 0 ? 'status-dot invalid' : 'status-dot valid';
  const parts = [
    `Comparing against ${status.orderCount} orders (${status.totalLineCount} items total), last synced ${formatDateTime(status.lastSyncedAt)}.`,
  ];
  if (errorOrders.length > 0) {
    parts.push(`Failed to fetch orders: ${errorOrders.join(', ')}.`);
  }
  orderStatusText.textContent = parts.join(' ');
}

// extractIssueIdentity, groupByIssue, and issueCard come from solicitation-cards.js -
// shared with the Solicitations tab's full by-publisher browse.
function renderMatchCard(match, targetList) {
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

  // Same card treatment as the Solicitations tab - covers shown eagerly here (these
  // lists are short and always visible, never inside a collapsed <details> themselves).
  const ul = document.createElement('ul');
  ul.className = 'comic-list';
  for (const group of groupByIssue(match.items)) {
    ul.appendChild(issueCard(group, true));
  }
  info.appendChild(ul);

  li.appendChild(info);
  targetList.appendChild(li);
}

// Split by status rather than one flat list - the actionable half of a monthly
// order pass is specifically the Unsticky matches (DCBS's own sticky list never
// auto-carries these into the cart, so they're easy to forget), while Sticky/
// Unresolved ones should already be sitting in the cart automatically and are
// only here for reference.
function renderTracked(matches) {
  const unsticky = matches.filter(m => m.status === 'Unsticky');
  const rest = matches.filter(m => m.status !== 'Unsticky');

  unstickyHeading.textContent = `Unsticky — Add To Cart By Hand (${unsticky.length})`;
  unstickyList.innerHTML = '';
  for (const match of unsticky) {
    renderMatchCard(match, unstickyList);
  }

  trackedHeading.textContent = `Sticky / Unresolved matches (${rest.length})`;
  trackedList.innerHTML = '';
  for (const match of rest) {
    renderMatchCard(match, trackedList);
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
  showMessage('Fetching your order history from DCBS - this can take a little while…', false);
  try {
    const res = await fetch('/api/orders/sync-recent', { method: 'POST' });
    const result = await res.json();
    log('order sync complete', result);
    renderOrderStatus(result.status, result.orderErrors);
    const newCount = (result.newFirstIssues || []).length;
    showMessage(
      `Synced ${result.status.orderCount} orders.` +
        (newCount > 0 ? ` ${newCount} new title(s) found and tracked - see the Shipments tab for details.` : ''),
      false);
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
