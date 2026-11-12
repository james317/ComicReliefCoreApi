const log = (...args) => console.log('[reading-log]', ...args);

const searchInput = document.getElementById('searchInput');
const searchBtn = document.getElementById('searchBtn');
const suggestList = document.getElementById('suggestList');
const searchResults = document.getElementById('searchResults');
const sameWeekSection = document.getElementById('sameWeekSection');
const sameWeekHeading = document.getElementById('sameWeekHeading');
const sameWeekList = document.getElementById('sameWeekList');
const weekNavTop = document.getElementById('weekNavTop');
const weekNavBottom = document.getElementById('weekNavBottom');
const recentList = document.getElementById('recentList');

// What the same-week section is currently showing, so marking something read in it can
// refresh that exact view - anchored on an issue, or reached by stepping to a ship date.
let currentWeek = null;

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

async function markRead(item, fromWeekView = false) {
  log('marking read', item);
  try {
    const res = await fetch('/api/reading-log/read', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ series: item.series, issueNumber: item.issueNumber }),
    });
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    await runSearch();
    if (fromWeekView && currentWeek?.date) {
      await showWeek(currentWeek.date, false);
    } else if (fromWeekView && currentWeek?.item) {
      await showSameWeek(currentWeek.item);
    } else {
      await showSameWeek(item);
    }
    await loadRecent();
  } catch (err) {
    log('markRead failed', err);
  }
}

function renderWeekNav(container, view) {
  container.innerHTML = '';
  const prev = document.createElement('button');
  prev.type = 'button';
  prev.className = 'secondary';
  prev.textContent = view.previousReleaseDate ? `‹ ${view.previousReleaseDate}` : '‹ Earliest';
  prev.disabled = !view.previousReleaseDate;
  prev.addEventListener('click', () => showWeek(view.previousReleaseDate));

  const next = document.createElement('button');
  next.type = 'button';
  next.className = 'secondary';
  next.textContent = view.nextReleaseDate ? `${view.nextReleaseDate} ›` : 'Latest ›';
  next.disabled = !view.nextReleaseDate;
  next.addEventListener('click', () => showWeek(view.nextReleaseDate));

  container.appendChild(prev);
  container.appendChild(next);
}

function renderWeek(view) {
  sameWeekHeading.textContent = view.series
    ? `Shipped the same week as ${view.series} #${view.issueNumber} (${view.releaseDate})`
    : `Shipped the week of ${view.releaseDate}`;
  sameWeekList.innerHTML = '';
  for (const same of view.sameWeekIssues) {
    sameWeekList.appendChild(issueRow(same, (it) => markRead(it, true), null));
  }
  renderWeekNav(weekNavTop, view);
  renderWeekNav(weekNavBottom, view);
  sameWeekSection.hidden = false;
}

async function showSameWeek(item) {
  try {
    const params = new URLSearchParams({ series: item.series, issueNumber: item.issueNumber });
    const res = await fetch(`/api/reading-log/same-week?${params}`);
    if (res.status === 404) {
      sameWeekSection.hidden = true;
      currentWeek = null;
      return;
    }
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const view = await res.json();
    log('same week', view);
    currentWeek = { item };
    renderWeek(view);
  } catch (err) {
    log('showSameWeek failed', err);
  }
}

async function showWeek(releaseDate, scroll = true) {
  try {
    const res = await fetch(`/api/reading-log/week?releaseDate=${encodeURIComponent(releaseDate)}`);
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const view = await res.json();
    log('week', view);
    currentWeek = { date: releaseDate };
    renderWeek(view);
    if (scroll) sameWeekHeading.scrollIntoView({ block: 'start' });
  } catch (err) {
    log('showWeek failed', err);
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

function hideSuggestions() {
  suggestList.style.display = 'none';
  suggestList.innerHTML = '';
}

function selectSuggestion(series) {
  searchInput.value = series;
  hideSuggestions();
  runSearch();
}

async function updateSuggestions() {
  const query = searchInput.value.trim();
  if (query.length < 2) {
    hideSuggestions();
    return;
  }
  try {
    const res = await fetch(`/api/reading-log/suggest?query=${encodeURIComponent(query)}`);
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const suggestions = await res.json();
    if (suggestions.length === 0) {
      hideSuggestions();
      return;
    }
    suggestList.innerHTML = '';
    for (const s of suggestions) {
      const li = document.createElement('li');
      li.className = 'pull-card';
      li.style.cursor = 'pointer';

      const info = document.createElement('div');
      info.className = 'pull-info';
      const title = document.createElement('div');
      title.className = 'pull-title';
      title.textContent = s.series;
      const sub = document.createElement('div');
      sub.textContent = s.nextUnreadIssueNumber == null
        ? 'Caught up'
        : `Next: #${s.nextUnreadIssueNumber}${s.nextUnreadReleaseDate ? ` (${s.nextUnreadReleaseDate})` : ''}`;
      info.appendChild(title);
      info.appendChild(sub);
      li.appendChild(info);

      li.addEventListener('mousedown', (event) => {
        event.preventDefault();
        selectSuggestion(s.series);
      });
      suggestList.appendChild(li);
    }
    suggestList.style.display = '';
  } catch (err) {
    log('updateSuggestions failed', err);
  }
}

let suggestTimer = null;
searchInput.addEventListener('input', () => {
  clearTimeout(suggestTimer);
  suggestTimer = setTimeout(updateSuggestions, 200);
});
searchInput.addEventListener('focus', () => {
  if (searchInput.value.trim().length >= 2) updateSuggestions();
});
searchInput.addEventListener('blur', () => {
  setTimeout(hideSuggestions, 100);
});

searchBtn.addEventListener('click', () => {
  hideSuggestions();
  runSearch();
});
searchInput.addEventListener('keydown', (event) => {
  if (event.key === 'Enter') {
    event.preventDefault();
    hideSuggestions();
    runSearch();
  } else if (event.key === 'Escape') {
    hideSuggestions();
  }
});

(async () => {
  await loadRecent();
})();
