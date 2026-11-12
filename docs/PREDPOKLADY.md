# Předpoklady a rozhodnutí

- Verze jsou zafixované: .NET 10 (LTS), NuGet `Microsoft.Playwright` 1.64.1 a runtime image `mcr.microsoft.com/playwright/dotnet:v1.64.1-noble` (obsahuje .NET 10 runtime), EF Core 10.0.x, Npgsql 10.0.3.
- Původní zadání (README od Comgate) je přesunuté do `docs/ZADANI.md`. `README.md` teď popisuje spuštění.
- Přihlašovací údaje k Postgresu v `docker-compose.yml` jsou jen lokální vývojové hodnoty, ne tajemství. Port DB se nevystavuje ven.
- Aplikace běží v kontejneru pod neprivilegovaným uživatelem `pwuser`, který je součástí Playwright image.
- Kontejner má `shm_size: 1gb`, protože Chromium s výchozími 64 MB padá.
- Migrace se aplikují při startu aplikace (`Database.MigrateAsync`).
- `GET /health` vrací 200 jen tehdy, když je dostupná DB (jinak 503). Endpoint je dopsaný v `openapi.yaml`.
- Dev server frontendu (Vite) přeposílá API cesty na `localhost:8080`. V produkci je UI servírované ze stejného procesu a portu.
