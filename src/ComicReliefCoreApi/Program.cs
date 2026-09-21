using System.Text.Json.Serialization;
using ComicReliefCoreApi.Api.Configuration;
using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Services.Clz;
using ComicReliefCoreApi.Api.Services.Dcbs;
using ComicReliefCoreApi.Api.Services.ReadingLog;
using ComicReliefCoreApi.App.Services;
using ComicReliefCoreApi.Configuration;
using ComicReliefCoreApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ComicVineOptions>(builder.Configuration.GetSection("ComicVine"));
builder.Services.AddHttpClient<IComicVineService, ComicVineService>();

builder.Services.Configure<DcbsOptions>(builder.Configuration.GetSection("Dcbs"));
builder.Services.AddScoped<IDcbsSessionStore, DcbsSessionStore>();
builder.Services.AddHttpClient<IDcbsClient, DcbsClient>();
builder.Services.AddScoped<IDcbsSessionManager, DcbsSessionManager>();
builder.Services.AddScoped<IPullListService, PullListService>();
builder.Services.AddScoped<IClzImportStore, ClzImportStore>();
builder.Services.AddScoped<IClzCollectionService, ClzCollectionService>();

// Both Scoped (not Singleton): the crawl is now persisted via IDcbsSolicitationStore
// rather than cached in memory, so there's no in-process state that needs to outlive a
// request - and a Scoped DbContext-backed store couldn't be safely held by a Singleton
// anyway. Persisting means a code deploy no longer wipes crawled data (see docs/BACKLOG.md).
builder.Services.AddScoped<IDcbsSolicitationStore, DcbsSolicitationStore>();
builder.Services.AddScoped<ISolicitationService, SolicitationService>();
builder.Services.AddScoped<IDcbsOrderSnapshotStore, DcbsOrderSnapshotStore>();
builder.Services.AddScoped<IOrderSnapshotService, OrderSnapshotService>();
builder.Services.AddScoped<IIssueContinuityService, IssueContinuityService>();
builder.Services.AddScoped<IShipmentTrackingService, ShipmentTrackingService>();
builder.Services.AddScoped<IReadIssueStore, ReadIssueStore>();
builder.Services.AddScoped<IReadingLogService, ReadingLogService>();
builder.Services.AddScoped<IReviewFlagService, ReviewFlagService>();
builder.Services.AddScoped<IWriterPreferenceService, WriterPreferenceService>();

