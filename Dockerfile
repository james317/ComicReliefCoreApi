FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Three projects now (see docs/BACKLOG.md's naming section): the web host plus the
# Api/App layers it references. Copy just the .csproj files first so `dotnet restore`
# is cached across builds that don't touch dependencies, same as before the split.
COPY src/ComicReliefCoreApi/ComicReliefCoreApi.csproj src/ComicReliefCoreApi/
COPY src/ComicReliefCoreApi.Api/ComicReliefCoreApi.Api.csproj src/ComicReliefCoreApi.Api/
COPY src/ComicReliefCoreApi.App/ComicReliefCoreApi.App.csproj src/ComicReliefCoreApi.App/
RUN dotnet restore src/ComicReliefCoreApi/ComicReliefCoreApi.csproj

COPY src/ComicReliefCoreApi/ src/ComicReliefCoreApi/
COPY src/ComicReliefCoreApi.Api/ src/ComicReliefCoreApi.Api/
COPY src/ComicReliefCoreApi.App/ src/ComicReliefCoreApi.App/
RUN dotnet publish src/ComicReliefCoreApi/ComicReliefCoreApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ComicReliefCoreApi.dll"]
