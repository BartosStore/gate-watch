# Předpoklady a rozhodnutí

Zásadní předpoklady a rozhodnutí řešení. Měření a odhady jsou v [VYSLEDKY.md](VYSLEDKY.md), původní zadání v [ZADANI.md](ZADANI.md).

## Nejdůležitější rozhodnutí (shrnutí)

| Téma | Rozhodnutí |
|---|---|
| Identita nálezu | Dvojice **(obchod, normalizovaná URL)**. ID je prvních 32 hex znaků SHA-256 z `merchant_id + "\n" + url`, takže je stabilní napříč kontrolami. |
| Shoda s blacklistem | **Přesná shoda normalizované URL.** Shoda na úrovni hostu je volitelné rozšíření (krok F). |
| Stavy | DB: `ok`, `found`, `timeout`, `unreachable`. API: `ok`/`found` → `ok`; `timeout`/`unreachable` → `error` s popisem. Nedokončená kontrola **nikdy** nevypadá jako „bez nálezu“. |
| Timeout s nálezem | Nález se uloží a vrátí v `matches`, ale kontrola zůstává `timeout` (v API `error`). |
| Vyřízení | Kontrola nikdy nepřepíše `resolved` ani `note` a nález nikdy nesmaže. Vyřízení je idempotentní a opakované volání jen přepíše poznámku. |
| Blacklist se nenavštěvuje | Ani přímo, ani jako cíl přesměrování: každý požadavek jde přes `route.FetchAsync(MaxRedirects = 0)` a cíl `Location` se kontroluje dřív, než ho prohlížeč otevře. |
| URLhaus | Vyžaduje Auth-Key, a ten nemáme, takže se používá snapshot `data/urlhaus-snapshot.txt`. S klíčem v `URLHAUS_AUTH_KEY` se export stáhne automaticky. |
| Restart | Běhy ve stavu `running` se při startu uzavřou jako `failed` („Přerušeno restartem aplikace“) a znovu se nespouštějí. Hotové výsledky zůstávají. |

## Technologie a běh

- Verze jsou zafixované: .NET 10 (LTS), EF Core 10.0.15, Npgsql 10.0.3, NuGet `Microsoft.Playwright` 1.64.1 a jemu odpovídající runtime image `mcr.microsoft.com/playwright/dotnet:v1.64.1-noble` (obsahuje .NET 10). Frontend: React 19, Vite, MUI 9, MUI X DataGrid, TanStack Query, React Router.
- Jeden kontejner aplikace (API i UI z `wwwroot`, port 8080) a jeden Postgres 17 s healthcheckem a volume `pgdata`. Migrace se aplikují při startu (`Database.MigrateAsync`).
- Přihlašovací údaje k Postgresu v `docker-compose.yml` jsou jen lokální vývojové hodnoty, ne tajemství. Port DB se ven nevystavuje.
- Kontejner má `shm_size: 1gb`, protože Chromium s výchozími 64 MB padá.
- `dotnet publish` ztrácí exec bit Node driveru Playwrightu, proto ho Dockerfile nastavuje znovu (`chmod +x`).
- Při prvním startu na prázdné DB zapíše EF do logu jednu hlášku `fail`, protože hledá ještě neexistující `__EFMigrationsHistory`. Je to neškodné.
- Windows: Docker Compose 5 s buildx 0.38 selže hláškou `failed to execute bake`, když je složka v cmd zapsaná s malým písmenem disku (`c:\…`). Řešení je v README.
- Původní README od Comgate je přesunuté do `docs/ZADANI.md`. `README.md` popisuje řešení a spuštění.

## Datový model

- Tabulky podle zadání: `merchants`, `runs`, `run_items`, `checks`, `findings` (unikátní index `(merchant_id, url)`), `blacklist_entries` a `blacklist_meta`. Názvy jsou snake_case; převod dělá krátká smyčka v `AppDbContext`, bez dalšího balíčku.
- **Rozšíření:** `checks.matched_urls` (text[]) obsahuje shody dané kontroly. Bez něj by `GET /runs/{id}` u staršího běhu nevrátil správné `matches`, protože `findings.last_check_id` se novější kontrolou posune. `alerts` se dohledávají ve `findings` podle (obchod, URL) a vždy ukazují **aktuální** `resolved` a `note`.
- ID běhů a kontrol jsou `uuid`. Stavy se ukládají jako text, aby byly čitelné přímo v DB.
- `merchants` je zdroj cizích klíčů. Obchody z `POST /runs` se proto zapíšou nebo aktualizují; URL se přepíše jen při změně. Kontroluje se poslední známá URL obchodu.
- Seed: když je tabulka `merchants` při startu prázdná, naplní se z `data/merchants-sample.json` (219 e-shopů). Na neprázdnou tabulku se nesahá.

