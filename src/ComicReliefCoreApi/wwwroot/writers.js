const log = (...args) => console.log('[writers]', ...args);

const addForm = document.getElementById('addForm');
const nameInput = document.getElementById('nameInput');
const typeInput = document.getElementById('typeInput');
const addBtn = document.getElementById('addBtn');
const message = document.getElementById('message');
const groups = document.getElementById('groups');
const favoritesHeading = document.getElementById('favoritesHeading');
const favoritesList = document.getElementById('favoritesList');
const avoidHeading = document.getElementById('avoidHeading');
const avoidList = document.getElementById('avoidList');

function showMessage(text, isError) {
  message.textContent = text;
  message.className = 'message ' + (isError ? 'error' : 'success');
}

function renderCard(entry) {
  const li = document.createElement('li');
  const card = document.createElement('div');
  card.className = 'pull-card';

  const badge = document.createElement('span');
  badge.className = 'pull-badge ' + (entry.type === 'Favorite' ? 'favorite' : 'avoid');
  badge.textContent = entry.type === 'Favorite' ? '★' : '⚠';
  card.appendChild(badge);

  const info = document.createElement('div');
  info.className = 'pull-info';

  const title = document.createElement('div');
  title.className = 'pull-title';
  title.textContent = entry.name;
  info.appendChild(title);

  const removeBtn = document.createElement('button');
  removeBtn.type = 'button';
  removeBtn.className = 'secondary';
  removeBtn.textContent = 'Remove';
  removeBtn.style.marginTop = '8px';
  removeBtn.addEventListener('click', async () => {
    removeBtn.disabled = true;
    try {
      const res = await fetch(`/api/writerpreferences/${entry.id}`, { method: 'DELETE' });
      if (!res.ok) throw new Error(`Request failed (${res.status})`);
      await loadList();
    } catch (err) {
      console.error('[writers] remove failed', err);
      showMessage(`Remove failed: ${err.message}`, true);
      removeBtn.disabled = false;
    }
  });
  info.appendChild(removeBtn);

  card.appendChild(info);
  li.appendChild(card);
  return li;
}

async function loadList() {
  try {
    const res = await fetch('/api/writerpreferences');
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const entries = await res.json();
    log('loaded', entries.length);

    const favorites = entries.filter((e) => e.type === 'Favorite');
    const avoid = entries.filter((e) => e.type === 'Avoid');

    favoritesHeading.textContent = `Favorites (${favorites.length})`;
    favoritesList.innerHTML = '';
    for (const entry of favorites) {
      favoritesList.appendChild(renderCard(entry));
    }

    avoidHeading.textContent = `Avoid (${avoid.length})`;
    avoidList.innerHTML = '';
    for (const entry of avoid) {
      avoidList.appendChild(renderCard(entry));
    }

    groups.hidden = false;
  } catch (err) {
    console.error('[writers] loadList failed', err);
    showMessage('Could not load writer list.', true);
  }
}

addForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  const name = nameInput.value.trim();
  if (!name) {
    return;
  }

  addBtn.disabled = true;
  try {
    const res = await fetch('/api/writerpreferences', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, type: typeInput.value }),
    });
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    nameInput.value = '';
    showMessage(`Added "${name}" as ${typeInput.value}.`, false);
    await loadList();
  } catch (err) {
    console.error('[writers] add failed', err);
    showMessage(`Add failed: ${err.message}`, true);
  } finally {
    addBtn.disabled = false;
  }
});

loadList();
