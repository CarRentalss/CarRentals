# Budowanie: docker build -f backend/Broker.Api/Dockerfile .   (z katalogu głównego repo)
# Etap 1: build Reacta (frontend/ do dist). Jak nie ma frontend/package.json, etap nic nie robi.
FROM node:22-alpine AS frontend
WORKDIR /frontend
COPY frontend/ ./
RUN if [ -f package.json ]; then npm ci && npm run build; else mkdir -p dist; fi

# Etap 2: build Brokera
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY backend/Broker.Api/Broker.Api.csproj backend/Broker.Api/
RUN dotnet restore backend/Broker.Api/Broker.Api.csproj

COPY backend/Broker.Api/ backend/Broker.Api/
# Zbudowany React trafia do wwwroot, Broker serwuje go razem z /api (jedna domena, ciasteczko first-party)
COPY --from=frontend /frontend/dist/ backend/Broker.Api/wwwroot/
RUN dotnet publish backend/Broker.Api/Broker.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Broker.Api.dll"]
