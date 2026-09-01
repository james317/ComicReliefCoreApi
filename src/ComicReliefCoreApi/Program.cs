using System.Text.Json.Serialization;
using ComicReliefCoreApi.Api.Configuration;
using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Services.Clz;
using ComicReliefCoreApi.Api.Services.Dcbs;
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
            "RefreshedAt" TEXT NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS \"IX_DcbsSolicitationEntries_Publisher\" ON \"DcbsSolicitationEntries\" (\"Publisher\")");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
