const log = (...args) => console.log('[cart-check]', ...args);

const cartText = document.getElementById('cartText');
const checkBtn = document.getElementById('checkBtn');
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

function renderDuplicate(result) {
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
  if (result.orderDate) metaParts.push(result.orderDate);
  if (result.quantity != null) metaParts.push(`Qty ${result.quantity}`);
  if (result.unitPrice != null) metaParts.push(`$${Number(result.unitPrice).toFixed(2)}`);
  if (result.status) metaParts.push(STATUS_LABEL[result.status] || result.status);
  const meta = document.createElement('div');
  meta.className = 'comic-meta';
  meta.textContent = metaParts.join(' · ');
  info.appendChild(meta);

  card.appendChild(info);
  li.appendChild(card);
  return li;
}

function renderLineResult(lineResult) {
  const li = document.createElement('li');
  li.className = 'comic-card-wrap';

  const card = document.createElement('div');
  card.className = 'pull-card' + (lineResult.possibleDuplicates.length > 0 ? ' wanted' : '');

  const badge = document.createElement('span');
  badge.className = 'pull-badge ' + (lineResult.possibleDuplicates.length > 0 ? 'wanted' : 'corralled');
  badge.textContent = lineResult.possibleDuplicates.length > 0 ? '!' : '✓';
  card.appendChild(badge);

  const info = document.createElement('div');
  info.className = 'pull-info';

  const title = document.createElement('div');
  title.className = 'pull-title';
  title.textContent = lineResult.pastedLine;
  info.appendChild(title);

  const meta = document.createElement('div');
  meta.className = 'pull-meta';
  meta.textContent = lineResult.possibleDuplicates.length > 0
    ? `Already in your order history — possible duplicate (${lineResult.possibleDuplicates.length} match${lineResult.possibleDuplicates.length === 1 ? '' : 'es'})`
    : 'No match in your order history.';
  info.appendChild(meta);

  if (lineResult.possibleDuplicates.length > 0) {
    const dupList = document.createElement('ul');
    dupList.className = 'comic-list';
    dupList.style.marginTop = '8px';
    for (const dup of lineResult.possibleDuplicates) {
      dupList.appendChild(renderDuplicate(dup));
    }
    info.appendChild(dupList);
  }

  card.appendChild(info);
  li.appendChild(card);
  return li;
}

async function check() {
  const text = cartText.value;
  resultsList.innerHTML = '';
  try {
    const res = await fetch('/api/orders/check-cart', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ cartText: text }),
    });
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const results = await res.json();
    log('check results', results.length);

    if (results.length === 0) {
      showMessage('Nothing to check — paste one item per line first.', true);
      return;
    }

    const flagged = results.filter((r) => r.possibleDuplicates.length > 0);
    showMessage(
      flagged.length > 0
        ? `${flagged.length} of ${results.length} line${results.length === 1 ? '' : 's'} may already be in your order history — check below.`
        : `Checked ${results.length} line${results.length === 1 ? '' : 's'} — nothing matched your order history.`,
      flagged.length > 0);

    for (const result of results) {
      resultsList.appendChild(renderLineResult(result));
    }
  } catch (err) {
    console.error('[cart-check] check failed', err);
    showMessage('Check failed - see the console.', true);
  }
}

checkBtn.addEventListener('click', async () => {
  if (!cartText.value.trim()) {
    showMessage('Paste the cart contents first.', true);
    return;
  }
  checkBtn.disabled = true;
  showMessage('Checking…', false);
  try {
    await check();
  } finally {
    checkBtn.disabled = false;
  }
});
