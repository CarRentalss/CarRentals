# Szybki test działającej aplikacji po docker compose up (Car Rental API na 5080, Broker na 5100).
# Sprawdza cały przepływ KM2: katalog, oferta, wynajem z potwierdzeniem w tle, Broker, logowanie dev,
# wynajem przez Brokera jako zwykły użytkownik i panel pracownika.
# Uruchom z katalogu repo:  powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1

param(
    [string]$CarRentalUrl = "http://localhost:5080",
    [string]$BrokerUrl = "http://localhost:5100",
    [string]$ApiKey = "dev-our-broker-key-change-me"
)

$ErrorActionPreference = "Continue"
$failed = 0
$passed = 0

function Check([string]$name, [bool]$ok, [string]$details = "") {
    if ($ok) { $script:passed++; Write-Host "OK    $name" }
    else { $script:failed++; Write-Host "BLAD  $name $details" }
}

# curl.exe jest w Windows 10+, PowerShell 5.1 nie ma -SkipHttpErrorCheck, więc kod HTTP bierzemy z curl
function Call([string]$method, [string]$url, [string[]]$headers = @(), [string]$body = $null, [string]$cookieJar = $null) {
    $tmp = [System.IO.Path]::GetTempFileName()
    $curlArgs = @("-s", "-o", $tmp, "-w", "%{http_code}", "-X", $method, $url)
    foreach ($h in $headers) { $curlArgs += @("-H", $h) }
    if ($body) {
        $bodyFile = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($bodyFile, $body)
        $curlArgs += @("-H", "Content-Type: application/json", "--data-binary", "@$bodyFile")
    }
    if ($cookieJar) { $curlArgs += @("-b", $cookieJar, "-c", $cookieJar) }
    $code = & curl.exe @curlArgs
    $text = Get-Content $tmp -Raw -Encoding UTF8
    Remove-Item $tmp -ErrorAction SilentlyContinue
    return @{ Code = [int]$code; Body = $text }
}

# Po docker compose up API potrzebują chwili (tworzenie baz), więc czekamy na /health do 90 s
function WaitForHealth([string]$url) {
    for ($i = 1; $i -le 18; $i++) {
        $r = Call GET "$url/health"
        if ($r.Code -eq 200) { return $r }
        Write-Host "czekam na $url/health (próba $i, kod $($r.Code))..."
        Start-Sleep -Seconds 5
    }
    return $r
}

function Json([string]$text) {
    try { return $text | ConvertFrom-Json } catch { return $null }
}

$key = "x-api-key: $ApiKey"
# Losowy termin, żeby drugie uruchomienie nie trafiło na auto zajęte przez pierwsze (409)
$offset = Get-Random -Minimum 20 -Maximum 330
$start = (Get-Date).AddDays($offset).ToString("yyyy-MM-dd")
$end = (Get-Date).AddDays($offset + 3).ToString("yyyy-MM-dd")

Write-Host "=== Car Rental API ($CarRentalUrl) ==="
$r = WaitForHealth $CarRentalUrl
Check "health 200" ($r.Code -eq 200) "kod $($r.Code) $($r.Body)"

$r = Call GET "$CarRentalUrl/api/v1/cars"
Check "cars bez klucza 401" ($r.Code -eq 401) "kod $($r.Code)"

$r = Call GET "$CarRentalUrl/api/v1/cars" @($key)
$cars = Json $r.Body
Check "cars z kluczem 200 i 12 aut" ($r.Code -eq 200 -and $cars.Count -eq 12) "kod $($r.Code), aut: $($cars.Count)"

$offerBody = "{""carId"":2,""age"":22,""licenseYears"":2,""location"":""Warszawa"",""startDate"":""$start"",""endDate"":""$end""}"
$r = Call POST "$CarRentalUrl/api/v1/offers" @($key) $offerBody
$offer = Json $r.Body
Check "oferta 201" ($r.Code -eq 201 -and $offer.offerId) "kod $($r.Code) $($r.Body)"

