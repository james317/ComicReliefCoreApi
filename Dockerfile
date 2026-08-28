FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/ComicReliefCoreApi/ComicReliefCoreApi.csproj src/ComicReliefCoreApi/
RUN dotnet restore src/ComicReliefCoreApi/ComicReliefCoreApi.csproj

COPY src/ComicReliefCoreApi/ src/ComicReliefCoreApi/
RUN dotnet publish src/ComicReliefCoreApi/ComicReliefCoreApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ComicReliefCoreApi.dll"]
