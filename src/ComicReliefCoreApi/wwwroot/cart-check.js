const log = (...args) => console.log('[cart-check]', ...args);

const cartText = document.getElementById('cartText');
const checkBtn = document.getElementById('checkBtn');
const pdfInput = document.getElementById('pdfInput');
const pdfBtn = document.getElementById('pdfBtn');
const candidatesLink = document.getElementById('candidatesLink');
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

// A cart line needs a look if it may already be ordered, or (PDF uploads only, where the
// quantity is known) it's in the cart more than once.
function hasDuplicate(lineResult) {
  return lineResult.possibleDuplicates.length > 0;
}

function hasExtraCopies(lineResult) {
  return lineResult.quantity != null && lineResult.quantity > 1;
}

function needsLook(lineResult) {
  return hasDuplicate(lineResult) || hasExtraCopies(lineResult);
}

function renderLineResult(lineResult) {
  const li = document.createElement('li');
  li.className = 'comic-card-wrap';

  const card = document.createElement('div');
  card.className = 'pull-card' + (needsLook(lineResult) ? ' wanted' : '');

  const badge = document.createElement('span');
  badge.className = 'pull-badge ' + (needsLook(lineResult) ? 'wanted' : 'corralled');
  badge.textContent = needsLook(lineResult) ? '!' : '✓';
  card.appendChild(badge);

  const info = document.createElement('div');
  info.className = 'pull-info';

  const title = document.createElement('div');
  title.className = 'pull-title';
  title.textContent = lineResult.pastedLine;
  info.appendChild(title);

  if (lineResult.productCode) {
    const code = document.createElement('div');
    code.className = 'pull-meta';
    code.textContent = `${lineResult.productCode}${lineResult.quantity != null ? ` · Qty ${lineResult.quantity}` : ''}`;
    info.appendChild(code);
  }

  if (hasExtraCopies(lineResult)) {
    const qtyWarning = document.createElement('div');
    qtyWarning.className = 'pull-meta';
    qtyWarning.textContent = `${lineResult.quantity} copies in the cart — set it to 1 unless you meant to buy extras.`;
    info.appendChild(qtyWarning);
  }

  const meta = document.createElement('div');
  meta.className = 'pull-meta';
  meta.textContent = hasDuplicate(lineResult)
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
    renderResults(results, `Checked ${results.length} line${results.length === 1 ? '' : 's'}`);
  } catch (err) {
    console.error('[cart-check] check failed', err);
    showMessage('Check failed - see the console.', true);
  }
}

// Flagged lines first, so anything to fix is at the top of a long cart.
function renderResults(results, summaryPrefix) {
  const flagged = results.filter(needsLook);
  const duplicates = results.filter(hasDuplicate).length;
  const extraCopies = results.filter(hasExtraCopies).length;
  const problems = [];
  if (duplicates > 0) problems.push(`${duplicates} may already be ordered`);
  if (extraCopies > 0) problems.push(`${extraCopies} ${extraCopies === 1 ? 'has' : 'have'} more than one copy`);
  showMessage(
    problems.length > 0
      ? `${summaryPrefix} — ${problems.join(', ')}. See below.`
      : `${summaryPrefix} — nothing already ordered${results.some((r) => r.quantity != null) ? ', one copy each' : ''}.`,
    problems.length > 0);

  // The check just saved this cart, so the Candidates tab now shows what's in it.
  candidatesLink.hidden = false;

  resultsList.innerHTML = '';
  for (const result of [...flagged, ...results.filter((r) => !needsLook(r))]) {
    resultsList.appendChild(renderLineResult(result));
  }

  // Results sit below the paste box - bring the summary up under the sticky tab bar.
  const navHeight = document.querySelector('.trail-nav')?.offsetHeight ?? 0;
  window.scrollTo({ top: message.getBoundingClientRect().top + window.scrollY - navHeight - 8 });
}

async function checkPdf(file) {
  resultsList.innerHTML = '';
  const formData = new FormData();
  formData.append('file', file);
  const res = await fetch('/api/orders/check-cart-pdf', { method: 'POST', body: formData });
  if (!res.ok) {
    const problem = await res.text().catch(() => '');
    showMessage(problem || `Upload failed (${res.status}).`, true);
    return;
  }
  const results = await res.json();
  log('pdf results', results.length);
  const copies = results.reduce((sum, r) => sum + (r.quantity ?? 0), 0);
  renderResults(results, `Read ${results.length} item${results.length === 1 ? '' : 's'} (${copies} ${copies === 1 ? 'copy' : 'copies'}) from the PDF`);
}

pdfBtn.addEventListener('click', async () => {
  const file = pdfInput.files[0];
  if (!file) {
    showMessage('Choose the cart PDF first.', true);
    return;
  }
  pdfBtn.disabled = true;
  showMessage('Reading the PDF…', false);
  try {
    await checkPdf(file);
  } catch (err) {
    console.error('[cart-check] pdf check failed', err);
    showMessage('Check failed - see the console.', true);
  } finally {
    pdfBtn.disabled = false;
  }
});

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
