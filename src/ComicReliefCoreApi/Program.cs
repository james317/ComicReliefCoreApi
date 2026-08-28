using ComicReliefCoreApi.Configuration;
using ComicReliefCoreApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<ComicVineOptions>(builder.Configuration.GetSection("ComicVine"));
builder.Services.AddHttpClient<IComicVineService, ComicVineService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
