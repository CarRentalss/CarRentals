# Diagram ER (KM2)

Dwie osobne bazy: CarRental DB (nasza wypożyczalnia, Car Rental API) i Broker DB (aplikacja pośrednicząca). Schemat ma powstawać z migracji EF (`InitialCreate`, generuje ją Maks skryptem `scripts/add-migrations.ps1`), a diagram zrobiłem z konfiguracji EF w kodzie. Konfiguracja jest tylko fluent w `Data/Configurations`. Oznaczenia: PK klucz główny, FK klucz obcy, IX indeks, UX indeks unikalny, null kolumna opcjonalna. Źródła: `docs/diagrams/er-*.mmd`.

## CarRental DB (sqldb-carrental)

![CarRental DB](diagrams/er-carrental.png)

```mermaid
erDiagram
  Cars ||--o{ Offers : "CarId, Restrict"
  Cars ||--o{ Rentals : "CarId, Restrict"
  Offers ||--o| Rentals : "OfferId unikalny, Restrict"

  Cars {
    int Id PK
    nvarchar_50 Make "IX (Make, Model)"
    nvarchar_50 Model
    int Year
    nvarchar_100 Location
    decimal_10_2 BasePricePerDay
    decimal_10_2 InsurancePricePerDay
    bit IsActive
  }
  Offers {
    uniqueidentifier Id PK
    int CarId FK "IX"
    int DriverAge
    int LicenseYears
    nvarchar_100 Location
    date StartDate
    date EndDate
    decimal_10_2 DailyPrice
    decimal_10_2 DailyInsurancePrice
    decimal_12_2 TotalPrice
    nvarchar_3 Currency
    datetime2 CreatedAtUtc
    datetime2 ExpiresAtUtc "IX"
    nvarchar_100 BrokerName
  }
  Rentals {
    uniqueidentifier Id PK
    uniqueidentifier OfferId FK "UX"
    int CarId FK "IX (CarId, Status)"
    date StartDate
    date EndDate
    decimal_12_2 TotalPrice
    nvarchar_100 FirstName
    nvarchar_100 LastName
    nvarchar_254 Email
    nvarchar_20 Status "tekst, IX (Status, CreatedAtUtc)"
    nvarchar_500 Reason "null"
    nvarchar_100 BrokerName "IX"
    datetime2 CreatedAtUtc
    datetime2 UpdatedAtUtc
    datetime2 ReturnRequestedAtUtc "null"
    datetime2 CompletedAtUtc "null"
    nvarchar_200 CompletedBy "null"
    nvarchar_1000 ReturnNote "null"
    nvarchar_300 AttachmentKey "null"
    nvarchar_100 AttachmentContentType "null"
    nvarchar_255 AttachmentFileName "null"
  }
```

- Seed: 12 aut (`HasData`, jest w migracji).
- Oferta należy do jednego auta, wynajem powstaje z jednej oferty (unikalny OfferId), więc jednej oferty nie da się wynająć dwa razy.
- Rental kopiuje CarId, daty i cenę z oferty. To celowy zapis warunków z chwili wynajmu.
- Klucze obce mają Restrict: auta z ofertami nie usuwamy, wycofujemy je flagą IsActive.
- Status wynajmu jest zapisany jako tekst (Pending, Confirmed, Rejected, ReturnRequested, Completed).
- Indeksy pod zapytania: (Make, Model) katalog, (CarId, Status) dostępność auta, (Status, CreatedAtUtc) proces w tle i panel pracownika, BrokerName izolacja brokerów, ExpiresAtUtc sprzątanie ofert.

## Broker DB (sqldb-broker)

![Broker DB](diagrams/er-broker.png)

```mermaid
erDiagram
  Providers ||--o{ Quotes : "ProviderId, Restrict"
  UserProfiles |o--o{ Quotes : "UserProfileId null, SetNull"

  Providers {
    int Id PK
    nvarchar_50 Code "UX: our, lecturer, other-group"
    nvarchar_100 Name
    bit IsEnabled
  }
  UserProfiles {
    uniqueidentifier Id PK
    nvarchar_100 EntraObjectId "UX"
    nvarchar_254 Email
    nvarchar_100 FirstName
    nvarchar_100 LastName
    date DateOfBirth "null"
    date LicenseObtainedOn "null"
    nvarchar_100 Location "null"
    datetime2 CreatedAtUtc
    datetime2 UpdatedAtUtc
  }
  Quotes {
    uniqueidentifier Id PK
    int ProviderId FK "IX"
    uniqueidentifier UserProfileId FK "IX, null"
    nvarchar_100 ProviderOfferId
    nvarchar_100 ProviderCarId
    nvarchar_200 CarLabel
    decimal_10_2 DailyPrice
    decimal_10_2 DailyInsurancePrice
    decimal_10_2 TotalPrice
    nchar_3 Currency
    date StartDate
    date EndDate
    datetime2 ExpiresAtUtc "IX"
    datetime2 CreatedAtUtc
  }
```

- Seed: 3 wypożyczalnie (our włączona, lecturer i other-group wyłączone do KM3). Adresy i klucze API nie są w bazie, tylko w konfiguracji.
- Profil trzyma daty (urodzenia, prawa jazdy), a nie wiek i staż. Wiek i staż liczymy na dzień wyceny.
- Quotes to oferty pokazane użytkownikowi. Usunięcie profilu nie usuwa ofert (SetNull), wypożyczalni z ofertami nie da się usunąć (Restrict).
- Indeksy: unikalny EntraObjectId (profil szukany po oid z tokena), unikalny Code, Quotes(UserProfileId) i Quotes(ExpiresAtUtc) do sprzątania w tle.
