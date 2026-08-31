(() => {
  const addForm = document.getElementById("addForm");
  const titleInput = document.getElementById("titleInput");
  const addBtn = document.getElementById("addBtn");
  const message = document.getElementById("message");
  const statusEl = document.getElementById("status");
  const groups = document.getElementById("groups");
  const corralledList = document.getElementById("corralledList");
  const wantedList = document.getElementById("wantedList");
  const unresolvedList = document.getElementById("unresolvedList");
  const archivedList = document.getElementById("archivedList");
  const corralledHeading = document.getElementById("corralledHeading");
  const wantedHeading = document.getElementById("wantedHeading");
  const unresolvedHeading = document.getElementById("unresolvedHeading");
  const archivedHeading = document.getElementById("archivedHeading");
  const archivedGroup = document.getElementById("archivedGroup");
  const pageHeading = document.getElementById("pageHeading");
  const pageIntro = document.getElementById("pageIntro");
  const viewToggleLink = document.getElementById("viewToggleLink");
  const selectModeBtn = document.getElementById("selectModeBtn");
  const bulkBar = document.getElementById("bulkBar");
  const bulkCount = document.getElementById("bulkCount");
  const bulkActionBtn = document.getElementById("bulkActionBtn");

  const isBootHill = new URLSearchParams(location.search).get("archived") === "true";

  const activeGroupIds = ["corralledHeading", "wantedHeading", "unresolvedHeading"].map((id) =>
    document.getElementById(id).closest(".posse-group"));

  if (isBootHill) {
    pageHeading.textContent = "Boot Hill";
    pageIntro.innerHTML =
      "Titles marked done for good — a finished series, a one-time item. " +
      "They're only hidden here; nothing was touched on DCBS itself.";
    viewToggleLink.textContent = "← Back to the Pull List";
    viewToggleLink.href = "/pull-list.html";
    addForm.hidden = true;
    activeGroupIds.forEach((el) => { el.hidden = true; });
  } else {
    archivedGroup.hidden = true;
  }

  let selectMode = false;
  const selectedIds = new Set();

  function showMessage(text, isError) {
    message.textContent = text ?? "";
    message.className = "message" + (text ? (isError ? " error" : " success") : "");
  }

  function formatDate(value) {
    return value ? new Date(value).toLocaleString() : null;
  }

  function metaLine(entry) {
    const bits = [];
    if (entry.status === "Sticky" && entry.lastVerifiedStickyAt) {
      bits.push(`Confirmed on your DCBS pull list ${formatDate(entry.lastVerifiedStickyAt)}`);
    }
    if (entry.status === "Unsticky" && entry.failureReason) {
      bits.push(entry.failureReason);
    }
    if (entry.status === "Unresolved") {
      bits.push("Not attempted yet");
    }
    if (entry.dcbsSeriesCode) {
      bits.push(`Series code ${entry.dcbsSeriesCode}`);
    }
    return bits.join(" · ");
  }

  function badgeFor(entry) {
    if (entry.status === "Sticky") return { cls: "corralled", label: "✓" };
    if (entry.status === "Unsticky") return { cls: "wanted", label: "!" };
    return { cls: "unresolved", label: "?" };
  }

  function updateBulkBar() {
    const n = selectedIds.size;
    if (!selectMode || n === 0) {
      bulkBar.hidden = true;
      return;
    }
    bulkBar.hidden = false;
    bulkCount.textContent = `${n} selected`;
    bulkActionBtn.textContent = isBootHill ? `Return ${n} to the Pull List` : `Send ${n} to Boot Hill`;
  }

  function setSelectMode(on) {
    selectMode = on;
    selectModeBtn.textContent = on ? "Cancel" : "Select";
    if (!on) selectedIds.clear();
    updateBulkBar();
    loadList();
  }

  selectModeBtn.addEventListener("click", () => setSelectMode(!selectMode));

  bulkActionBtn.addEventListener("click", async () => {
    const titles = currentEntries.filter((e) => selectedIds.has(e.id)).map((e) => e.title);
    const n = titles.length;
    if (n === 0) return;

    const preview = titles.slice(0, 10).map((t) => `- ${t}`).join("\n") + (n > 10 ? `\n…and ${n - 10} more` : "");
    const question = isBootHill
      ? `Return ${n} title${n === 1 ? "" : "s"} to the Pull List?\n\n${preview}`
      : `Send ${n} title${n === 1 ? "" : "s"} to Boot Hill?\n\n${preview}\n\nSticky titles stay exactly as-is on your real DCBS pull list - this only changes what shows here.`;
    if (!confirm(question)) return;

    bulkActionBtn.disabled = true;
    try {
      const action = isBootHill ? "unarchive" : "archive";
      const results = await Promise.all(
        [...selectedIds].map((id) =>
          fetch(`/api/pulllist/${id}/${action}`, { method: "POST" }).then((res) => ({ id, ok: res.ok }))));
      const failed = results.filter((r) => !r.ok);
      selectedIds.clear();
      setSelectMode(false);
      if (failed.length > 0) {
        showMessage(`${failed.length} of ${n} didn't go through - try again.`, true);
      } else {
        showMessage(isBootHill ? `Returned ${n} title${n === 1 ? "" : "s"} to the Pull List.` : `Sent ${n} title${n === 1 ? "" : "s"} to Boot Hill.`, false);
      }
    } catch (err) {
      showMessage(`Something went wrong: ${err.message}`, true);
    } finally {
      bulkActionBtn.disabled = false;
    }
  });

  let currentEntries = [];

  function renderCard(entry) {
    const li = document.createElement("li");
    const card = document.createElement("div");
    card.className = "pull-card" + (entry.status === "Unsticky" ? " wanted" : "");

    if (selectMode) {
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.className = "pull-checkbox";
      checkbox.checked = selectedIds.has(entry.id);
      checkbox.addEventListener("change", () => {
        if (checkbox.checked) selectedIds.add(entry.id);
        else selectedIds.delete(entry.id);
        updateBulkBar();
      });
      card.appendChild(checkbox);
    }

    const badge = document.createElement("span");
    const b = badgeFor(entry);
    badge.className = `pull-badge ${b.cls}`;
    badge.textContent = b.label;

    const info = document.createElement("div");
    info.className = "pull-info";

    const title = document.createElement("div");
    title.className = "pull-title";
    title.textContent = entry.title;
    info.appendChild(title);

    const meta = metaLine(entry);
    if (meta) {
      const metaEl = document.createElement("div");
      metaEl.className = "pull-meta";
      metaEl.textContent = meta;
      info.appendChild(metaEl);
    }

    card.appendChild(badge);
    card.appendChild(info);

    li.appendChild(card);
    return li;
  }

  async function loadList() {
    statusEl.hidden = false;
    groups.hidden = true;
    statusEl.textContent = isBootHill ? "Riding out to Boot Hill…" : "Rounding up your pull list…";
    try {
      const res = await fetch(`/api/pulllist?archived=${isBootHill}`, { cache: "no-store" });
      if (!res.ok) throw new Error(`Request failed (${res.status})`);
      const entries = await res.json();
      currentEntries = entries;

      if (isBootHill) {
        archivedList.innerHTML = "";
        archivedHeading.textContent = `Boot Hill (${entries.length})`;
        entries.forEach((e) => archivedList.appendChild(renderCard(e)));

        if (entries.length === 0) {
          statusEl.hidden = false;
          groups.hidden = true;
          statusEl.textContent = "Nothing archived yet.";
        } else {
          statusEl.hidden = true;
          groups.hidden = false;
        }
        return;
      }

      corralledList.innerHTML = "";
      wantedList.innerHTML = "";
      unresolvedList.innerHTML = "";

      const corralled = entries.filter((e) => e.status === "Sticky");
      const wanted = entries.filter((e) => e.status === "Unsticky");
      const unresolved = entries.filter((e) => e.status === "Unresolved");

      corralledHeading.textContent = `Corralled (${corralled.length})`;
      wantedHeading.textContent = `Still Wanted (${wanted.length})`;
      unresolvedHeading.textContent = `Just Rode In (${unresolved.length})`;

      corralled.forEach((e) => corralledList.appendChild(renderCard(e)));
      wanted.forEach((e) => wantedList.appendChild(renderCard(e)));
      unresolved.forEach((e) => unresolvedList.appendChild(renderCard(e)));

      if (entries.length === 0) {
        statusEl.hidden = false;
        groups.hidden = true;
        statusEl.textContent = "No titles tracked yet — add one above.";
      } else {
        statusEl.hidden = true;
        groups.hidden = false;
      }
    } catch (err) {
      statusEl.hidden = false;
      groups.hidden = true;
      statusEl.textContent = `Couldn't load the pull list: ${err.message}`;
      statusEl.classList.add("error");
    }
  }

  addForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const title = titleInput.value.trim();
    if (!title) {
      showMessage("Enter a title first.", true);
      return;
    }
    addBtn.disabled = true;
    showMessage(`Trying to corral "${title}"…`, false);
    try {
      const res = await fetch("/api/pulllist/add", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ title }),
      });
      if (!res.ok) throw new Error(`Request failed (${res.status})`);
      const entry = await res.json();
      if (entry.status === "Sticky") {
        showMessage(`"${entry.title}" is corralled on your DCBS pull list.`, false);
      } else if (entry.status === "Unsticky") {
        showMessage(`"${entry.title}" wouldn't stick — tracked as Still Wanted. ${entry.failureReason ?? ""}`, true);
      } else {
        showMessage(`"${entry.title}" added — status pending.`, false);
      }
      titleInput.value = "";
      await loadList();
    } catch (err) {
      showMessage(`Something went wrong: ${err.message}`, true);
    } finally {
      addBtn.disabled = false;
    }
  });

  loadList();
})();
