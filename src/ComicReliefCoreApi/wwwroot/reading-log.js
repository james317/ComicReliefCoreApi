const log = (...args) => console.log('[reading-log]', ...args);

const searchInput = document.getElementById('searchInput');
const searchBtn = document.getElementById('searchBtn');
const searchResults = document.getElementById('searchResults');
const sameWeekSection = document.getElementById('sameWeekSection');
const sameWeekHeading = document.getElementById('sameWeekHeading');
const sameWeekList = document.getElementById('sameWeekList');
const recentList = document.getElementById('recentList');

function issueRow(item, onMarkRead, onShowSameWeek) {
  const li = document.createElement('li');
  li.className = 'pull-card';

  const info = document.createElement('div');
  info.className = 'pull-info';
  const title = document.createElement('div');
  title.className = 'pull-title';
  title.textContent = `${item.series} #${item.issueNumber}`;
  const sub = document.createElement('div');
  sub.textContent = item.releaseDate ? `Released ${item.releaseDate}` : 'No release date on file';
  info.appendChild(title);
  info.appendChild(sub);
  li.appendChild(info);

  if (item.alreadyRead) {
    const badge = document.createElement('span');
    badge.className = 'pull-badge corralled';
    badge.textContent = 'Read';
    li.appendChild(badge);
  } else if (onMarkRead) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.textContent = 'Mark read';
    btn.addEventListener('click', () => onMarkRead(item));
    li.appendChild(btn);
  }

  if (onShowSameWeek && item.releaseDate) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'secondary';
    btn.textContent = 'Same week';
    btn.addEventListener('click', () => onShowSameWeek(item));
    li.appendChild(btn);
  }

  return li;
}

async function markRead(item) {
  log('marking read', item);
  try {
    const res = await fetch('/api/reading-log/read', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ series: item.series, issueNumber: item.issueNumber }),
    });
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    await runSearch();
    await showSameWeek(item);
    await loadRecent();
  } catch (err) {
    log('markRead failed', err);
  }
}

async function showSameWeek(item) {
  try {
    const params = new URLSearchParams({ series: item.series, issueNumber: item.issueNumber });
    const res = await fetch(`/api/reading-log/same-week?${params}`);
    if (res.status === 404) {
      sameWeekSection.hidden = true;
      return;
    }
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const view = await res.json();
    log('same week', view);

    sameWeekHeading.textContent = `Shipped the same week as ${view.series} #${view.issueNumber} (${view.releaseDate})`;
    sameWeekList.innerHTML = '';
    for (const same of view.sameWeekIssues) {
      sameWeekList.appendChild(issueRow(same, markRead, null));
    }
    sameWeekSection.hidden = false;
  } catch (err) {
    log('showSameWeek failed', err);
  }
}

async function runSearch() {
  const query = searchInput.value.trim();
  if (!query) {
    searchResults.innerHTML = '';
    return;
  }
  try {
    const res = await fetch(`/api/reading-log/search-owned?query=${encodeURIComponent(query)}`);
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const items = await res.json();
    log('search results', items.length);
    searchResults.innerHTML = '';
    for (const item of items) {
      searchResults.appendChild(issueRow(item, markRead, showSameWeek));
    }
  } catch (err) {
    log('runSearch failed', err);
    searchResults.innerHTML = '<li>Search failed.</li>';
  }
}

async function loadRecent() {
  try {
    const res = await fetch('/api/reading-log/recent?max=20');
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const items = await res.json();
    recentList.innerHTML = '';
    if (items.length === 0) {
      recentList.innerHTML = '<li>Nothing logged yet.</li>';
      return;
    }
    for (const item of items) {
      const li = document.createElement('li');
      li.className = 'pull-card';
      const info = document.createElement('div');
      info.className = 'pull-info';
      const title = document.createElement('div');
      title.className = 'pull-title';
      title.textContent = `${item.series} #${item.issueNumber}`;
      const sub = document.createElement('div');
      sub.textContent = item.readAt ? `Read ${new Date(item.readAt).toLocaleString()}` : 'Read date unknown (backfilled)';
      info.appendChild(title);
      info.appendChild(sub);
      li.appendChild(info);
      recentList.appendChild(li);
    }
  } catch (err) {
    log('loadRecent failed', err);
  }
}

searchBtn.addEventListener('click', runSearch);
searchInput.addEventListener('keydown', (event) => {
  if (event.key === 'Enter') {
    event.preventDefault();
    runSearch();
  }
});

(async () => {
  await loadRecent();
})();
