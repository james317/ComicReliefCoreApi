const log = (...args) => console.log('[solicitations]', ...args);

const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const refreshBtn = document.getElementById('refreshBtn');
const message = document.getElementById('message');
const filterInput = document.getElementById('filterInput');
const publisherGroups = document.getElementById('publisherGroups');
const viewToggle = document.getElementById('viewToggle');
const viewByPublisherBtn = document.getElementById('viewByPublisherBtn');
const viewNewIssuesBtn = document.getElementById('viewNewIssuesBtn');
const newIssuesIntro = document.getElementById('newIssuesIntro');
const reviewFlagsBanner = document.getElementById('reviewFlagsBanner');
const reviewFlagsHeader = document.getElementById('reviewFlagsHeader');
const reviewFlagsList = document.getElementById('reviewFlagsList');

let allItems = [];
let currentView = 'publisher';

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

// Re-renders whichever view is currently selected from the already-fetched allItems -
// switching views (or re-filtering) never needs a fresh request.
function applyView() {
  if (allItems.length === 0) {
    filterInput.hidden = true;
    viewToggle.hidden = true;
    newIssuesIntro.hidden = true;
    publisherGroups.innerHTML = '';
    return;
  }

  const isNewIssuesView = currentView === 'new-issues';
  newIssuesIntro.hidden = !isNewIssuesView;
  viewByPublisherBtn.classList.toggle('active', !isNewIssuesView);
  viewByPublisherBtn.classList.toggle('secondary', isNewIssuesView);
  viewNewIssuesBtn.classList.toggle('active', isNewIssuesView);
  viewNewIssuesBtn.classList.toggle('secondary', !isNewIssuesView);

  const itemsToRender = isNewIssuesView
    ? allItems.filter((item) => item.isNewFirstIssueOrOneShot)
    : allItems;
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

(async () => {
  await loadStatus();
  await loadItems();
  await loadReviewFlags();
})();