if ($offer.offerId) {
    $rentalBody = "{""offerId"":""$($offer.offerId)"",""firstName"":""Jan"",""lastName"":""Test"",""email"":""jan.test@example.com""}"
    $r = Call POST "$CarRentalUrl/api/v1/rentals" @($key) $rentalBody
    $rental = Json $r.Body
    Check "wynajem 202 Pending" ($r.Code -eq 202 -and $rental.status -eq "Pending") "kod $($r.Code) $($r.Body)"

    if ($rental.rentalId) {
        # Potwierdzenie w tle: około 10 s opóźnienia plus do 5 s na kolejny obieg, więc pytamy co 3 s do 30 s
        Write-Host "czekam na potwierdzenie w tle (do 30 s)..."
        for ($i = 0; $i -lt 10; $i++) {
            Start-Sleep -Seconds 3
            $r = Call GET "$CarRentalUrl/api/v1/rentals/$($rental.rentalId)" @($key)
            $status = Json $r.Body
            if ($status.status -ne "Pending") { break }
        }
        Check "status po potwierdzeniu w tle Confirmed" ($r.Code -eq 200 -and $status.status -eq "Confirmed") "kod $($r.Code) $($r.Body)"
    }
}

Write-Host "=== Broker ($BrokerUrl) ==="
$r = WaitForHealth $BrokerUrl
Check "broker health 200" ($r.Code -eq 200) "kod $($r.Code) $($r.Body)"

$r = Call GET "$BrokerUrl/"
Check "React z wwwroot (index.html)" ($r.Code -eq 200 -and $r.Body -match "<div id=""root"">") "kod $($r.Code)"

$r = Call GET "$BrokerUrl/api/v1/cars?page=1&pageSize=5"
$page = Json $r.Body
Check "broker cars 200 i stronicowanie po 5" ($r.Code -eq 200 -and $page.items.Count -le 5 -and $page.totalCount -ge 12) "kod $($r.Code) $($r.Body)"

$r = Call GET "$BrokerUrl/api/v1/cars/makes"
Check "broker marki 200" ($r.Code -eq 200 -and $r.Body -match "Toyota") "kod $($r.Code)"

$quoteBody = "{""provider"":""our"",""carId"":""1"",""startDate"":""$start"",""endDate"":""$end"",""age"":30,""licenseYears"":10,""location"":""Warszawa""}"
$r = Call POST "$BrokerUrl/api/v1/offers" @() $quoteBody
Check "broker oferta bez X-CSRF 400" ($r.Code -eq 400) "kod $($r.Code)"

$r = Call POST "$BrokerUrl/api/v1/offers" @("X-CSRF: 1") $quoteBody
$quote = Json $r.Body
Check "broker oferta z naszego API" ($r.Code -eq 200 -and $quote.offers.Count -ge 1) "kod $($r.Code) $($r.Body)"

# Wynajem przez Brokera jako zwykły użytkownik (dev-login bez roli, tak jak przycisk "Klient" w menu).
# Wycena anonimowa nie ma właściciela, więc zalogowany wycenia jeszcze raz i z tej oferty składa wniosek.
$userJar = [System.IO.Path]::GetTempFileName()
$providerRentalId = $null
$r = Call GET "$BrokerUrl/api/v1/auth/dev-login?name=jan.kowalski" @() $null $userJar
$me = Json $r.Body
Check "dev-login zwykłego użytkownika (bez roli)" ($r.Code -eq 200 -and $me.isAuthenticated -and -not $me.isEmployee) "kod $($r.Code) $($r.Body)"

$userQuoteBody = "{""provider"":""our"",""carId"":""3"",""startDate"":""$start"",""endDate"":""$end"",""age"":30,""licenseYears"":10,""location"":""Warszawa""}"
$r = Call POST "$BrokerUrl/api/v1/offers" @("X-CSRF: 1") $userQuoteBody $userJar
$userQuote = Json $r.Body
Check "broker oferta zalogowanego" ($r.Code -eq 200 -and $userQuote.offers.Count -ge 1) "kod $($r.Code) $($r.Body)"

