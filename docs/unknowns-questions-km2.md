# Unknowns & questions (KM2, 19.11.2026)

## Czego jeszcze nie wiemy

- Z którą grupą integrujemy nasze API i kiedy będzie dokumentacja API prowadzącego (prowadzący poda ją później).
- Czy tenant PW pozwala studentom rejestrować aplikacje w Entra. Od tego zależy, czy logujemy kontami PW, czy własnym tenantem albo Google.
- Czy licencja ADO pozwala założyć Test Plan bez triala (test suite dla katalogu aut i ofert).
- Jaki próg pokrycia testami przyjmujemy. Arkusz mówi 40%, slajd 10-20%. U nas w YAML jest 40%.
- Jak liczyć cenę końcową przy wcześniejszym zwrocie i jak organizować odbiór auta.
- Jakie minimalne obciążenie mamy pokazać (opis mówi np. 50 użytkowników naraz).

## Pytania do prowadzącego

Pytamy na zajęciach w czwartek. Odpowiedzi zapisujemy na tablicy i w ADO.

1. Z którą grupą mamy się zintegrować i kiedy będzie dokumentacja Pana API? Nasze API jest pod /api/v1, dostęp przez nagłówek x-api-key, drugiej grupie damy osobny klucz.
2. Czy repo na GitHubie (reguły gałęzi w GitHubie, Boards i Pipelines w ADO) jest OK zamiast Azure Repos? Czy .NET 10 jest OK, skoro w opisie jest .NET 8 lub 9? Projekt da się przełączyć na .NET 8 jednym parametrem.
3. Odbiór auta, cena końcowa i wcześniejszy zwrot: proponujemy cenę za faktyczne dni wynajmu razy (cena dzienna + ubezpieczenie), minimum 1 dzień. Czy tak może być?

Zapas, jak będzie czas: czy Google albo własny tenant Entra jest OK, jeśli tenant PW blokuje rejestrację aplikacji; co Pan rozumie przez "Connect with Azure (external) services"; jaki próg pokrycia przyjąć.
