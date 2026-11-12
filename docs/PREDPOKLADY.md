# Předpoklady a rozhodnutí

- Verze jsou zafixované: .NET 10 (LTS), NuGet `Microsoft.Playwright` 1.64.1 a runtime image `mcr.microsoft.com/playwright/dotnet:v1.64.1-noble` (obsahuje .NET 10 runtime), EF Core 10.0.x, Npgsql 10.0.3.
- Původní zadání (README od Comgate) je přesunuté do `docs/ZADANI.md`. `README.md` teď popisuje spuštění.
- Přihlašovací údaje k Postgresu v `docker-compose.yml` jsou jen lokální vývojové hodnoty, ne tajemství. Port DB se nevystavuje ven.
- Aplikace běží v kontejneru pod neprivilegovaným uživatelem `pwuser`, který je součástí Playwright image.
- Kontejner má `shm_size: 1gb`, protože Chromium s výchozími 64 MB padá.
- Migrace se aplikují při startu aplikace (`Database.MigrateAsync`).
- `GET /health` vrací 200 jen tehdy, když je dostupná DB (jinak 503). Endpoint je dopsaný v `openapi.yaml`.
- Dev server frontendu (Vite) přeposílá API cesty na `localhost:8080`. V produkci je UI servírované ze stejného procesu a portu.
- Názvy tabulek a sloupců jsou snake_case. Převod dělá krátká smyčka v `AppDbContext`, bez dalšího balíčku.
- ID běhů a kontrol jsou `uuid`. Stavy (`running`, `ok`, `found`...) se ukládají jako text, aby byly čitelné přímo v DB.
- `merchants` je zdrojem pro cizí klíče z `run_items`, `checks` a `findings`. Obchod se proto založí nebo aktualizuje při `POST /runs`.
- Normalizace URL (`UrlNormalizer`): relativní adresa se převede na absolutní vůči stránce, kde se našla. Povolená jsou jen schémata http/https. Schéma a host se převedou na malá písmena (IDN na punycode), výchozí port 80/443 se odstraní, stejně jako fragment a `user:pass@`. Prázdná cesta se nahradí `/`. Cesta a dotaz zůstávají beze změny; jen .NET `Uri` sjednotí procentové kódování a odstraní `.`/`..`.
- ID nálezu je prvních 32 hex znaků SHA-256 z `merchant_id + "\n" + normalizovaná_url`, malými písmeny.
- Při prvním startu na prázdné DB zapíše EF do logu jednu `fail` hlášku, protože hledá ještě neexistující tabulku `__EFMigrationsHistory`. Je to neškodné, EF ji hned potom vytvoří.