if ($userQuote.offers.Count -ge 1) {
    $brokerRentalBody = "{""quoteId"":""$($userQuote.offers[0].quoteId)"",""firstName"":""Jan"",""lastName"":""Kowalski"",""email"":""jan.kowalski@example.com""}"
    $r = Call POST "$BrokerUrl/api/v1/rentals" @() $brokerRentalBody $userJar
    Check "broker wynajem bez X-CSRF 400" ($r.Code -eq 400) "kod $($r.Code)"

    $r = Call POST "$BrokerUrl/api/v1/rentals" @("X-CSRF: 1") $brokerRentalBody $userJar
    $brokerRental = Json $r.Body
    Check "broker wynajem 202 Pending" ($r.Code -eq 202 -and $brokerRental.status -eq "Pending") "kod $($r.Code) $($r.Body)"

    if ($brokerRental.rentalId) {
        $providerRentalId = $brokerRental.providerRentalId
        Write-Host "czekam na potwierdzenie wynajmu przez Brokera (do 30 s)..."
        for ($i = 0; $i -lt 10; $i++) {
            Start-Sleep -Seconds 3
            $r = Call GET "$BrokerUrl/api/v1/rentals/$($brokerRental.rentalId)" @() $null $userJar
            $status = Json $r.Body
            if ($status.status -ne "Pending") { break }
        }
        Check "broker status wynajmu Confirmed" ($r.Code -eq 200 -and $status.status -eq "Confirmed") "kod $($r.Code) $($r.Body)"

        $r = Call GET "$BrokerUrl/api/v1/rentals" @() $null $userJar
        $mine = @(Json $r.Body)
        $found = @($mine | Where-Object { $_.rentalId -eq $brokerRental.rentalId })
        Check "moje wypożyczenia zawierają nowy wynajem" ($r.Code -eq 200 -and $found.Count -eq 1) "kod $($r.Code) $($r.Body)"
    }
}

$r = Call GET "$BrokerUrl/api/v1/rentals"
Check "moje wypożyczenia bez logowania 401" ($r.Code -eq 401) "kod $($r.Code)"

$jar = [System.IO.Path]::GetTempFileName()
$r = Call GET "$BrokerUrl/api/v1/auth/dev-login?name=anna&role=Employee&returnUrl=/" @() $null $jar
Check "dev-login pracownika" ($r.Code -ge 200 -and $r.Code -lt 400) "kod $($r.Code)"

$r = Call GET "$BrokerUrl/api/v1/auth/me" @() $null $jar
$me = Json $r.Body
Check "auth/me zalogowany z rolą Employee" ($r.Code -eq 200 -and $me.isAuthenticated -and ($me.roles -contains "Employee")) "kod $($r.Code) $($r.Body)"

$r = Call GET "$BrokerUrl/api/v1/employee/rentals" @() $null $jar
Check "panel pracownika przez Brokera 200" ($r.Code -eq 200) "kod $($r.Code) $($r.Body)"

if ($providerRentalId) {
    $employeeRentals = @(Json $r.Body)
    $seen = @($employeeRentals | Where-Object { $_.rentalId -eq $providerRentalId })
    Check "pracownik widzi wynajem złożony przez Brokera" ($seen.Count -eq 1) "rentalId $providerRentalId"
}

$r = Call GET "$BrokerUrl/api/v1/profile" @() $null $jar
Check "profil zalogowanego 200" ($r.Code -eq 200) "kod $($r.Code) $($r.Body)"

$r = Call GET "$BrokerUrl/api/v1/employee/rentals"
Check "panel bez logowania 401" ($r.Code -eq 401) "kod $($r.Code)"

Remove-Item $jar, $userJar -ErrorAction SilentlyContinue
Write-Host "=== Wynik: OK $passed, BLAD $failed ==="
if ($failed -gt 0) { exit 1 }
