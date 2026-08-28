(() => {
  const monthTitle = document.getElementById("monthTitle");
  const subTitle = document.getElementById("subTitle");
  const statusEl = document.getElementById("status");
  const listEl = document.getElementById("comicList");
  const prevBtn = document.getElementById("prevMonth");
  const nextBtn = document.getElementById("nextMonth");

  // null until the first response tells us what "month after next" resolved to.
  let current = null;

  function setStatus(message, isError) {
    statusEl.hidden = message === null;
    listEl.hidden = message !== null;
    statusEl.textContent = message ?? "";
    statusEl.classList.toggle("error", Boolean(isError));
  }

  function formatDate(isoDate) {
    const d = new Date(isoDate + "T00:00:00");
    return d.toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" });
  }

  function render(data) {
    monthTitle.textContent = `${data.monthName} ${data.year}`;
    subTitle.textContent = data.count === 0
      ? "No comics found"
      : `${data.count} comic${data.count === 1 ? "" : "s"}${data.truncated ? " (partial results)" : ""}`;

    if (data.comics.length === 0) {
      setStatus("No comics found for this month yet. Solicitations may not be posted.", false);
      return;
    }

    setStatus(null, false);
    listEl.innerHTML = "";

    let lastDate = null;
    for (const comic of data.comics) {
      const dateKey = comic.storeDate ?? "unknown";
      if (dateKey !== lastDate) {
        lastDate = dateKey;
        const heading = document.createElement("li");
        heading.className = "date-heading";
        heading.textContent = comic.storeDate ? formatDate(comic.storeDate) : "Date TBD";
        listEl.appendChild(heading);
      }

      const item = document.createElement("li");
      const card = document.createElement("a");
      card.className = "comic-card";
      card.href = comic.detailUrl ?? "#";
      card.target = "_blank";
      card.rel = "noopener";

      const img = document.createElement("img");
      img.className = "comic-cover";
      img.loading = "lazy";
      img.alt = "";
      if (comic.coverImageUrl) {
        img.src = comic.coverImageUrl;
      }

      const info = document.createElement("div");
      info.className = "comic-info";

      const title = document.createElement("div");
      title.className = "comic-title";
      title.textContent = comic.title;

      info.appendChild(title);
      card.appendChild(img);
      card.appendChild(info);
      item.appendChild(card);
      listEl.appendChild(item);
    }
  }

  async function load(year, month) {
    setStatus("Loading…", false);
    const params = year && month ? `?year=${year}&month=${month}` : "";

    try {
      const response = await fetch(`/api/comics/upcoming${params}`, { cache: "no-store" });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail ?? `Request failed (${response.status})`);
      }
      const data = await response.json();
      current = { year: data.year, month: data.month };
      render(data);
    } catch (err) {
      setStatus(`Couldn't load comics: ${err.message}`, true);
    }
  }

  function shiftMonth(delta) {
    if (!current) return;
    let { year, month } = current;
    month += delta;
    if (month < 1) { month = 12; year -= 1; }
    if (month > 12) { month = 1; year += 1; }
    load(year, month);
  }

  prevBtn.addEventListener("click", () => shiftMonth(-1));
  nextBtn.addEventListener("click", () => shiftMonth(1));

  load();
})();
