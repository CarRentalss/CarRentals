## Co zmienia ten PR

<!-- 1-3 zdania: co i dlaczego. -->

## Zadania w ADO

AB#

<!-- Każde zadanie osobno, np. AB#84 AB#85. Dzięki temu PR podpina się pod work item w ADO. -->

## Jak sprawdzić

<!-- Co uruchomić i co powinno wyjść, np. "docker compose up, GET /api/v1/cars zwraca 12 aut". -->

## Checklista

- [ ] PR idzie do develop (do main tylko z develop albo z hotfix/*)
- [ ] pipeline Jagna676.CarRentals jest zielony
- [ ] testy przechodzą lokalnie (dotnet test, w frontend/ npm test -- --run)
- [ ] nowe endpointy są pod /api/v1 i mają opis w Swaggerze
- [ ] na encjach nie ma atrybutów EF, konfiguracja jest w Data/Configurations
- [ ] nie ma sekretów w kodzie ani w appsettings (klucze tylko "change-me" dla Development)
- [ ] zmiana modelu bazy ma migrację (scripts/add-migrations.ps1)
- [ ] README albo docs zaktualizowane, jeśli coś się zmieniło w uruchamianiu
