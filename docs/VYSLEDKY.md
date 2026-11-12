# Výsledky: vzorek, měření a odhad pro 20 000 e-shopů

Měřeno 2026-11-12. U každého čísla je uvedeno, jestli je **změřené**, nebo **odhad**.

## Vzorek

- `data/merchants-sample.json` obsahuje **219 skutečných českých e-shopů** různých velikostí a oborů:
  - velké obchody a marketplace (czc, datart, alza, kaufland, aukro, allegro),
  - potraviny a drogerie (rohlik, kosik, itesco, billa, globus, dm, rossmann, teta),
  - lékárny (benu, pilulka, drmax),
  - móda a obuv, české i zahraniční řetězce (zoot, footshop, bibloo, ccc, deichmann, reserved, sinsay, kik),
  - sport a výživa (sportisimo, intersport, gymbeam, nutrend),
  - knihy a hudba (kosmas, knihydobrovsky, martinus, grada, supraphonline),
  - nábytek, dům a zahrada (ikea, jysk, hornbach, tescoma, thun, dormeo),
  - elektronika a součástky (expert, megapixel, laskakit, hwkitchen, dratek),
  - zvířata, kosmetika (manufaktura, ryor, nobilis), optika, autodíly a pneumatiky,
  - menší specializované obchody (rybářské a army potřeby, pražírny kávy, vinotéky, nářadí, výtvarné potřeby…).
- **Ověření:** u každého kandidáta jsem zkontroloval DNS, stáhl úvodní stránku a podle titulku vyřadil, co není e-shop. Vyřazeno bylo:
  - 32 domén bez DNS,
  - 8 parkovaných domén přesměrovaných na reklamu,
  - 1 kasinový spam,
  - firemní a nakladatelské weby bez e-shopu, magazíny a duplicity.

  Na prostý `curl` 403 nebo „ověření, že nejste bot“ ve vzorku nechávám záměrně, protože ukazují reálné podmínky.
- Platformy (Shoptet, Magento, vlastní řešení…) jsem cíleně nevybíral ani nevyhodnocoval, protože aplikace zachycené URL neukládá. Vzorek je proto pestrý hlavně podle velikosti a oboru.

## Prostředí měření (změřeno)

- Docker Desktop na Windows 11 a vývojářský počítač. Docker VM měla 12 vCPU a 31 GiB RAM, připojení bylo kancelářské.
- Aplikace běžela v jednom kontejneru: jeden proces, jeden Chromium a jeden Playwright driver. Limit je 10 s na web. Každé měření běželo na čisté databázi.
- Blacklist: ukázkový snapshot URLhaus (5 záznamů) a 3 vlastní záznamy. Velikost blacklistu výkon prakticky neovlivňuje, protože jde o vyhledání v hashovací tabulce.
- CPU a paměť jsem vzorkoval přes `docker stats` během běhu. 100 % odpovídá 1 jádru.

## Výsledky pro 219 webů (změřeno)

| Souběžnost | Celkem | Průměr na web | Medián | 95. percentil | ok + found | timeout | unreachable | CPU průměr / max | Paměť max |
|---|---|---|---|---|---|---|---|---|---|
| 4 | 242,7 s | 4,35 s | 4,29 s | 10,0 s | **144 (65,8 %)** | 13 (5,9 %) | 62 (28,3 %) | 449 % / 826 % | 988 MiB |
| **8 (výchozí)** | **192,5 s** | **6,67 s** | 7,23 s | 10,0 s | 110 (50,2 %) | 54 (24,7 %) | 55 (25,1 %) | 611 % / 848 % | 966 MiB |

