using System.Text.Json.Serialization;
using ComicReliefCoreApi.Api.Configuration;
using ComicReliefCoreApi.Api.Data;
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
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
