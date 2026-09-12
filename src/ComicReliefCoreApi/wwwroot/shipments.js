const log = (...args) => console.log('[shipments]', ...args);

const shipmentList = document.getElementById('shipmentList');
const shipmentDetail = document.getElementById('shipmentDetail');
const shipmentDetailHeading = document.getElementById('shipmentDetailHeading');
const readingOrder = document.getElementById('readingOrder');
const clzShipmentFileInput = document.getElementById('clzShipmentFileInput');
const clzShipmentUploadBtn = document.getElementById('clzShipmentUploadBtn');
const clzShipmentUploadResult = document.getElementById('clzShipmentUploadResult');
const orderStatusDot = document.getElementById('orderStatusDot');
const orderStatusText = document.getElementById('orderStatusText');
const syncOrderBtn = document.getElementById('syncOrderBtn');
const message = document.getElementById('message');
const missedIssuesList = document.getElementById('missedIssuesList');

let selectedShipmentId = null;

function showMessage(text, isError) {
  message.textContent = text;
  message.className = 'message ' + (isError ? 'error' : 'success');
}

function formatDateTime(value) {
  return value ? new Date(value).toLocaleString() : null;
}

async function loadShipments() {
  try {
    const res = await fetch('/api/shipments?max=12');
    const shipments = await res.json();
    log('shipments loaded', shipments.length);
    renderShipmentList(shipments);
    if (shipments.length > 0) {
      await selectShipment(shipments[0].shipmentId, shipments[0]);
    }
  } catch (err) {
    log('loadShipments failed', err);
    shipmentList.innerHTML = '<li>Could not reach DCBS - check the DCBS Session tab.</li>';
  }
}

function renderShipmentList(shipments) {
  shipmentList.innerHTML = '';
  for (const shipment of shipments) {
    const li = document.createElement('li');
    li.className = 'pull-card shipment-row';
    li.dataset.shipmentId = shipment.shipmentId;

    const info = document.createElement('div');
    info.className = 'pull-info';
    const title = document.createElement('div');
    title.className = 'pull-title';
    title.textContent = `Packlist ${shipment.packlistNumber}`;
    const sub = document.createElement('div');
    sub.textContent = `Shipped ${shipment.shippedAt}`;
    info.appendChild(title);
    info.appendChild(sub);

    li.appendChild(info);
    li.addEventListener('click', () => selectShipment(shipment.shipmentId, shipment));
    shipmentList.appendChild(li);
  }
}

function markActiveRow() {
  for (const row of shipmentList.querySelectorAll('.shipment-row')) {
    row.classList.toggle('active', row.dataset.shipmentId === selectedShipmentId);
  }
}

async function selectShipment(shipmentId, shipment) {
  selectedShipmentId = shipmentId;
  markActiveRow();
  shipmentDetailHeading.textContent = shipment
    ? `Packlist ${shipment.packlistNumber} — shipped ${shipment.shippedAt}`
    : `Shipment ${shipmentId}`;
  shipmentDetail.hidden = false;
  await loadReadingOrder(shipmentId);
}

async function loadReadingOrder(shipmentId) {
  readingOrder.innerHTML = '<p>Loading…</p>';
  try {
    const res = await fetch(`/api/shipments/${shipmentId}/reading-order`);
    const order = await res.json();
    renderReadingOrder(order);
  } catch (err) {
    log('loadReadingOrder failed', err);
    readingOrder.innerHTML = '<p>Could not load reading order.</p>';
  }
}

function renderReadingOrder(order) {
  readingOrder.innerHTML = '';
  if (!order.groups || order.groups.length === 0) {
    readingOrder.innerHTML = '<p>No items found on this shipment.</p>';
    return;
  }
  for (const group of order.groups) {
    const div = document.createElement('div');
    div.className = 'reading-group';

    const h4 = document.createElement('h4');
    h4.textContent = group.releaseDate ? group.releaseDate : 'Unknown release date';
    div.appendChild(h4);

    const ul = document.createElement('ul');
    for (const item of group.items) {
      const li = document.createElement('li');
      li.textContent = item.title;
      ul.appendChild(li);
    }
    div.appendChild(ul);
    readingOrder.appendChild(div);
  }
}

