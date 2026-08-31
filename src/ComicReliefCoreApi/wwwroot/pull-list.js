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
  const corralledHeading = document.getElementById("corralledHeading");
  const wantedHeading = document.getElementById("wantedHeading");
  const unresolvedHeading = document.getElementById("unresolvedHeading");

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

  function renderCard(entry) {
    const li = document.createElement("li");
    const card = document.createElement("div");
    card.className = "pull-card" + (entry.status === "Unsticky" ? " wanted" : "");

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
    statusEl.textContent = "Rounding up your pull list…";
    try {
      const res = await fetch("/api/pulllist", { cache: "no-store" });
      if (!res.ok) throw new Error(`Request failed (${res.status})`);
      const entries = await res.json();

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