## Normalizace URL

Jedna sdílená funkce `UrlNormalizer` pro požadavky, odkazy, formuláře i blacklist:
- relativní adresa se převede na absolutní vůči stránce, kde se našla (u DOM vůči `document.baseURI`),
- povolená jsou jen schémata `http` a `https`, ostatní se zahodí,
- schéma a host se převedou na malá písmena (IDN na punycode), výchozí port 80/443 se odstraní, stejně jako fragment a `user:pass@`,
- prázdná cesta se nahradí `/`, dotazový řetězec zůstává beze změny. Procentové kódování a `.`/`..` sjednotí .NET `Uri`.

## Blacklist

- Staré veřejné exporty URLhaus (`/downloads/text*/`, `csv*`) vracejí **410 Gone**. Nový export `https://urlhaus-api.abuse.ch/v2/files/exports/<KEY>/recent.csv` vyžaduje bezplatný Auth-Key.
- Pořadí zdrojů při startu:
  1. URLhaus, pokud je nastavené `URLHAUS_AUTH_KEY`,
  2. jinak předchozí úspěšný import z DB,
  3. jinak `data/urlhaus-snapshot.txt` (zatím jen ukázkové záznamy na rezervovaných doménách `.invalid` a `.example`; návod na náhradu skutečnými daty je v hlavičce souboru),
  4. vždy navíc `data/blacklist-custom.txt`.
- `data/blacklist-custom.txt` obsahuje 2 skripty, které weby ze vzorku skutečně načítají (czc.cz, knihydobrovsky.cz a laskakit.cz). Jsou neškodné a slouží jen k předvedení nálezu.
- Import přepíše jen záznamy daného zdroje. Nezměněné záznamy si ponechají `added_at`. Když je URL ve více zdrojích, zůstane první.
- V paměti je blacklist jako slovník `URL → zdroj`, protože zdroj je potřeba u nálezu. Dokud se nenačte, `POST /runs` vrací **503**. Když import selže, opakuje se za 30 s. Stav ukazuje `GET /health`.

## Kontrola webu (`PageScanner`)

- Pro celý proces je jeden sdílený headless Chromium a pro každý web **nový `BrowserContext`**: běžný Chrome user agent, `cs-CZ`, 1366×768, bez stahování, bez oprávnění. Service workery jsou blokované, jinak by obešly `route`. Dialogy se zavírají a vyskakovací okna hned zavírají.
- Zachytí se všechny síťové požadavky včetně těch, které vytvoří JS, každý krok přesměrování a po načtení z DOM `a[href]`, `form[action]` a `iframe[src]`.
  - Atributy se čtou přes `getAttribute` (u SVG i `xlink:href`), ne přes vlastnosti elementů: SVG `<a>.href` je objekt a `form.action` může stránka přepsat polem `<input name="action">` (DOM clobbering).
  - Výsledek ze stránky se přijímá jen jako pole textů.
  - Text a komentáře se neprohledávají.
- **Na nic se neklikne, nic se neodešle a dál než na úvodní stránku se nejde.** Audio a video se jen zaznamenají a nestahují.
- `kind` určuje typ požadavku (script, stylesheet, image, iframe, redirect, link, form, other). Platí první zjištěný typ a síťové požadavky se zaznamenají dřív než DOM.
- **Přesměrování:** Chromium u přesměrování nevolá `route` handler, což ukázal test. Každý požadavek se proto stáhne přes `route.FetchAsync(MaxRedirects = 0)`. Cíl `Location` se zkontroluje proti blacklistu a privátním sítím a zakázaný se zablokuje. Povolený cíl prohlížeč otevře a jeho požadavek projde handlerem znovu.
- Limit je **10 s na celou kontrolu**, včetně přesměrování (`CancellationToken` a zbývající čas pro každý krok). Po `load` se ještě nejvýš 3 s čeká na `networkidle` kvůli pozdním požadavkům z JS.
- Výsledek je `Completed`, `Timeout` nebo `Unreachable` (DNS, spojení, TLS, HTTP ≥ 400 hlavní stránky, zablokovaná hlavní adresa). Adresy zachycené do chvíle chyby se vrátí i u neúspěchu.
- **Obnova po pádu:** když spadne Chromium nebo Playwright driver, další kontrola spustí oba znovu. Kontroly rozpracované v okamžiku pádu skončí jako `unreachable` („Prohlížeč pro kontrolu spadl…“).
- Souběžnost řídí jeden globální `SemaphoreSlim(SCAN_CONCURRENCY)` pro všechny běhy. Výchozí hodnota je 8 podle zadání. Podle měření je pro produkční běh lepší 4 na proces a škálovat víc kontejnery (viz VYSLEDKY).
- Výjimka při kontrole jednoho obchodu se uloží jako chyba jen toho obchodu. Ostatní obchody i běh pokračují.
- Vývojářský příkaz `dotnet Api.dll scan <url>` zkontroluje stránky bez DB a vypíše zachycené adresy.

