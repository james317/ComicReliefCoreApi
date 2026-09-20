const log = (...args) => console.log('[solicitations]', ...args);

const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const refreshBtn = document.getElementById('refreshBtn');
const orderStatusDot = document.getElementById('orderStatusDot');
const orderStatusText = document.getElementById('orderStatusText');
const syncOrderBtn = document.getElementById('syncOrderBtn');
const message = document.getElementById('message');
const filterInput = document.getElementById('filterInput');
const publisherGroups = document.getElementById('publisherGroups');
const viewToggle = document.getElementById('viewToggle');
const viewByPublisherBtn = document.getElementById('viewByPublisherBtn');
const viewNewIssuesBtn = document.getElementById('viewNewIssuesBtn');
const viewSinceRefreshBtn = document.getElementById('viewSinceRefreshBtn');
const viewSinceOrderBtn = document.getElementById('viewSinceOrderBtn');
const newIssuesIntro = document.getElementById('newIssuesIntro');
const deltaIntro = document.getElementById('deltaIntro');
const reviewFlagsBanner = document.getElementById('reviewFlagsBanner');
const reviewFlagsHeader = document.getElementById('reviewFlagsHeader');
const reviewFlagsList = document.getElementById('reviewFlagsList');

let allItems = [];
let currentView = 'publisher';
// Fetched once per page load (not per view-switch) - the order's placement date doesn't
// change without a fresh "Sync Order History" click, and there's no reason to hit the API
// again just to toggle back and forth between views.
let mostRecentOrderDate = null;

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

