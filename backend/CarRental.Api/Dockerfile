# Budowanie: docker build -f backend/CarRental.Api/Dockerfile .   (z katalogu głównego repo)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY backend/CarRental.Api/CarRental.Api.csproj backend/CarRental.Api/
RUN dotnet restore backend/CarRental.Api/CarRental.Api.csproj

COPY backend/CarRental.Api/ backend/CarRental.Api/
RUN dotnet publish backend/CarRental.Api/CarRental.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Katalog na załączniki musi być zapisywalny dla użytkownika bez uprawnień roota
RUN mkdir -p /app/App_Data/attachments && chown -R $APP_UID /app/App_Data

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CarRental.Api.dll"]