## API

- Tři povinné operace v `openapi.yaml` jsou beze změny. Přidané endpointy (`/health`, `/alerts`, `/merchants`) jsou zdokumentované v tomtéž souboru i s důvodem. JSON používá snake_case.
- `POST /runs` vrací 400, když:
  - tělo není JSON objekt s polem `merchants`,
  - pole je prázdné nebo má víc než 25 000 položek,
  - `id` není neprázdný text (max. 200 znaků) nebo je v požadavku dvakrát,
  - `url` není absolutní http(s) adresa,
  - text není v UTF-8.

  Obchody i položky běhu se zapisují jedním SQL příkazem, takže **20 000 obchodů vrátí 202 za 2,2–3,3 s** (změřeno).
- `GET /runs/{id}`: neplatné i neznámé ID vrací 404. Při `running` vrací jen dokončené obchody, při `done` právě jeden výsledek na obchod, řazeno podle `merchant_id`. Data se vždy čtou z DB.
- `POST /alerts/{id}/resolve`: poznámka může být prázdná (max. 10 000 znaků); prázdná se v API vrací jako `""`.
- `GET /alerts` řadí od naposledy viděného nálezu. `GET /merchants` řadí podle zadání: nevyřízené nálezy, pak timeout/unreachable, pak OK, pak nezkontrolované. Hledání bere `%` a `_` doslova a stránkování je `{items, total, page, page_size}` s `pageSize` max. 1000.

## UI

- Dvě obrazovky: **Nálezy** (úvodní) a **Obchody**.
- Nálezy:
  - souhrnné karty stavů s proklikem na filtrovaný seznam obchodů (filtr je v URL),
  - tabulka se serverovým stránkováním a výchozím filtrem „nevyřízené“,
  - boční panel s detailem (co, kde, záznam a zdroj blacklistu, časy, kopírování URL, informace o vyřízení) a formulářem „Vyřídit“.
- Obchody: hledání, filtr stavu a tlačítko „Spustit kontrolu“, které pošle všechny obchody z DB do `POST /runs`. Průběh se zjišťuje pollingem `GET /runs/{id}` každé 2 s a přežije i obnovení stránky (`run_id` je v `localStorage`, ošetřené chyby úložiště).
- Barvy stavů jsou jednotné: **červená** = nalezeno nebo nevyřízeno, **žlutá** = nedokončeno (timeout), **oranžová** = nedostupné, **zelená** = OK, **šedá** = nezkontrolováno nebo vyřízeno. Timeout a nedostupné nejsou nikdy zelené.
- Na Nálezech je stálé upozornění, když některé obchody nemají dokončenou kontrolu, protože prázdný seznam nálezů pak není důkaz bezpečí.
- Úvodní stránka obchodu je v UI jen text, ne odkaz, protože web může být napadený. Písmo je systémové, takže UI nestahuje nic z internetu.
- Z obchodu vede proklik na jeho nálezy. Při filtru na jeden obchod je výchozí pohled „Vše“, aby byla vidět i historie vyřízených nálezů.

## Model hrozeb a opatření

Kontrolujeme skutečné weby a některé mohou být napadené. Cílem je, aby napadený web nepoškodil hostitele, neviděl nic citlivého a nemohl kontrolu zneužít.