clzShipmentUploadBtn.addEventListener('click', async () => {
  const file = clzShipmentFileInput.files[0];
  if (!file) {
    clzShipmentUploadResult.textContent = 'Choose a CSV file first.';
    return;
  }
  log('clz shipment upload: starting', { name: file.name, size: file.size });
  clzShipmentUploadBtn.disabled = true;
  clzShipmentUploadResult.textContent = 'uploading…';
  try {
    const formData = new FormData();
    formData.append('file', file);
    const res = await fetch('/api/clz/import-shipment-issues', { method: 'POST', body: formData });
    if (!res.ok) {
      const problem = await res.text().catch(() => '');
      throw new Error(problem || `Request failed (${res.status})`);
    }
    const result = await res.json();
    log('clz shipment upload: succeeded', result);
    clzShipmentFileInput.value = '';
    clzShipmentUploadResult.textContent = `Updated release dates for ${result.issuesUpserted} issue(s).`;
    if (selectedShipmentId) {
      await loadReadingOrder(selectedShipmentId);
    }
  } catch (err) {
    log('clz shipment upload: failed', err);
    clzShipmentUploadResult.textContent = `Upload failed: ${err.message}`;
  } finally {
    clzShipmentUploadBtn.disabled = false;
  }
});

function renderOrderStatus(status, orderErrors) {
  const errorOrders = Object.keys(orderErrors || {});

  if (!status.lastSyncedAt) {
    orderStatusDot.className = 'status-dot unknown';
    orderStatusText.textContent = 'No orders synced yet - click "Sync Order History & Check" below.';
    return;
  }
  orderStatusDot.className = errorOrders.length > 0 ? 'status-dot invalid' : 'status-dot valid';
  const parts = [
    `Checked against ${status.orderCount} orders (${status.totalLineCount} items total), last synced ${formatDateTime(status.lastSyncedAt)}.`,
  ];
  if (errorOrders.length > 0) {
    parts.push(`Failed to fetch orders: ${errorOrders.join(', ')}.`);
  }
  orderStatusText.textContent = parts.join(' ');
}

function renderMissedIssues(flags) {
  missedIssuesList.innerHTML = '';
  if (flags.length === 0) {
    const li = document.createElement('li');
    li.textContent = 'No gaps found.';
    missedIssuesList.appendChild(li);
    return;
  }
  for (const flag of flags) {
    const li = document.createElement('li');
    li.className = 'pull-card';
    li.textContent = `${flag.pullListTitle} #${flag.missingIssueNumber} - between #${flag.precedingIssueNumber} and #${flag.followingIssueNumber}`;
    missedIssuesList.appendChild(li);
  }
}

async function loadOrderStatus() {
  try {
    const res = await fetch('/api/orders/status');
    const status = await res.json();
    renderOrderStatus(status);
  } catch (err) {
    log('loadOrderStatus failed', err);
    orderStatusText.textContent = 'Could not reach the API.';
  }
}

async function loadMissedIssues() {
  try {
    const res = await fetch('/api/missed-issues');
    const flags = await res.json();
    renderMissedIssues(flags);
  } catch (err) {
    log('loadMissedIssues failed', err);
    missedIssuesList.innerHTML = '<li>Could not load missed-issue check.</li>';
  }
}

syncOrderBtn.addEventListener('click', async () => {
  syncOrderBtn.disabled = true;
  showMessage('Fetching your order history from DCBS - this can take a little while…', false);
  try {
    const res = await fetch('/api/orders/sync-recent', { method: 'POST' });
    const result = await res.json();
    log('order sync complete', result);
    renderOrderStatus(result.status, result.orderErrors);
    showMessage(`Synced ${result.status.orderCount} orders.`, false);
    await loadMissedIssues();
  } catch (err) {
    log('order sync failed', err);
    showMessage('Order sync failed - check the console.', true);
  } finally {
    syncOrderBtn.disabled = false;
  }
});

(async () => {
  await loadShipments();
  await loadOrderStatus();
  await loadMissedIssues();
})();
