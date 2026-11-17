# Budowanie: docker build -f backend/CarRental.Worker/Dockerfile .   (z katalogu głównego repo)
# Worker linkuje pliki z backend/CarRental.Api (encje, DbContext, potwierdzanie, maile), więc kopiujemy też ten katalog.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY backend/CarRental.Worker/CarRental.Worker.csproj backend/CarRental.Worker/
RUN dotnet restore backend/CarRental.Worker/CarRental.Worker.csproj

COPY backend/CarRental.Api/ backend/CarRental.Api/
COPY backend/CarRental.Worker/ backend/CarRental.Worker/
RUN dotnet publish backend/CarRental.Worker/CarRental.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CarRental.Worker.dll"]
