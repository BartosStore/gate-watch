# Kontrola webů obchodníků

Comgate provozuje platební bránu pro zhruba 20 000 e-shopů a rádi bychom jejich weby pravidelně kontrolovali proti
databázi známých škodlivých adres. Kolega, který bude kontrolu používat, potřebuje rychle poznat, kde je problém,
co přesně se našlo a proč. Nález pak vyřídí a později se k němu může vrátit v historii. Když se stejný problém
objeví při další kontrole znovu, nemá mu přidělávat práci. Když se ale objeví něco nového, nesmí se to ztratit
jen proto, že starší nález už vyřídil.

Navrhněte a postavte malou aplikaci, se kterou se dá opravdu pracovat. Nejvíc nás zajímá, jestli bude dávat smysl
člověku, který s ní bude trávit čas každý den. Technologie nechceme předepisovat. Sami používáme PHP,
JavaScript s Vue a PostgreSQL, ale vyberte si to, v čem budete nejrychlejší.

## Co přesně kontrolujeme

U každého e-shopu nás zajímá jeho úvodní stránka. Sledujte přesměrování, všechno, co stránka skutečně načte
(skripty, styly, iframy, obrázky a podobně), a také kam vedou odkazy a formuláře. Počítejte i s adresami, které
vzniknou až po spuštění JavaScriptu. Dál než na úvodní stránku nechoďte, na odkazy neklikejte a formuláře
neodesílejte. Adresa, která je jen napsaná v textu nebo v komentáři, nálezem není.

Nalezené adresy porovnejte s blacklistem. Jako blacklist použijte veřejný seznam škodlivých adres, za výchozí považujeme [URLhaus](https://urlhaus.abuse.ch/)
od abuse.ch. Další zdroje klidně přidejte. Na skutečných e-shopech nejspíš žádný nález nenajdete, a proto si do
blacklistu přidejte i vlastní testovací záznamy, třeba adresu skriptu, který kontrolovaný e-shop opravdu načítá.
Aplikace tak bude mít co ukázat. Blacklist slouží jen k porovnání, adresy z něj nenavštěvujte.

Některé weby budou pomalé nebo úplně nedostupné. Kontrola jednoho webu i s přesměrováním má skončit do 10 sekund.
Pokud se kontrolu nepodaří dokončit, nesmí to vypadat, jako že web prošel bez nálezu. Výsledky i vyřízené nálezy
musí přežít obnovení stránky i restart aplikace.

## Jak se mají chovat nálezy

Stejný obchod se stejnou adresou je při každé kontrole pořád ten samý nález a jeho vyřízení zůstává. Stejná adresa
u jiného obchodu je samostatný nález. Nová adresa, která tu dřív nebyla, je nový nevyřízený nález.

Kontrolu stačí spouštět ručně. Přihlašování, role, plánovač ani napojení na systémy Comgate nepotřebujeme. Jak bude
rozhraní vypadat a co v něm uživatel uvidí, necháváme na vás.

## Rozsah a cena

Kontrola má jednou běžet nad všemi e-shopy, takže nám záleží na rozumném poměru ceny a výkonu. Na vyzkoušení si
vyberte vzorek skutečných e-shopů podle vlastního uvážení. Ve výsledcích pak odhadněte, jak dlouho by trvala a kolik
by stála kontrola všech 20 000.

## API

Abychom mohli službu napojit, potřebuje jednotné API. Držte se prosím popisu v [openapi.yaml](openapi.yaml). Pokud
přidáte další endpointy, dopište je do stejného souboru a vysvětlete, co dělají a proč jsou potřeba. API musí
pracovat se stejnou logikou a daty jako aplikace.

## Spuštění

Řešení musí jít z čistého klonu spustit jedním příkazem, například `docker compose up --build`. Postup popište
v README repozitáře.

## Bezpečnost

Kontrolujete skutečné weby a některé z nich mohou být napadené. Spouštějte proto kontrolu odděleně od svého
systému, nejlépe v Dockeru. Na webech se chovejte jako běžný návštěvník. Neodesílejte formuláře, nepřihlašujte se
a weby nezahlcujte.

## Odevzdání

Řešení dejte do soukromého repozitáře na svém GitHubu a pozvěte do něj účet
[**Comgate-a-s**](https://github.com/Comgate-a-s). Kromě kódu tam prosím přidejte md soubory s návodem ke spuštění,
s výsledky (které weby jste proskenovali, k čemu jste došli, odhad doby a nákladů, co se povedlo a co ne) a se
zásadními předpoklady a rozhodnutími.

Do formuláře na této stránce pak vložte odkaz na repozitář a SHA commitu, který máme hodnotit. Přidejte screenshoty
aplikace a konverzaci s AI. Stačí samotná konverzace, výpisy nástrojů a diffy tam být nemusí. Pokud chcete něco
dodat, napište to do komentáře.

Odevzdanou verzi už před schůzkou neměňte, úpravy si necháme na společné probrání. Pokud všechno nestihnete, nevadí.
Odevzdejte, co máte, a napište, co chybí.
