<#
.SYNOPSIS
    Generuje migracje EF Core (domyślnie InitialCreate) dla Car Rental API i Broker API.

.DESCRIPTION
    Uruchamiać z katalogu głównego repo (tam, gdzie jest CarRentals.sln).
    Domyślnie wszystko dzieje się w kontenerze mcr.microsoft.com/dotnet/sdk:10.0, więc lokalnie
    wystarczy Docker Desktop (nie trzeba .NET 10 SDK). Z przełącznikiem -Local używa lokalnego .NET 10 SDK.

    Migracje trafiają do domyślnych folderów Migrations (tego szuka pipeline):
      backend/CarRental.Api/Migrations  (CarRentalDbContext)
      backend/Broker.Api/Migrations     (BrokerDbContext, pomijane, jeśli projektu jeszcze nie ma)

    Baza nie jest potrzebna: dotnet ef korzysta z fabryk design-time (CarRentalDbContextFactory,
    BrokerDbContextFactory), a nie z Program.cs.

.EXAMPLE
    .\scripts\add-migrations.ps1
.EXAMPLE
    .\scripts\add-migrations.ps1 -Local
.EXAMPLE
    .\scripts\add-migrations.ps1 -Name AddRentalDocuments
#>
[CmdletBinding()]
param(
    # Lokalny .NET 10 SDK zamiast Dockera
    [switch]$Local,
    # Nazwa migracji
    [string]$Name = "InitialCreate",
    # Obraz SDK dla trybu Docker
    [string]$Image = "mcr.microsoft.com/dotnet/sdk:10.0"
)

$ErrorActionPreference = "Stop"

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not (Test-Path (Join-Path $repo "CarRentals.sln"))) {
    throw "Nie znaleziono CarRentals.sln w $repo. Uruchom skrypt z repo CarRentals."
}

$targets = @(
    @{ Project = "backend/CarRental.Api/CarRental.Api.csproj"; Context = "CarRentalDbContext" },
    @{ Project = "backend/Broker.Api/Broker.Api.csproj"; Context = "BrokerDbContext" }
)

# Wybieramy projekty, które istnieją i nie mają jeszcze migracji o tej nazwie
$todo = @()
foreach ($t in $targets) {
    $projectPath = Join-Path $repo $t.Project
    if (-not (Test-Path $projectPath)) {
        Write-Host "Pomijam $($t.Project): projektu jeszcze nie ma." -ForegroundColor Yellow
        continue
    }

    $migrationsDir = Join-Path (Split-Path $projectPath -Parent) "Migrations"
    if (Test-Path (Join-Path $migrationsDir "*_$Name.cs")) {
        Write-Host "Pomijam $($t.Project): migracja $Name już jest w $migrationsDir." -ForegroundColor Yellow
        continue
    }

    $todo += $t
}

if ($todo.Count -eq 0) {
    Write-Host "Nic do zrobienia."
    return
}

$commands = @("dotnet tool restore")
foreach ($t in $todo) {
    $commands += "dotnet ef migrations add $Name --project $($t.Project) --startup-project $($t.Project) --context $($t.Context)"
}

Push-Location $repo
try {
    if ($Local) {
        $sdks = (& dotnet --list-sdks) -join "`n"
        if ($sdks -notmatch "(?m)^10\.") {
            throw "Brak .NET 10 SDK (dotnet --list-sdks). Zainstaluj .NET 10 SDK albo uruchom skrypt bez -Local (przez Dockera)."
        }

        foreach ($command in $commands) {
            Write-Host "> $command" -ForegroundColor Cyan
            $parts = $command.Split(" ")
            & $parts[0] $parts[1..($parts.Length - 1)]
            if ($LASTEXITCODE -ne 0) { throw "Polecenie zakończyło się błędem: $command" }
        }
    }
    else {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw "Nie znaleziono dockera. Uruchom Docker Desktop albo użyj -Local (wymaga .NET 10 SDK)."
        }

        # bin/obj z kontenera mają ścieżki /src/..., więc po wszystkim je usuwamy (lokalny build zrobi je od nowa)
        $cleanup = ($todo | ForEach-Object {
            $dir = Split-Path $_.Project -Parent
            "rm -rf $dir/bin $dir/obj"
        }) -join " ; "
        $script = ($commands -join " && ") + " ; status=`$? ; $cleanup ; exit `$status"

        Write-Host "> docker run $Image" -ForegroundColor Cyan
        Write-Host "  $($commands -join "`n  ")"
        & docker run --rm `
            -v "${repo}:/src" `
            -v "carrentals-nuget:/root/.nuget/packages" `
            -w /src `
            -e DOTNET_CLI_TELEMETRY_OPTOUT=1 `
            -e DOTNET_NOLOGO=1 `
            $Image sh -c $script
        if ($LASTEXITCODE -ne 0) { throw "Generowanie migracji w kontenerze nie powiodło się (kod $LASTEXITCODE)." }
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Gotowe. Sprawdź nowe pliki w folderach Migrations (git status) i zrób commit." -ForegroundColor Green
Write-Host "Jeśli lokalna baza powstała wcześniej przez EnsureCreated, usuń ją przed pierwszym uruchomieniem z migracjami."