| Hrozba | Jak by mohla nastat | Opatření |
|---|---|---|
| **Exploit prohlížeče** (škodlivý skript využije chybu v Chromiu) | Napadený web načte exploit | Kontrola běží v **Docker kontejneru**, ne na hostiteli, pod neprivilegovaným uživatelem `pwuser`. Kontejner nemá připojené složky hostitele ani Docker socket, nemá žádná tajemství (jen lokální heslo k DB, volitelně klíč URLhaus) a nevystavuje port DB. Chromium je aktuální verze z fixovaného image. Vlastní sandbox Chromia je vypnutý (výchozí nastavení Playwrightu v kontejneru), takže izolační hranicí je kontejner. |
| **Únik dat mezi weby** | Cookies nebo úložiště jednoho webu ovlivní kontrolu jiného | Každý web má nový `BrowserContext` (žádné sdílené cookies ani storage), který se po kontrole zavře. |
| **Stažení a spuštění souboru** | Web vyvolá stažení | `AcceptDownloads = false` a audio i video se nestahují. |
| **Zneužití oprávnění** (kamera, poloha, notifikace) | Web žádá o oprávnění | Context nemá žádná oprávnění. Dialogy se zavírají a vyskakovací okna hned zavírají. |
| **Návštěva škodlivé adresy** | Stránka načte nebo přesměruje na adresu z blacklistu | Požadavek se zaznamená a **zablokuje dřív, než odejde**, a to i pro cíle přesměrování. Ověřuje to automatický test s lokálním webem. |
| **SSRF / útok na vnitřní síť** | Stránka pošle požadavek na `localhost`, `169.254.169.254` (metadata cloudu), `10.x` apod. | Blokují se `localhost`, privátní, loopback a link-local rozsahy IPv4 i IPv6 (včetně mapovaných adres) a schémata jiná než http/https, také pro cíle přesměrování. *Omezení:* kontroluje se IP zapsaná v URL, ne výsledek DNS. Doména, která se přeloží na privátní IP, projde (volitelný krok F). V produkci doporučuji kontejneru zakázat odchozí provoz do vnitřních sítí i na úrovni sítě (firewall nebo egress policy). |
| **Interakce s webem** (odeslání formuláře, přihlášení) | Kontrola by měnila stav na cizím webu | Na nic se neklikne, nic se neodešle a dál než na úvodní stránku se nejde. Formuláře a odkazy se jen čtou z DOM. |
| **Zahlcení webu** | Příliš mnoho požadavků | Každý web se kontroluje jednou za běh, s limitem 10 s, souběžnost je omezená a user agent běžný. |
| **Podvržená data ze stránky** | Skript přepíše globální objekty nebo DOM, aby zmátl čtení odkazů | Atributy se čtou přes `getAttribute`, výsledek se ověřuje (jen pole textů) a každá URL projde normalizací. Nečekaná data nikdy neshodí běh, maximálně chybu jednoho obchodu. |
| **Pád prohlížeče** (třeba vyvolaný stránkou) | Chromium nebo driver spadne | Automatická obnova. Rozpracované kontroly se označí jako chyba, ne jako OK. |
| **Škodlivý obsah v UI** | Adresa nebo text z webu by se v UI vykonaly | React vše vykresluje jako text. Úvodní stránka obchodu ani škodlivá adresa nejsou v UI klikací odkazy. |

## Testy

- Unit testy: normalizace URL, ID nálezu, parser blacklistu, blokace privátních sítí a validace vstupu.
- Integrační testy nad Postgresem (databáze `gatewatch_test`) s falešným scannerem ověřují pravidla nálezů, stavy, restart a seznamy.
- Testy s Chromiem proti lokálnímu testovacímu webu ověřují: přesměrování, JS požadavky, DOM včetně SVG a „přepsaného“ formuláře, že se nikdy nestáhne blacklistovaná adresa, timeout, HTTP chybu, DNS, privátní síť a obnovu po pádu.
- Celá sada (103 testů) běží přes `docker compose run --rm --build tests`. Na hostiteli `dotnet test` testy s DB a prohlížečem přeskočí.

## Otevřené body a známá omezení

- **Asi 34–50 % webů kontrola nedokončí** (ochrana proti botům 403/429 a timeouty). Příčina: kvůli blokaci cílů přesměrování posílá požadavky Node driver Playwrightu místo Chromia, takže ho ochrany poznají, a jeden driver je úzké hrdlo. Zadání je splněné, protože nedokončení se vždy zobrazí. Pravděpodobné zlepšení je posílat požadavky přímo z Chromia a cíle přesměrování blokovat jinak (CDP `Fetch`). Převzít ho lze jen tehdy, když projde test `Captures_requests_redirects_dom_and_never_fetches_blacklisted_urls`. Zatím nezkoušeno.
- Client Hints (`sec-ch-ua`) prozrazují `HeadlessChrome`, i když hlavička `User-Agent` je běžná.
- Ochrana proti SSRF přes DNS (doména → privátní IP) není (volitelný krok F).
- Historie kontrol jednoho obchodu a znovuotevření nálezu nejsou (volitelné kroky B a E).