- Dřívější měření na 93 webech (stejná sada a k ní menší podmnožina) dopadlo obdobně: při souběžnosti 8 vyšlo 79,6 s, průměr 6,46 s a 55 % ok; běh, který zadavatel spustil z UI, vyšel 78,9 s s průměrem 6,45 s. **Výsledky jsou opakovatelné.**
- Při souběžnosti 16 (na 93 webech) skončí 80 % webů timeoutem, protože jeden driver zátěž nezvládne. Proto souběžnost 16 nedoporučuji.
- U dokončených webů prohlížeč zachytil v průměru 451–470 adres, nejvíc 8 956 na jedné stránce.
- **Nálezy:**
  - czc.cz (`ngastatic.com/s4c/tracker.js`),
  - knihydobrovsky.cz a laskakit.cz (oba `cdn.retino.com/badgeInjector.js`).

  Na skutečných datech se tak ukázala pravidla „stejná URL u jiného obchodu = samostatný nález“ a „timeout s nálezem zůstává timeout, ale nález se uloží“ (knihydobrovsky při souběžnosti 8). Všechny tři skripty jsou neškodné a do `data/blacklist-custom.txt` jsou přidané jen kvůli ukázce.

### Proč tolik chyb (změřeno a rozbor)

Rozpad chyb pro 219 webů:

| Chyba | Souběžnost 4 | Souběžnost 8 |
|---|---|---|
| HTTP 403 / 429 (ochrana proti botům) | 34 | 34 |
| Timeout (nedokončeno do 10 s) | 13 | 54 |
| `ERR_CONNECTION_FAILED` / `REFUSED` | 14 | 14 |
| `ERR_NAME_NOT_RESOLVED` (z kontejneru) | 5 | 5 |
| Pád prohlížeče, po kterém se sám obnovil | 5 | 0 |
| Ostatní (`ERR_FAILED`, `ERR_ABORTED`) | 4 | 2 |

1. **Ochrana proti botům (asi 15 % webů).** Kvůli zadání „adresy z blacklistu se nenavštěvují ani přes přesměrování“ jde každý požadavek přes `route.FetchAsync`. Requesty tedy neposílá Chromium, ale Node driver Playwrightu, a ochrany typu Cloudflare nebo Akamai ho podle otisku TLS a HTTP poznají. Řešení je popsané v [PREDPOKLADY.md](PREDPOKLADY.md) jako otevřený bod.
2. **Úzké hrdlo driveru.** Při souběžnosti 8 je timeoutů 54, při souběžnosti 4 jen 13 a 12 z nich jsou stejné weby v obou bězích. Asi **40 timeoutů při souběžnosti 8 tedy způsobuje zátěž**, ne weby. Všechny požadavky totiž prochází jedním Node procesem. CPU přitom zdaleka není vytížené (6 z 12 jader).
3. **Spotřeba CPU je stabilní, asi 5–5,4 CPU-sekundy na web** (souběžnost 4: 449 % × 243 s ÷ 219 ≈ 5,0 CPU-s; souběžnost 8: 611 % × 193 s ÷ 219 ≈ 5,4 CPU-s).
4. **Pád prohlížeče nebo driveru.** První měření odhalilo, že po pádu driveru selhaly všechny další kontroly. Opravil jsem to: prohlížeč i driver se teď spustí znovu a kontroly, které běžely v okamžiku pádu, skončí jako `unreachable` s popisem. V druhém měření se pád jednou opravdu stal a postihl 5 kontrol, zbytek běhu proběhl normálně. Oprava je pokrytá testem, který driver zabije.
5. Měření našlo i chybu ve čtení DOM: SVG odkazy mají `href` jako objekt a `form.action` může stránka „přepsat“ polem `name="action"`. Opraveno, atributy se teď čtou přes `getAttribute`, a pokryto testem.

Žádná chyba se nezobrazuje jako „bez nálezu“: v API má stav `error` s popisem, v UI je žlutá nebo oranžová.

## Odhad pro 20 000 e-shopů (odhad)

Vzorec ze zadání: **čas na web × 20 000 ÷ souběžnost.**

| Varianta | Výpočet | Doba | Dokončeno (podle vzorku) |
|---|---|---|---|
| 1 instance, souběžnost 8 | 6,67 s × 20 000 ÷ 8 | **16 700 s ≈ 4,6 h** | ≈ 50 % |
| 1 instance, souběžnost 4 | 4,35 s × 20 000 ÷ 4 | **21 750 s ≈ 6,0 h** | ≈ 66 % |
| Kontrola podle změřené propustnosti (souběžnost 8: 219 webů / 192,5 s) | 20 000 ÷ 1,14 webů/s | 17 600 s ≈ 4,9 h | |
| **Doporučeno:** 4 instance po souběžnosti 4 | 6,0 h ÷ 4 | **≈ 1,5 h** | ≈ 66 % |

