# Test case'y do ADO Test Plans: Feature "katalog aut i oferty" (#117, #118, #119)

Plan: Test Plans, New Test Plan, nazwa "KM2 Car Rental API", Iteration KM2 (albo KM3, jeśli Test Plans wymaga triala).
Suite: New Suite, Static suite "Feature: katalog aut i oferty". W nim New Suite, Requirement based suite, zapytanie o User Story #117, #118, #119, Create suites. Test case'y dodajemy do suite danej story, wtedy dostają link Tests do story.
Każdy test case: Title jak niżej, Steps (Action i Expected Result), Assigned To, State Ready. W opisie nazwa testu automatycznego, żeby było widać, że ten sam przypadek jest w pipeline.
Na koniec: Execute, zaznaczyć wszystkie, Run for web application, przejść kroki i oznaczyć Pass albo Fail. Wynik pokazujemy w Runs i w Progress report.

Warunki wspólne (wkleić do opisu każdego test case'u albo jako Shared steps):
Uruchomić API: docker compose up --build z katalogu głównego repo. Swagger: http://localhost:5080/swagger. W Authorize wpisać klucz dev-our-broker-key-change-me. Można też użyć kolekcji Postman z docs/postman.
Na Azure to samo pod adresem https://app-carrentals-api.azurewebsites.net (Swagger po Swagger__Enabled=true) z kluczem z App Settings.
D oznacza dzisiejszą datę, D+1 jutro.

## TC1 Lista aut z poprawnym kluczem (#117)
1. Akcja: GET /api/v1/cars z nagłówkiem x-api-key: dev-our-broker-key-change-me. Oczekiwane: 200, lista 12 aut.
2. Akcja: sprawdzić pierwsze auto na liście. Oczekiwane: są pola id, make, model, year, location, available.
Automat: CarsEndpointTests.GetCars_WithValidKey_ReturnsWholeFleet

## TC2 Lista aut bez klucza i ze złym kluczem (#117)
1. Akcja: GET /api/v1/cars bez nagłówka x-api-key. Oczekiwane: 401.
2. Akcja: GET /api/v1/cars z x-api-key: zly-klucz-123456789. Oczekiwane: 401.
Automat: CarsEndpointTests.GetCars_WithoutApiKey_Returns401, GetCars_WithWrongApiKey_Returns401

## TC3 Klucz drugiej grupy (#117)
1. Akcja: GET /api/v1/cars z kluczem dev-other-group-key-change-me. Oczekiwane: 200, lista 12 aut.
Automat: CarsEndpointTests.GetCars_OtherGroupKey_AlsoWorks

## TC4 Szczegóły auta (#117)
1. Akcja: GET /api/v1/cars/1. Oczekiwane: 200, Toyota Corolla, 2022, Warszawa.
2. Akcja: GET /api/v1/cars/999. Oczekiwane: 404 z opisem "Nie znaleziono samochodu.".
Automat: CarsEndpointTests.GetCar_Existing_ReturnsCar, GetCar_Unknown_Returns404

## TC5 Filtr po marce (#118)
1. Akcja: GET /api/v1/cars?make=Toyota. Oczekiwane: 3 auta: Corolla, Yaris, RAV4.
2. Akcja: GET /api/v1/cars?make=toyota. Oczekiwane: te same 3 auta (wielkość liter bez znaczenia).

## TC6 Filtr po marce, modelu i roku (#118)
1. Akcja: GET /api/v1/cars?make=Toyota&year=2022. Oczekiwane: tylko auto id 1 (Corolla).
2. Akcja: GET /api/v1/cars?model=Golf. Oczekiwane: tylko auto id 4 (Volkswagen Golf, Kraków).
3. Akcja: GET /api/v1/cars?year=1990. Oczekiwane: 200 i pusta lista.
Automat: CarsEndpointTests.GetCars_FilterByYearAndMake

## TC7 Oferta dla doświadczonego kierowcy (#119)
1. Akcja: POST /api/v1/offers z body carId 1, age 30, licenseYears 10, location Warszawa, startDate D+1, endDate D+4. Oczekiwane: 201.
2. Akcja: sprawdzić odpowiedź. Oczekiwane: dailyPrice 150.00, dailyInsurancePrice 30.00, days 3, totalPrice 540.00, currency PLN, jest offerId.
3. Akcja: porównać expiresAt z aktualnym czasem UTC. Oczekiwane: expiresAt to około teraz plus 10 minut.
Automat: OffersEndpointTests.CreateOffer_Valid_Returns201WithPriceAndExpiry

## TC8 Oferta dla młodego kierowcy z krótkim stażem (#119)
1. Akcja: POST /api/v1/offers z body carId 1, age 22, licenseYears 2, location Warszawa, startDate D+1, endDate D+4. Oczekiwane: 201.
2. Akcja: sprawdzić ceny. Oczekiwane: dailyPrice 225.00, dailyInsurancePrice 45.00, totalPrice 810.00 (mnożnik 1,25 razy 1,2).
Automat: OffersEndpointTests.CreateOffer_YoungNewDriver_PaysMore

## TC9 Walidacja danych oferty (#119)
1. Akcja: POST /api/v1/offers z pustym body {}. Oczekiwane: 400 z błędami dla brakujących pól.
2. Akcja: startDate D-1, endDate D+2 (reszta jak w TC7). Oczekiwane: 400, błąd pola startDate.
3. Akcja: startDate D+4, endDate D+1. Oczekiwane: 400, błąd pola endDate.
4. Akcja: startDate D+1, endDate D+32. Oczekiwane: 400, wynajem może trwać najwyżej 30 dni.
5. Akcja: age 20, licenseYears 5. Oczekiwane: 400, błąd pola licenseYears.
Automat: OffersEndpointTests.CreateOffer_MissingFields_Returns400, CreateOffer_StartInPast_Returns400, CreateOffer_EndBeforeStart_Returns400, CreateOffer_TooLong_Returns400, CreateOffer_InvalidDriver_Returns400

## TC10 Lokalizacja odbioru (#119)
1. Akcja: carId 1, location Kraków (reszta jak w TC7). Oczekiwane: 400 z komunikatem, że auto można odebrać tylko w lokalizacji Warszawa.
2. Akcja: carId 1, location warszawa. Oczekiwane: 201.
Automat: OffersEndpointTests.CreateOffer_WrongLocation_Returns400, CreateOffer_LocationIsCaseInsensitive

## TC11 Oferta dla nieznanego auta i bez klucza (#119)
1. Akcja: carId 999 (reszta jak w TC7). Oczekiwane: 404.
2. Akcja: poprawne body bez nagłówka x-api-key. Oczekiwane: 401.
Automat: OffersEndpointTests.CreateOffer_UnknownCar_Returns404, CreateOffer_WithoutApiKey_Returns401

## TC12 Auto zajęte w terminie (#119)
1. Akcja: POST /api/v1/offers carId 2, age 30, licenseYears 10, location Warszawa, startDate D, endDate D+3. Oczekiwane: 201, zapisać offerId.
2. Akcja: POST /api/v1/rentals z tym offerId, firstName Jan, lastName Kowalski, email jan@example.com. Oczekiwane: 202, status Pending.
3. Akcja: GET /api/v1/cars/2. Oczekiwane: available false.
4. Akcja: POST /api/v1/offers carId 2 z terminem D+1 do D+2. Oczekiwane: 409, auto zajęte w wybranym terminie.
5. Akcja: POST /api/v1/offers carId 2 z terminem D+3 do D+5. Oczekiwane: 201, bo dzień oddania auta jest wolny.
Automat: OffersEndpointTests.CreateOffer_CarAlreadyRentedInThatTerm_Returns409, CreateOffer_TermStartingOnReturnDay_IsAllowed, CarsEndpointTests.GetCar_RentedToday_IsNotAvailable

Uwaga do TC12: po kroku 2 auto 2 jest zajęte od D do D+3 w tej bazie. Przy ponownym przebiegu użyj innego auta z Warszawy (np. 3 albo 9) albo postaw bazę od nowa (docker compose down -v).

Suite jest pełny, gdy każda story feature'a ma co najmniej jeden test case i wszystkie test case'y są wykonane w jednym Run z wynikiem.

## Dodatkowo: wynajem przez Brokera w UI (osobny suite "Wynajem przez Brokera")

Warunki: docker compose up --build, aplikacja http://localhost:5100, logowanie dev (Klient albo Pracownik w menu). Pracownika najlepiej w oknie incognito.

## TC13 Wynajem z oferty i potwierdzenie
1. Akcja: zalogować się jako Klient, uzupełnić profil, wejść w auto Skoda Octavia (Warszawa), Sprawdź ceny. Oczekiwane: oferta z ceną i przyciskiem Wynajmij.
2. Akcja: Wynajmij. Oczekiwane: formularz z imieniem, nazwiskiem i emailem z profilu.
3. Akcja: Potwierdź wynajem. Oczekiwane: ekran statusu "Czeka na potwierdzenie", po około 10 s sam zmienia się na "Potwierdzone".
4. Akcja: Moje wypożyczenia. Oczekiwane: wynajem na liście ze statusem Potwierdzone i przyciskiem Zgłoś zwrot.
Automat: RentalEndpointTests.PostRentals_OwnQuote_Returns202PendingAndSavesRental, GetRentals_PendingRental_RefreshesStatusFromCarRentalApi, CarRentalIntegrationTests.PostRentals_ThroughBroker_CreatesPendingRentalVisibleForEmployee

## TC14 Zwrot zgłoszony przez klienta i potwierdzony przez pracownika
1. Akcja: w Moich wypożyczeniach Zgłoś zwrot przy wynajmie z TC13. Oczekiwane: status "Zwrot zgłoszony", przycisk znika.
2. Akcja: jako Pracownik Panel pracownika, filtr Do odbioru. Oczekiwane: ten wynajem z danymi klienta.
3. Akcja: Szczegóły, notatka i plik jpg, potwierdzenie zwrotu. Oczekiwane: status Zakończone.
4. Akcja: jako Klient odświeżyć Moje wypożyczenia. Oczekiwane: status "Zakończone".
Automat: RentalEndpointTests.PostReturn_ConfirmedRental_SendsCustomerEmailAndReturnsReturnRequested, PostReturn_RentalStillPending_Returns409

## TC15 Wynajem bez logowania, cudza i wygasła oferta
1. Akcja: bez logowania Sprawdź ceny i Wynajmij. Oczekiwane: przejście do logowania, po zalogowaniu powrót na stronę auta.
2. Akcja: POST /api/v1/rentals z quoteId oferty innego użytkownika. Oczekiwane: 404.
3. Akcja: po 10 minutach od wyceny Wynajmij i Potwierdź wynajem. Oczekiwane: komunikat "Oferta wygasła. Sprawdź cenę jeszcze raz." (410).
Automat: RentalEndpointTests.PostRentals_QuoteOfAnotherUser_Returns404, PostRentals_AnonymousQuote_Returns404, PostRentals_ExpiredQuote_Returns410, PostRentals_WithoutCsrfHeader_Returns400AndDoesNotCallCarRentalApi
