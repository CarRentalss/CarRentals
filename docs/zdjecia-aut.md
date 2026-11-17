# Zdjęcia aut

Zdjęcia 12 aut z seeda są w kontenerze `cars` konta `stcarrentalsjkm` (publiczny odczyt blobów, bez listowania):
https://stcarrentalsjkm.blob.core.windows.net/cars/

W bazie (`Cars.ImageUrl`) trzymamy tylko nazwę pliku. Pełny adres składa Car Rental API z App Setting `Images__BaseUrl` (`https://stcarrentalsjkm.blob.core.windows.net/cars/`), więc seed nie ma wpisanej domeny. Bez `Images__BaseUrl` API zwraca `imageUrl: null`, a front pokazuje zaślepkę "Brak zdjęcia". Broker przepuszcza tylko adresy https, a jego CSP pozwala na obrazki z `https://*.blob.core.windows.net`.

Nazwa pliku: `<marka>-<model>-<rok>.jpg`, małe litery, spacje zamienione na `-`. Typ treści przy wgrywaniu: `image/jpeg`.

Wszystkie zdjęcia są z Wikimedia Commons na licencji CC BY-SA 4.0. Podpis pod zdjęciem na stronie auta ("Zdjęcie: autor, licencja") idzie z `Images:Credits` w `backend/CarRental.Api/appsettings.json` (klucz to nazwa pliku). Jak zmieniamy zdjęcie, poprawiamy też ten wpis.

| Id | Auto | Plik | Autor (Wikimedia Commons, CC BY-SA 4.0) |
|---|---|---|---|
| 1 | Toyota Corolla 2022 | toyota-corolla-2022.jpg | Alexander Migl |
| 2 | Toyota Yaris 2023 | toyota-yaris-2023.jpg | Vauxford |
| 3 | Skoda Octavia 2021 | skoda-octavia-2021.jpg | Mike-fiesta |
| 4 | Volkswagen Golf 2022 | volkswagen-golf-2022.jpg | Vauxford |
| 5 | Kia Sportage 2023 | kia-sportage-2023.jpg | LuvsMG481 |
| 6 | Hyundai i30 2020 | hyundai-i30-2020.jpg | Vauxford |
| 7 | Ford Focus 2021 | ford-focus-2021.jpg | Vauxford |
| 8 | Renault Clio 2022 | renault-clio-2022.jpg | do potwierdzenia (plik Renault_Clio_(V,_Facelift)_–_f_02092025.jpg), na stronie "Wikimedia Commons" |
| 9 | BMW Seria 3 2023 | bmw-seria-3-2023.jpg | Alexander-93 |
| 10 | Audi A4 2022 | audi-a4-2022.jpg | Alexander-93 |
| 11 | Toyota RAV4 2024 | toyota-rav4-2024.jpg | Mr.choppers |
| 12 | Opel Corsa 2021 | opel-corsa-2021.jpg | Alexander Migl |

Wgranie wszystkich naraz (Cloud Shell, pliki w katalogu `zdjecia`):

```bash
STORAGE_CS=$(az storage account show-connection-string -g rg-carrentals -n stcarrentalsjkm --query connectionString -o tsv)
az storage blob upload-batch -d cars -s ./zdjecia --pattern '*.jpg' --content-type image/jpeg --overwrite --connection-string "$STORAGE_CS"
```

Lokalnie zdjęcia też się pokażą, jak ustawimy `Images__BaseUrl` (np. w docker compose: `IMAGES_BASE_URL=https://stcarrentalsjkm.blob.core.windows.net/cars/`).