// SQLite path comes from config (appsettings.json locally, the Data__SqlitePath env var
// in fly.toml for production) so it can point at the Fly volume mount without code
// changes - see fly.toml for the mount and README.md for the one-time volume setup.
var dbPath = builder.Configuration["Data:SqlitePath"] ?? "comicrelief.db";
builder.Services.AddDbContext<ComicReliefDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ComicReliefDbContext>();
    // No dotnet-ef tooling was available to generate a tracked migration when this was
    // written - EnsureCreated() builds the schema directly from the model instead. If
    // real migrations are added later, switch this to db.Database.Migrate().
    db.Database.EnsureCreated();

    // EnsureCreated() only builds the schema for a brand-new database - it does NOT apply
    // incremental changes to one that already exists, which the production database on the
    // Fly volume now does (it has real imported pull-list data). Add new columns by hand
    // like this instead of just editing the model, or EF throws "no such column" at query
    // time against the live database. Safe to run on every startup: a duplicate-column
    // error just means a previous startup already applied it.
    try
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE PullListEntries ADD COLUMN ArchivedAt TEXT NULL");
    }
    catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
    {
        // Already applied.
    }

    // Same EnsureCreated() limitation, but for a whole new table (ClzSeriesSummaries) rather
    // than a column - EnsureCreated() no-ops entirely once the database file already exists,
    // so a brand-new table added to the model afterward never gets created on its own.
    // "IF NOT EXISTS" makes this safe to run on every startup without a try/catch.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "ClzSeriesSummaries" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ClzSeriesSummaries" PRIMARY KEY AUTOINCREMENT,
            "Series" TEXT NOT NULL,
            "NormalizedSeries" TEXT NOT NULL,
            "LastReleaseDate" TEXT NULL,
            "IssueCount" INTEGER NOT NULL,
            "ImportedAt" TEXT NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_ClzSeriesSummaries_NormalizedSeries\" ON \"ClzSeriesSummaries\" (\"NormalizedSeries\")");

    // Same EnsureCreated() limitation, third occurrence: DcbsSolicitationEntries persists
    // the crawled-solicitations data that used to live only in an in-memory cache (wiped by
    // every deploy). No unique index needed - rows are replaced per-publisher wholesale
    // (see DcbsSolicitationStore.ReplacePublisherAsync), never upserted by key.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DcbsSolicitationEntries" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_DcbsSolicitationEntries" PRIMARY KEY AUTOINCREMENT,
            "Publisher" TEXT NOT NULL,
            "ProductCode" TEXT NOT NULL,
            "Title" TEXT NOT NULL,
            "ProductUrl" TEXT NOT NULL,
            "ThumbnailUrl" TEXT NULL,
            "CreatorsAndDescription" TEXT NULL,
            "Price" TEXT NULL,
            "IsRelisted" INTEGER NOT NULL,
            "IsFacsimileOrReprint" INTEGER NOT NULL DEFAULT 0,
            "RefreshedAt" TEXT NOT NULL,
            "FirstSeenAt" TEXT NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS \"IX_DcbsSolicitationEntries_Publisher\" ON \"DcbsSolicitationEntries\" (\"Publisher\")");

    // DcbsSolicitationEntries already shipped and deployed once without this column - the
    // CREATE TABLE IF NOT EXISTS above no-ops against that existing table, so it needs the
    // same hand-written ALTER TABLE treatment as PullListEntries.ArchivedAt above.
    try
    {
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE DcbsSolicitationEntries ADD COLUMN IsFacsimileOrReprint INTEGER NOT NULL DEFAULT 0");
    }
    catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
    {
        // Already applied.
    }

    // Same gap, second column: FirstSeenAt (see DcbsSolicitationEntry) needs a real per-row
    // value, not a shared constant, to support the "new since last refresh"/"new since order
    // placed" views - SQLite's ALTER TABLE ADD COLUMN only accepts a constant DEFAULT on a
    // non-empty table, so this adds the column with a fixed sentinel far in the past
    // (deliberately never touched again) rather than trying to guess a real date for
    // already-solicited rows. A sentinel has to be older than any real date for both
    // consumers of this column: it must never equal a real RefreshedAt (or every legacy row
    // reads as "new since last refresh" - the bug an earlier version of this migration
    // actually shipped, backfilling to RefreshedAt itself, which trivially made the equality
    // true for every single row), and it must sort before any real order-placed date (so
    // legacy rows don't also flood "new since order").
    try
    {
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE DcbsSolicitationEntries ADD COLUMN FirstSeenAt TEXT NOT NULL DEFAULT '2000-01-01T00:00:00.0000000'");
    }
    catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
    {
        // Already applied.
    }

    // One-time correction for the bug described above, which did ship and run once against
    // production before being caught live (every row backfilled to FirstSeenAt = RefreshedAt
    // instead of a sentinel). Safe to leave in permanently: bounded by a fixed past cutoff,
    // so it can only ever match rows poisoned by that specific already-happened bug, never a
    // real future refresh's genuinely-equal FirstSeenAt/RefreshedAt pair.
    db.Database.ExecuteSqlRaw(
        "UPDATE DcbsSolicitationEntries SET FirstSeenAt = '2000-01-01T00:00:00.0000000' " +
        "WHERE FirstSeenAt = RefreshedAt AND RefreshedAt < '2026-09-21T00:00:00.0000000'");

    // Same EnsureCreated() limitation, fourth occurrence: DcbsOrderSnapshotLines persists
    // every synced order (see IOrderSnapshotService) so a candidates rescan can flag
    // "matches your pull list, not in anything you've ordered."
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DcbsOrderSnapshotLines" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_DcbsOrderSnapshotLines" PRIMARY KEY AUTOINCREMENT,
            "OrderId" TEXT NOT NULL,
            "ProductCode" TEXT NOT NULL,
            "Title" TEXT NOT NULL,
            "Status" INTEGER NULL,
            "SyncedAt" TEXT NOT NULL
        )
        """);

    // DcbsOrderSnapshotLines already shipped and deployed once without this column - same
    // ALTER TABLE treatment as IsFacsimileOrReprint above. Stores the DcbsShipmentStatus enum
    // as its underlying int (EF's default enum mapping), nullable for the pre-existing rows
    // synced before this column existed as well as the free items that never carry a status.
    try
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE DcbsOrderSnapshotLines ADD COLUMN Status INTEGER NULL");
    }
    catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
    {
        // Already applied.
    }

    // Same gap, four more columns: Quantity/UnitPrice/ThumbnailUrl/OrderDate (see
    // DcbsOrderSnapshotLine) for the order-search feature - all genuinely optional facts
    // (null for the free-item rows, or until the next order sync populates them), so unlike
    // FirstSeenAt above, a plain nullable ALTER TABLE with no backfill is correct as-is: an
    // existing row just shows blank details until re-synced, never a wrong value.
    foreach (var (column, sqlType) in new[]
             {
                 ("Quantity", "INTEGER"), ("UnitPrice", "TEXT"), ("ThumbnailUrl", "TEXT"), ("OrderDate", "TEXT"),
             })
    {
        try
        {
            db.Database.ExecuteSqlRaw($"ALTER TABLE DcbsOrderSnapshotLines ADD COLUMN {column} {sqlType} NULL");
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Already applied.
        }
    }

    // Same EnsureCreated() limitation, fifth occurrence: ClzIssueReleases persists per-issue
    // release dates from a CLZ export (see ClzCsvParser.ParsePerIssueRows) - a separate table
    // from the per-series ClzSeriesSummaries aggregate, needed to match a shipment's specific
    // issues to when they actually came out rather than only the series' latest release.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "ClzIssueReleases" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ClzIssueReleases" PRIMARY KEY AUTOINCREMENT,
            "Series" TEXT NOT NULL,
            "NormalizedSeries" TEXT NOT NULL,
            "IssueNumber" INTEGER NOT NULL,
            "ReleaseDate" TEXT NULL,
            "ImportedAt" TEXT NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS \"IX_ClzIssueReleases_NormalizedSeries\" ON \"ClzIssueReleases\" (\"NormalizedSeries\")");

    // One-time cleanup for a real incident: before ParsePerIssueRows deduped its own output,
    // a full collection export whose owner has multiple covers of the same issue (confirmed
    // real and common) produced duplicate (NormalizedSeries, IssueNumber) rows here, which
    // then crashed the next upsert's dictionary build. Idempotent - a no-op once already clean.
    db.Database.ExecuteSqlRaw("""
        DELETE FROM ClzIssueReleases
        WHERE Id NOT IN (
            SELECT MIN(Id) FROM ClzIssueReleases GROUP BY NormalizedSeries, IssueNumber
        )
        """);

    // Same EnsureCreated() limitation, sixth occurrence: ReadIssues persists the reading log
    // (see IReadingLogService) - a guard against reading out of release order or accidentally
    // skipping an issue. ReadAt is nullable (a backfilled entry with no known real date - Id
    // insertion order is what carries reading order for those, see ReadIssue's own docs).
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "ReadIssues" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ReadIssues" PRIMARY KEY AUTOINCREMENT,
            "Series" TEXT NOT NULL,
            "NormalizedSeries" TEXT NOT NULL,
            "IssueNumber" INTEGER NOT NULL,
            "ReadAt" TEXT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS \"IX_ReadIssues_NormalizedSeries_IssueNumber\" ON \"ReadIssues\" (\"NormalizedSeries\", \"IssueNumber\")");

    // Same EnsureCreated() limitation, seventh occurrence: ReviewFlagEntries persists titles
    // flagged "not enough info yet" from a solicitation card (see IReviewFlagService) - a
    // reminder to revisit before the DCBS order-edit cutoff, not a pull-list decision.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "ReviewFlagEntries" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ReviewFlagEntries" PRIMARY KEY AUTOINCREMENT,
            "Title" TEXT NOT NULL,
            "Publisher" TEXT NULL,
            "ProductCode" TEXT NULL,
            "ProductUrl" TEXT NULL,
            "Notes" TEXT NULL,
            "FlaggedAt" TEXT NOT NULL,
            "ResolvedAt" TEXT NULL
        )
        """);

    // Same EnsureCreated() limitation, eighth occurrence: WriterPreferences persists the
    // favorite/avoid writer lists (see IWriterPreferenceService). Type stored as its string
    // name (HasConversion<string>() in ComicReliefDbContext), same as every other enum here.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "WriterPreferences" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_WriterPreferences" PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL,
            "NormalizedName" TEXT NOT NULL,
            "Type" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_WriterPreferences_NormalizedName\" ON \"WriterPreferences\" (\"NormalizedName\")");
}

app.UseDefaultFiles();
// This app is deployed and re-checked live constantly during active development - without
// this, a browser (an iOS home-screen PWA especially - confirmed live 9/2026 to keep
// serving stale HTML/JS across a full close-and-reopen, worse than a normal Safari tab)
// can keep serving old pages after a deploy with no visible sign anything's wrong.
// no-cache (tried first) still permits caching and just asks for revalidation - not
// strong enough here, so this is no-store: don't cache the response at all. Note this
// only affects requests made from here on - a client already holding a stale copy needs
// a real cache-clear (or a fresh URL) to see it, no server-side header can reach back and
// un-cache what's already stuck on a device.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers["Cache-Control"] = "no-store",
});

app.MapControllers();

app.Run();