// Order data ("In your order" badges, the New Since Order view) is entirely separate from
// the solicitations crawl above - it only reflects whatever the LAST "Sync Order History"
// click (here or on the Candidates tab) found, not anything just added to a real DCBS order.
// Kept as its own status card/button (matching Candidates exactly) rather than folding into
// "Refresh from DCBS", since the two hit a completely different set of DCBS pages and either
// one can fail on its own.
function renderOrderStatus(status, orderErrors) {
  const errorOrders = Object.keys(orderErrors || {});

  if (!status.lastSyncedAt) {
    orderStatusDot.className = 'status-dot unknown';
    orderStatusText.textContent = 'No orders synced yet - click "Sync Order History" so "In your order" badges (and the New Since Order view) reflect what you\'ve actually ordered.';
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

async function loadMostRecentOrderDate() {
  try {
    const res = await fetch('/api/orders/most-recent-date');
    mostRecentOrderDate = await res.json(); // { orderId, orderDate } or null
  } catch (err) {
    log('loadMostRecentOrderDate failed', err);
    mostRecentOrderDate = null;
  }
}

// Every view here (other than "By Publisher", the unfiltered complete list) is a plain
// client-side filter over the same already-fetched allItems - no separate request per view,
// matching the New #1s view's own pattern. The two delta views both key off
// item.firstSeenAt (when DCBS first showed this exact product code, preserved across
// refreshes - see SolicitationItem/DcbsSolicitationEntry): "Since Refresh" uses the
// server-computed isNewSinceLastRefresh boolean (true exactly when this refresh is the first
// time the code was ever seen), "Since Order" instead compares against the most recently
// placed order's own date, fetched once via loadMostRecentOrderDate - DCBS exposes only an
// "Order Date" (initial placement), no separate "last updated" field, so that's the one
// anchor available for this view.
function applyView() {
  if (allItems.length === 0) {
    filterInput.hidden = true;
    viewToggle.hidden = true;
    newIssuesIntro.hidden = true;
    deltaIntro.hidden = true;
    publisherGroups.innerHTML = '';
    return;
  }

  const buttonsByView = {
    publisher: viewByPublisherBtn,
    'new-issues': viewNewIssuesBtn,
    'since-refresh': viewSinceRefreshBtn,
    'since-order': viewSinceOrderBtn,
  };
  for (const [view, btn] of Object.entries(buttonsByView)) {
    btn.classList.toggle('active', view === currentView);
    btn.classList.toggle('secondary', view !== currentView);
  }

  newIssuesIntro.hidden = currentView !== 'new-issues';

  let itemsToRender = allItems;
  if (currentView === 'new-issues') {
    itemsToRender = allItems.filter((item) => item.isNewFirstIssueOrOneShot);
  } else if (currentView === 'since-refresh') {
    itemsToRender = allItems.filter((item) => item.isNewSinceLastRefresh);
    deltaIntro.hidden = false;
    deltaIntro.textContent = `Titles first seen in the most recent "Refresh from DCBS" - ${itemsToRender.length} found.`;
  } else if (currentView === 'since-order') {
    if (mostRecentOrderDate) {
      const cutoff = new Date(`${mostRecentOrderDate.orderDate}T00:00:00`);
      itemsToRender = allItems.filter((item) => new Date(item.firstSeenAt) > cutoff);
      deltaIntro.hidden = false;
      deltaIntro.textContent =
        `Titles first solicited since order ${mostRecentOrderDate.orderId} was placed ` +
        `(${mostRecentOrderDate.orderDate}) - ${itemsToRender.length} found.`;
    } else {
      itemsToRender = [];
      deltaIntro.hidden = false;
      deltaIntro.textContent = 'No synced order to compare against yet - click "Sync Order History" above first.';
    }
  }
  if (currentView !== 'since-refresh' && currentView !== 'since-order') {
    deltaIntro.hidden = true;
  }

  renderByPublisher(publisherGroups, itemsToRender);
  filterInput.value = '';
  filterInput.hidden = false;
  viewToggle.hidden = false;
}

// Cutoff date comes back as a bare "yyyy-MM-dd" DateOnly - appending T00:00:00 forces the
// browser to parse it as local midnight rather than UTC midnight, which would otherwise
// display a day early/late depending on the viewer's timezone (the same DateTime/DateOnly
// UTC-vs-local trap documented in ComicReliefDbContext, just on the frontend side this time).
function daysUntil(isoDate) {
  const target = new Date(`${isoDate}T00:00:00`);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return Math.round((target - today) / 86400000);
}

function renderReviewFlags(data) {
  const { flags, orderEditCutoffDate } = data;
  if (flags.length === 0) {
    reviewFlagsBanner.hidden = true;
    return;
  }

  reviewFlagsBanner.hidden = false;
  const count = `${flags.length} title${flags.length === 1 ? '' : 's'} flagged for review`;
  if (orderEditCutoffDate) {
    const days = daysUntil(orderEditCutoffDate);
    const when = days > 0 ? `in ${days} day${days === 1 ? '' : 's'}` : days === 0 ? 'today' : 'already passed';
    reviewFlagsHeader.textContent = `${count} — order edits close ${when} (${orderEditCutoffDate}).`;
  } else {
    reviewFlagsHeader.textContent = `${count} — couldn't read the current order-edit cutoff from DCBS.`;
  }

  reviewFlagsList.innerHTML = '';
  for (const flag of flags) {
    const li = document.createElement('li');
    const link = document.createElement('a');
    link.href = flag.productUrl || '#';
    link.target = '_blank';
    link.rel = 'noopener';
    link.textContent = flag.title;
    li.appendChild(link);

    const resolveBtn = document.createElement('button');
    resolveBtn.type = 'button';
    resolveBtn.className = 'secondary';
    resolveBtn.textContent = 'Resolve';
    resolveBtn.addEventListener('click', async () => {
      resolveBtn.disabled = true;
      try {
        const res = await fetch(`/api/reviewflags/${flag.id}/resolve`, { method: 'POST' });
        if (!res.ok) throw new Error(`Request failed (${res.status})`);
        await loadReviewFlags();
      } catch (err) {
        console.error('[solicitations] resolve flag failed', err);
        resolveBtn.disabled = false;
      }
    });
    li.appendChild(resolveBtn);

    reviewFlagsList.appendChild(li);
  }
}

async function loadReviewFlags() {
  try {
    const res = await fetch('/api/reviewflags');
    renderReviewFlags(await res.json());
  } catch (err) {
    log('loadReviewFlags failed', err);
  }
}

document.addEventListener('reviewflag:added', loadReviewFlags);

async function loadItems() {
  try {
    const res = await fetch('/api/solicitations/items');
    allItems = await res.json();
    log('items loaded', allItems.length);
    applyView();
  } catch (err) {
    log('loadItems failed', err);
    showMessage('Could not load solicitations.', true);
  } finally {
    window.dismissSplash?.();
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
    await loadItems();
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
        (newCount > 0 ? ` ${newCount} new title(s) found and tracked - see the Pull List tab for details.` : ''),
      false);
    await loadItems();
  } catch (err) {
    log('order sync failed', err);
    showMessage('Order sync failed - check the console.', true);
  } finally {
    syncOrderBtn.disabled = false;
  }
});

filterInput.addEventListener('input', () => {
  filterByPublisher(publisherGroups, filterInput.value.trim().toLowerCase());
});

viewByPublisherBtn.addEventListener('click', () => {
  currentView = 'publisher';
  applyView();
});

viewNewIssuesBtn.addEventListener('click', () => {
  currentView = 'new-issues';
  applyView();
});

viewSinceRefreshBtn.addEventListener('click', () => {
  currentView = 'since-refresh';
  applyView();
});

viewSinceOrderBtn.addEventListener('click', () => {
  currentView = 'since-order';
  applyView();
});

(async () => {
  await loadStatus();
  await loadOrderStatus();
  await loadMostRecentOrderDate();
  await loadItems();
  await loadReviewFlags();
})();
