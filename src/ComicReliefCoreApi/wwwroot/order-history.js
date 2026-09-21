const log = (...args) => console.log('[order-history]', ...args);

const searchForm = document.getElementById('searchForm');
const termInput = document.getElementById('termInput');
const searchBtn = document.getElementById('searchBtn');
const message = document.getElementById('message');
const resultsList = document.getElementById('resultsList');

function showMessage(text, isError) {
  message.textContent = text;
  message.className = 'message ' + (isError ? 'error' : 'success');
}

const STATUS_LABEL = {
  Processing: 'Processing',
  Filled: 'Filled',
  Shipped: 'Shipped',
  Cancelled: 'Cancelled',
};

function renderResult(result) {
  const li = document.createElement('li');
  li.className = 'comic-card-wrap';

  const card = document.createElement(result.productUrl ? 'a' : 'div');
  card.className = 'comic-card';
  if (result.productUrl) {
    card.href = result.productUrl;
    card.target = '_blank';
    card.rel = 'noopener';
  }

  if (result.thumbnailUrl) {
    const img = document.createElement('img');
    img.className = 'comic-cover';
    img.src = result.thumbnailUrl;
    img.alt = result.title;
    card.appendChild(img);
  }

  const info = document.createElement('div');
  info.className = 'comic-info';

  const title = document.createElement('div');
  title.className = 'comic-title';
  title.textContent = result.title;
  info.appendChild(title);

  const metaParts = [`Order #${result.orderId}`];
  if (result.orderDate) {
    metaParts.push(result.orderDate);
  }
  if (result.publisher) {
    metaParts.push(result.publisher);
  }
  if (result.quantity != null) {
    metaParts.push(`Qty ${result.quantity}`);
  }
  if (result.unitPrice != null) {
    metaParts.push(`$${Number(result.unitPrice).toFixed(2)}`);
  }
  if (result.status) {
    metaParts.push(STATUS_LABEL[result.status] || result.status);
  }
  const meta = document.createElement('div');
  meta.className = 'comic-meta';
  meta.textContent = metaParts.join(' · ');
  info.appendChild(meta);

  card.appendChild(info);
  li.appendChild(card);
  return li;
}

async function search(term) {
  resultsList.innerHTML = '';
  try {
    const res = await fetch(`/api/orders/search?term=${encodeURIComponent(term)}`);
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const results = await res.json();
    log('search results', results.length);

    if (results.length === 0) {
      showMessage(`No ordered items matching "${term}".`, false);
      return;
    }

    showMessage(`${results.length} matching item${results.length === 1 ? '' : 's'}.`, false);
    for (const result of results) {
      resultsList.appendChild(renderResult(result));
    }
  } catch (err) {
    console.error('[order-history] search failed', err);
    showMessage('Search failed - check the console.', true);
  }
}

searchForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  const term = termInput.value.trim();
  if (!term) {
    return;
  }
  searchBtn.disabled = true;
  showMessage('Searching…', false);
  try {
    await search(term);
  } finally {
    searchBtn.disabled = false;
  }
});
