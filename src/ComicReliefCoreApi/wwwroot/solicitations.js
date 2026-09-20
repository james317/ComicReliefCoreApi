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
})();
