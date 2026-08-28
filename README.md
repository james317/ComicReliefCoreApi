# Comic Relief (API) — "If You Pull, Don't Miss" (app)

`comic-relief-api` is the ASP.NET Core backend: it lists comics shipping in a
given month (defaulting to "month after next"), backed by the
[Comic Vine](https://comicvine.gamespot.com/api/) API. The same process also
serves **If You Pull, Don't Miss** — a small, western-themed mobile web page
that calls that API and that you can add to your iPhone's Home Screen and use
like an app. The name/theming is aimed at its planned next feature: tracking
your comic pull list so nothing on it slips by.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A free Comic Vine API key: sign up at https://comicvine.gamespot.com/api/

## Configure your API key

Don't put your key in `appsettings.json` (it's committed to git). Use one of:

**User secrets (recommended for local dev):**

```bash
cd src/ComicReliefCoreApi
dotnet user-secrets init
dotnet user-secrets set "ComicVine:ApiKey" "YOUR_KEY_HERE"
```

**Environment variable (works anywhere, e.g. when deploying):**

```bash
export ComicVine__ApiKey="YOUR_KEY_HERE"
```

## Run it

```bash
cd src/ComicReliefCoreApi
dotnet run
```

The app listens on `http://localhost:5000`. Open that URL in a browser to see
the mobile page, or hit the API directly:

```
GET /api/comics/upcoming            # comics shipping "month after next"
GET /api/comics/upcoming?year=2026&month=12
```

## Use it on your iPhone

1. Deploy this somewhere reachable from your phone (see below), or run it on
   your Mac and open `http://<your-mac's-LAN-IP>:5000` from Safari on your
   iPhone while on the same Wi-Fi.
2. In Safari, tap the Share icon → **Add to Home Screen**.
3. Launch it from the Home Screen icon — it opens full-screen, like an app,
   with no browser chrome.

For access away from your home Wi-Fi, deploy the API somewhere public (see
"Deploy to Fly.io" below) and point the Home Screen icon at that URL instead.

## Deploy to Fly.io

The repo includes a `Dockerfile` and `fly.toml` configured to **scale to
zero**: the machine stops when nothing's hit it for a while and starts back
up on the next request, so a personal, occasionally-checked app like this
costs pennies a month instead of paying for an always-on server. The first
request after a period of idleness takes a few extra seconds while the
machine wakes up — expected, not a bug.

Deploys are automated with GitHub Actions
(`.github/workflows/fly-deploy.yml`): every push to `master` builds and
deploys automatically. There's a one-time setup step that needs a real
terminal — [GitHub Codespaces](https://github.com/codespaces) works fine
from Safari on an iPhone (Code → Codespaces → Create codespace on this
repo), so this whole flow can be done without a computer:

1. **In the Codespace terminal**, install flyctl and sign up/in:
   ```bash
   curl -L https://fly.io/install.sh | sh
   export FLYCTL_INSTALL="$HOME/.fly"
   export PATH="$FLYCTL_INSTALL/bin:$PATH"
   fly auth signup   # or `fly auth login` if you already made an account
   ```
2. **Mint a deploy token** and copy the value it prints:
   ```bash
   fly tokens create org -o personal
   ```
3. **Add two repository secrets** on GitHub (Settings → Secrets and
   variables → Actions → New repository secret — works fine from the
   GitHub website or app on your phone, no CLI needed):
   - `FLY_API_TOKEN` — the token from step 2
   - `COMICVINE_API_KEY` — your Comic Vine key
4. **Push to `master`** (merge this branch into it). The workflow creates
   the Fly app the first time it runs, stages your API key as a Fly
   secret, and deploys — watch it under the repo's **Actions** tab.
5. **Find the URL**: the workflow log's `flyctl deploy` step prints it, or
   run `fly status -a comic-relief-api` (the app name from `fly.toml`) in
   the Codespace. Open it on your iPhone in Safari and Add to Home Screen,
   per the steps above.

If `comic-relief-api` in `fly.toml` is already taken by someone else
globally, the deploy step fails with a name-conflict error — edit that one
line to something unique (GitHub's web file editor works fine for this) and
push again.

To confirm auto-stop is working, leave the app idle for a few minutes, then
`fly machine list` — the machine should show as `stopped`. It restarts
automatically on the next incoming request.

Prefer to do it by hand from a computer instead of via GitHub Actions? Skip
the workflow and repository secrets, and just run `fly launch --no-deploy`,
`fly secrets set ComicVine__ApiKey="YOUR_KEY_HERE"`, and `fly deploy` from
the repo root.

## How it works

- `Services/ComicVineService.cs` queries Comic Vine's `/issues/` endpoint,
  filtering by `store_date` for the target month, paging through results
  (Comic Vine returns up to 100 issues per page and rate-limits to roughly
  one request/second, which the service respects).
- `Controllers/ComicsController.cs` exposes `GET /api/comics/upcoming`,
  defaulting the target month to today + 2 months.
- `wwwroot/` is a static, dependency-free HTML/CSS/JS page that calls the API
  and renders results grouped by ship date, with month navigation.

## Project layout

```
.github/workflows/fly-deploy.yml    Auto-deploy to Fly.io on push to master
Dockerfile                          Container build for deployment (e.g. Fly.io)
fly.toml                            Fly.io app config with auto-stop/auto-start
src/ComicReliefCoreApi/
  Controllers/ComicsController.cs   API endpoint
  Services/ComicVineService.cs      Comic Vine client
  Models/                           DTOs and response shapes
  Configuration/ComicVineOptions.cs API key + paging config
  wwwroot/                          Mobile web page (index.html, app.js, styles.css, icons)
```
