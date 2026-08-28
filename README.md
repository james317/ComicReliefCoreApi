# Comic Relief

An ASP.NET Core API that lists comics shipping in a given month (defaulting to
"month after next"), backed by the [Comic Vine](https://comicvine.gamespot.com/api/)
API. It also serves a small mobile-friendly web page you can add to your
iPhone's Home Screen and use like an app.

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

1. Install `flyctl` and sign in: https://fly.io/docs/flyctl/install/, then
   `fly auth login`.
2. From the repo root, run `fly launch --no-deploy`. It detects `fly.toml`
   and the `Dockerfile`, and will offer to pick a unique app name and region
   for you (accept its rewrite of `fly.toml`, which keeps the
   `http_service`/`vm` settings from this repo).
3. Set your API key as a Fly secret (never commit it):
   ```bash
   fly secrets set ComicVine__ApiKey="YOUR_KEY_HERE"
   ```
4. Deploy: `fly deploy`
5. Find your app's URL: `fly status`. Open it on your iPhone in Safari and
   Add to Home Screen, per the steps above.

To confirm auto-stop is working, leave the app idle for a few minutes, then
`fly machine list` — the machine should show as `stopped`. It restarts
automatically on the next incoming request.

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
Dockerfile                          Container build for deployment (e.g. Fly.io)
fly.toml                            Fly.io app config with auto-stop/auto-start
src/ComicReliefCoreApi/
  Controllers/ComicsController.cs   API endpoint
  Services/ComicVineService.cs      Comic Vine client
  Models/                           DTOs and response shapes
  Configuration/ComicVineOptions.cs API key + paging config
  wwwroot/                          Mobile web page (index.html, app.js, styles.css, icons)
```