**Doporučení:** souběžnost 4 na jeden proces a škálování přidáváním kontejnerů. Souběžnost 8 je o 25 % rychlejší, ale nedokončí asi o 40 webů z 219 víc (zhruba 3 600 z 20 000), což by zbytečně zatěžovalo člověka, který výsledky prochází.

**Potřebný výkon:** asi 5 CPU-s × 20 000 ≈ 100 000 CPU-s ≈ **28 CPU-hodin** na celý běh. Jeden proces se souběžností 4 využije průměrně 4,5 jádra (krátce až 8) a nejvýš 1 GiB RAM. Vhodná instance pro jeden proces má 4–8 vCPU a 2–4 GB RAM.

### Cena (odhad)

Předpoklady:
- platí se za CPU-hodiny (28 CPU-h na běh) a přirážka asi 30 % pokrývá nevyužitou kapacitu a start instancí;
- DB, úložiště a provoz jsou zanedbatelné. Data se nestahují a odpovědi jdou jen přes RAM. Síťový provoz jsem neměřil;
- ceny jsou z veřejných ceníků a srovnávačů ke 2026-11 a před nasazením je potřeba je ověřit u poskytovatele.

| Instance | Cena za hodinu (8 vCPU) | ≈ za vCPU-h | 1 kompletní běh (28 CPU-h + 30 %) | Denně po dobu 30 dní |
|---|---|---|---|---|
| AWS c7i.2xlarge, eu-central-1, on-demand | ≈ $0,41 | $0,051 | **≈ $1,9** | ≈ $56 |
| AWS c7i.2xlarge, spot | ≈ $0,20 | $0,025 | ≈ $0,9 | ≈ $28 |
| Hetzner CCX33 (8 dedikovaných vCPU, 32 GB), fakturace po hodinách | ≈ $0,22 | $0,028 | **≈ $1,0** | ≈ $31 |

Varianta „jedna instance s 8 vCPU a souběžností 4, 6 h“ vyjde na Hetzneru asi na $1,3 a na AWS asi na $2,5 za běh.

## Co se povedlo a co ne

**Povedlo se**
- Kontrola respektuje zadání. Blacklistované adresy se nestahují ani jako cíl přesměrování; ověřuje to automatický test s lokálním webem. Na nic se neklikalo, nic se neodesílalo, limit 10 s se dodržel (max. 10,0 s).
- Nedokončené kontroly nikdy nevypadají jako „bez nálezu“ a nález zachycený během timeoutu se uloží.
- Výsledky jsou opakovatelné a se změřenými parametry lze plánovat.
- Měření na větším vzorku odhalilo dvě skutečné chyby: neobnovení po pádu driveru a čtení SVG odkazů a „přepsaných“ formulářů. Obě jsou opravené a pokryté testy.

**Nepovedlo se nebo zůstává otevřené**
- **Asi 34 % (souběžnost 4) až 50 % (souběžnost 8) webů kontrola nedokončí.** To je hlavní slabina současného řešení. Pravděpodobné zlepšení: posílat požadavky přímo z Chromia a cíle přesměrování blokovat jinak (např. CDP `Fetch`). Měl by klesnout počet 403 i zátěž driveru. Zatím jsem to nezkoušel (otevřený bod).
- URLhaus vyžaduje Auth-Key, takže se používá jen ukázkový snapshot. Skutečná data z URLhaus jsem nestahoval.
- Odhad doby a ceny vychází z vývojářského počítače, ne z cílové cloudové instance. Rozdíl v síti a CPU může být ±50 %.
- Proč driver občas spadne, se nepodařilo zjistit, protože v logu není nic kromě „Process exited“. Aplikace se ale z pádu zotaví.
