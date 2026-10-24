# AION 2 Companion – feature TODO

Utolsó kutatás: 2026-10-22

Ez a lista a Companion hosszú távú ötlettára. Nem hibajegyzék; a technikai hibák külön a `PROJECT_ISSUES.md` fájlban vannak.

Jelölések:

- `[~]` Már készül vagy van hozzá működő alap.
- `[ ]` Tervezett funkció.
- `[?]` Előbb packet/opcode vagy játékadat-kutatás kell hozzá.
- `[web]` Saját szerveres vagy webes háttér is kell hozzá.

## 0. Ami már készül

- [~] **Automatikus capture** – `--auto` esetén a packet figyelés a programmal együtt indul.
- [~] **Élő DPS overlay** – Játékosonként mutatja a sebzést, DPS-t, részesedést és critet.
- [~] **Játékos részletei** – Skillenkénti sebzés, DPS, találat és crit bontás.
- [~] **Skill timeline** – Időrendben mutatja a harc alatt használt skilleket és healeket.
- [~] **Boss Assistant / DBM** – Mechanika-idővonal és figyelmeztető overlay alapja.
- [~] **Map Helper** – Az AION 2 térképére illeszkedő overlay és automatikus térképészlelés.
- [~] **NFG játékadatok** – Class-, skill- és mobadatok titkosított adatfájlokból.
- [~] **Mentett overlay pozíciók** – A DPS és DBM ablak visszakerül az előző helyére.
- [~] **Kapcsolható fájllogolás** – Debug log csak kérésre íródik lemezre, méretkorláttal.
- [~] **Fejlesztői Opcode Explorer** – Ismeretlen packetek és opcode-ok kutatásához.

## 1. Első kiadás – a stabil Companion alapja

- [~] **Combat Log** – A lezárt és kilépéskor megszakított fightok damage/heal/skill/timeline adatokkal tartósan visszanézhetők.
- [ ] **Fight-váltó nyilak** – Egy kattintással lépj az előző vagy következő harcra minden részletes nézetben.
- [ ] **Automatikus fight mentés** – Minden bossfight és hosszabb normál harc magától bekerül az előzményekbe.
- [~] **Fight kereső** – Szűrés boss, dátum, játékos, class és jegyzet alapján.
- [~] **Fight összehasonlítás** – Két fight eredménye és játékosonkénti eltérése egymás mellett.
- [~] **Fight címkék és jegyzetek** – Kedvenc jelölés és rövid megjegyzés egy runhoz; saját cím még nincs.
- [~] **Fight törlés és archiválás** – A felesleges harcok törölhetők, a kedvencek automatikusan megmaradnak.
- [~] **Adatmegőrzési limit** – Beállítható darabkorlát a historyhoz; a kedvenceket nem törli.
- [ ] **Helyes damage/heal szétválasztás** – Az ellenfél sebzése soha ne kerüljön healnek számolva.
- [ ] **Damage Taken nézet** – Megmutatja, ki mennyi sebzést kapott és mitől.
- [ ] **Healing és overheal nézet** – Heal, HPS, célpontok és a felesleges gyógyítás bontása.
- [?] **Shield / absorb nézet** – Pajzsok és elnyelt sebzés követése, ha a packetekből kinyerhető.
- [ ] **Halálok és death recap** – Ki, mikor és az utolsó találatok alapján mitől halt meg.
- [ ] **Boss HP sáv** – Aktuális és maximum HP, százalék és fázishatárok.
- [ ] **Wipe felismerés** – A sikertelen bosspróba külön encounterként legyen elmentve.
- [ ] **Encounter felismerés** – Boss, dungeon, map és nehézség automatikus azonosítása.
- [ ] **Fight idő és hátralévő limit** – Harcidő, boss enrage és dungeon időkorlát.
- [ ] **Pause / reset** – A mérés kézzel szüneteltethető és újraindítható.
- [~] **Automatikus overlay megjelenés** – Harc elején feljön, harc után késleltetve eltűnik.
- [ ] **Click-through mód** – Zárolva az overlay minden kattintást átenged a játéknak.
- [ ] **Overlay profilok** – Külön elrendezés solo, party, raid, PvP és stream használathoz.
- [ ] **Globális hotkeyek** – Overlay, reset, módváltás és pause billentyűi szabadon állíthatók.
- [ ] **Első indítás varázsló** – Npcap, hálózati adapter, jogosultság és AION 2 kliens ellenőrzése.
- [~] **Tálca mód** – Választható háttérben maradás, első bezárásos döntéssel és tray menüvel; értesítések még nincsenek.

## 2. DPS és harcelemzés

- [ ] **Öt meter mód** – Damage, DPS, Heal, Damage Taken és kompakt All-in-One nézet.
- [ ] **Target szerinti bontás** – Külön összesítés minden mobra és bossra.
- [ ] **Group / class szűrő** – Csak a kiválasztott group, class vagy játékos látszódjon.
- [ ] **Skill részletek** – Összes sebzés, DPS, hit, átlag, minimum, maximum és részarány.
- [ ] **Találatminőség** – Crit, back, front, perfect, double, multi-hit és miss/parry arány.
- [ ] **DPS görbe** – A teljes fight teljesítménye idővonalon, tüskék és üresjáratok jelölésével.
- [ ] **Skill rotáció** – Minden cast sorrendben, cooldown-kihagyásokkal és üres időkkel.
- [?] **Buff/debuff uptime** – Játékosonkénti hatásidő és az érintett skillek.
- [?] **Stagger / break elemzés** – Ki mennyivel járult hozzá a boss megtöréséhez.
- [ ] **Fázisokra bontott fight** – Külön DPS és események bossfázisonként.
- [ ] **Burst ablakok** – Legjobb 5, 10 és 30 másodperces sebzési szakasz.
- [ ] **Aktív idő** – Valós harci uptime, halott és tétlen idő külön számolva.
- [ ] **Summon/pet hozzárendelés** – A summon teljes sebzése biztosan a gazdájához kerüljön.
- [ ] **Skill szint felismerés** – A skill verziója/szintje jelenjen meg a meterben és timeline-on.
- [ ] **Gear Score és Combat Power** – Partyadatból GS/CP megjelenítése, ha elérhető.
- [?] **Saját stat snapshot** – A fight eleji statok, gear, skillek és Daevanion build mentése.
- [~] **Személyes rekordok** – Azonos boss és játékos alapján DPS-, HPS- és legnagyobb hit rekord kiemelése.
- [~] **Eredmény másolása** – Táblázatos Discord-formátum a vágólapra; játékchat és kép még nincs.
- [ ] **JSON/CSV export** – Helyi elemzéshez exportálható, érzékeny adatok nélkül.
- [ ] **Importálható fight fájl** – Másik gépről kapott combat log helyben megnyitható.
- [ ] **Plausibility check** – A mért party sebzés összevetése a boss elvesztett HP-jával.
- [ ] **PvE/PvP külön mód** – A két típusú harc és statisztika ne keveredjen össze.
- [ ] **PvP meter** – Damage, heal, kill, death, assist és K/D battlegroundon vagy Abyssban.

## 3. Boss Assistant / DBM

- [ ] **Boss cast bar** – Skillnév, hátralévő castidő és célpont élőben.
- [ ] **Mechanika visszaszámlálás** – Következő fontos mechanika és pontos hátralévő idő.
- [ ] **Hangbemondás** – Interrupt, stack, spread, dodge, orb és wipe mechanikák felolvasása.
- [ ] **Személyes riasztás** – Külön jelzés, ha te vagy a boss célpontja.
- [ ] **AREA / LETHAL jelzés** – Területi vagy várhatóan halálos képesség erős kiemelése.
- [ ] **Interrupt tracker** – Megszakítandó skillek és sikeres/sikertelen interruptok.
- [ ] **Mechanika timeline** – A teljes bossminta előre látható és fight közben követhető.
- [ ] **Fázisváltás** – HP vagy esemény alapján automatikusan váltson a következő mechanikasorra.
- [ ] **Tanuló mód** – Ismeretlen boss castjait és becsapódási idejét helyben gyűjtse kutatáshoz.
- [ ] **Egyéni boss pack** – Saját időzítés, szöveg, hang és szabály importálható/exportálható.
- [ ] **Dungeon guide** – Rövid bossmechanika-leírás képekkel, közvetlenül a Companionban.
- [ ] **Pull előtti checklist** – Ready, buff, food, potion és fontos cooldown emlékeztető.
- [ ] **Target-of-target** – Mutassa, hogy a kijelölt boss kit céloz.
- [?] **Healer panel** – Célpont HP-ja és fontos buff/debuff állapotai egy kompakt blokkban.

## 4. Map Helper és világfunkciók

- [~] **Játékbeli map overlay** – A marker-réteg az AION 2 nyitott térképére illeszkedik.
- [ ] **Automatikus map felismerés** – A jobb alsó fix arany kezelőikon alapján észlelje a megnyitott térképet.
- [ ] **Zoom- és pan-követés** – A markerek minden zoomszinten és eltolásnál a helyükön maradnak.
- [ ] **Map/zone felismerés** – Verteron, Altgard, Poeta, Ishalgen, Reshanta és instance mapok.
- [ ] **Rétegek** – Kategóriánként kapcsolható markerlista, keresővel és gyorsszűrővel.
- [ ] **Hidden Cube térkép** – Lehetséges cube spawnok, piros/arany jelölés és megtalált állapot.
- [ ] **Empyrean Trace tracker** – Megtalált és hiányzó trace-ek karakterenként.
- [ ] **Pet Soul térkép** – A hiányzó pet soulokat dobó mobok és chestek helye.
- [ ] **Gathering térkép** – Érc, gem, növény, fa és Odyle lelőhelyek.
- [ ] **Boss és named mob térkép** – Spawnhely, dropok és aktív respawn timer.
- [ ] **Rift térkép** – Portálhelyek, nyitási idő és értesítés.
- [ ] **Kibelisk / teleport térkép** – A célhoz legközelebbi teleport kiemelése.
- [ ] **Dungeon és sealed dungeon térkép** – Bejáratok, feltételek és jutalmak.
- [ ] **Stronghold, artifact és field event réteg** – Világesemények helye és ideje.
- [ ] **NPC és service kereső** – Vendor, craft, storage, quest és más szolgáltatások.
- [ ] **Quest marker réteg** – Questkezdők, célok és láncok térképen.
- [ ] **Saját waypoint** – Egyéni marker, név, szín és jegyzet.
- [ ] **Útvonaltervező** – A kijelölt pontokat hatékony farm- vagy gyűjtőútvonallá rendezi.
- [ ] **Napi útvonal** – A még hiányzó gyűjthető dolgokból automatikus kör.
- [ ] **Haladásmentés** – Markerállapotok karakterenként, export/import lehetőséggel.
- [?] **Játékospozíció követése** – A saját pozíció packetből, memóriaolvasás nélkül.
- [?] **Pet/gathering radar** – Közeli célpont távolsága és iránya valós időben.
- [ ] **Edge arrows** – A képernyő szélén nyíl mutat a legközelebbi kijelölt célra.
- [ ] **Map notes** – Saját és megosztható rövid jegyzetek egy ponthoz.

## 5. Timerek és napi teendők

- [ ] **Event timetable** – Minden napi/heti esemény a helyi időzónádban.
- [ ] **Egyedi riasztások** – Eseményenként külön hang, hangerő és előzetes figyelmeztetés.
- [ ] **World boss timerek** – Szerverenkénti respawnidő és tálcaértesítés.
- [ ] **Rift timer** – Következő nyitás, zárás és választható riasztás.
- [ ] **Napi/heti reset** – Visszaszámlálás a szerver resetig.
- [ ] **Automatikus checklist** – A játékadatból jelölje a kész és hátralévő feladatokat.
- [ ] **Karakterenkénti checklist** – Minden alt külön napi/heti állapottal.
- [?] **Dungeon belépők** – Maradék belépések, kulcsok és heti limitek.
- [?] **Odyle Energy** – Aktuális mennyiség, cap és elvesző energia előrejelzése.
- [?] **Kinah / Abyss Point limitek** – Heti limit és hátralévő megszerezhető mennyiség.
- [?] **Arena / Battlefield állapot** – Belépések, rating, szezon és következő esemény.
- [ ] **Lejárat szerint rendezés** – Legfelül az legyen, ami a leghamarabb elveszne.
- [ ] **Saját emlékeztető** – Tetszőleges időpont, ismétlés és megjegyzés.

## 6. Karakter, build és adatbázis

- [ ] **Karakterprofil** – Class, szint, szerver, faction, legion, gear, statok és build egy helyen.
- [ ] **Több karakter kezelése** – Main és altok automatikus felismerése, külön előzményekkel.
- [ ] **Item adatbázis** – Statok, szint, rarity, enhance lépcsők és beszerzési hely.
- [ ] **Skill adatbázis** – Szintenkénti értékek, cooldown, költség, chain és ikon.
- [ ] **Buff/debuff adatbázis** – Hatás, érték, időtartam és forrás.
- [ ] **Monster és boss adatbázis** – HP, hely, drop, drop chance és kapcsolódó dungeon.
- [ ] **Quest adatbázis** – Lépések, jutalmak, előfeltételek és questláncok.
- [ ] **Dungeon adatbázis** – Bossok, módok, chestek, loot és heti farmidő.
- [ ] **Recipe adatbázis** – Alapanyagok, források, siker/combo esély és várható költség.
- [ ] **Globális kereső** – Item, skill, mob, quest, recipe és map marker egy keresőből.
- [ ] **Build Planner** – Gear, skillek, stigma, pet, Daevanion és Pantheon összeállítása.
- [ ] **Daevanion planner** – Pontútvonal, költség és megosztható build.
- [ ] **Stigma planner** – Elérhető helyek, kombinációk és buildmentés.
- [ ] **Gear összehasonlító** – Két item vagy teljes szett statkülönbsége.
- [?] **Stat kalkulátor** – Megbecsüli, hogyan változtatja egy stat a sebzésedet.
- [ ] **Enhancement kalkulátor** – Várható ár, esély, percentilis és megadott budget.
- [ ] **Crafting kalkulátor** – Hiányzó alapanyag, teljes költség és beszerzési út.
- [ ] **Drop finder** – Egy itemhez minden mob, boss, dungeon és várható farmmennyiség.
- [ ] **Farm finder** – Cél vagy alapanyag alapján legjobb farmhely és útvonal.
- [ ] **Set bonus kalkulátor** – Aktív és következő bónuszok összevetése.
- [ ] **Pet collection** – Meglévő petek, soul progress, genus és következő fejlesztés.
- [ ] **Collection tracker** – Wings, titles, pets, Theostones, Manastones, Arcana és setek.
- [ ] **Class guide** – Rövid szerep, statprioritás, rotáció, PvE/PvP build és tippek.
- [ ] **Party comp planner** – Role-ok, buffok és hiányzó partyfunkciók áttekintése.
- [ ] **Patch változások** – Mi változott az előző NFG/játékverzió óta itemenként és skillenként.

## 7. Megosztás, ranglisták és stream

- [ ] **OBS Browser Source** – Átlátszó DPS, DBM, timer és map overlay streamhez.
- [ ] **Második gépes overlay** – Helyi hálózaton, véletlen kulccsal elérhető read-only webnézet.
- [web] **Megosztható combat log** – Fight feltöltése és megnyitható publikus vagy privát link.
- [web] **Boss ranglista** – Boss, class, role, régió és patch szerinti eredmények.
- [web] **Class statisztika** – Medián, percentilis és népszerű skillek/build azonos bossnál.
- [web] **Legjobb runok** – Leggyorsabb clear, legjobb DPS/HPS és kevés halál.
- [web] **Karakterprofil kereső** – Nyilvános gear, build, rekordok és fightok, csak opt-innel.
- [web] **Legion oldal** – Tagok, közös runok, rekordok és fejlődés.
- [~] **Discord-formátum** – Egy gombbal kulturált rangsor vagy fightösszefoglaló a vágólapra.
- [ ] **Anonimizálás** – Játékosnevek automatikus elrejtése streamen és exportban.
- [ ] **Adatküldés előnézet** – Feltöltés előtt pontosan mutassa, milyen adat hagyja el a gépet.
- [ ] **Minden feltöltés opt-in** – Alapból semmi ne kerüljön ki a felhasználó gépéről.

## 8. Alkalmazásminőség és későbbi extrák

- [ ] **Automatikus frissítés** – Új verzió letöltése háttérben, telepítés játékidőn kívül.
- [ ] **Installer és portable kiadás** – Saját .NET runtime, shortcut és tiszta uninstall.
- [ ] **Npcap ellenőrző** – Telepítés, WinPcap mód, adapter és capture-jogosultság diagnosztika.
- [ ] **Hálózati overlay** – Ping, jitter, packet loss, forgalom és online idő.
- [ ] **Hálózati riport** – Anonimizált diagnosztikai csomag támogatáshoz.
- [ ] **Backup / restore** – Settings, history, map progress és saját boss pack mentése.
- [ ] **Profilozott beállítások** – Gépenként, karakterenként vagy felbontásonként eltérő layout.
- [ ] **Témák** – Nexforge dark alap, opacity, méret, színek és color-blind mód.
- [ ] **Többnyelvű UI** – Elsőként angol és magyar, később közösségi fordítások.
- [ ] **Akadálymentesítés** – Skálázható betű, erős kontraszt és csak színtől nem függő jelzések.
- [ ] **Plugin rendszer** – Felülvizsgált, jogosultságot kérő, elkülönített kiegészítők.
- [ ] **Adatfrissítő** – Patch után új NFG csomag ellenőrzése és kompatibilitási figyelmeztetés.
- [ ] **Crash recovery** – Hibás leállás után az utolsó fight és beállítások visszaállítása.
- [ ] **Adatbázis-karbantartás** – Méret kijelzése, régi adatok takarítása és javítása.
- [ ] **Teljes adatvédelmi oldal** – Capture, logok, helyi fájlok és opcionális hálózati funkciók leírása.
- [ ] **Donate helye** – Beállítható támogatási link a README-ben és az About oldalon.

## 9. Tudatosan nem cél

- [ ] **Nincs memóriaolvasás vagy injection** – A Companion maradjon passzív és a játékfolyamattól különálló.
- [ ] **Nincs bot vagy inputautomatizálás** – Ne mozogjon, célozzon, harcoljon vagy farmoljon a játékos helyett.
- [ ] **Nincs ellenfélrejtett adat** – Csak a kliensnek szabályosan elküldött és biztonságosan értelmezett adat jelenjen meg.
- [ ] **Nincs kötelező account vagy telemetry** – Az alap Companion teljesen helyben is használható legyen.
- [ ] **Nincs automatikus megszégyenítés** – A DPS-adatok önfejlesztésre valók, nem játékosok zaklatására.

## 10. Saját Nexforge ötletek – amitől nem csak még egy meter lenne

- [ ] **Pull boncolás** – A fight végén három mondatban összefoglalja, mi döntötte el az eredményt.
- [ ] **„Miért lett rosszabb?” összehasonlítás** – Megmutatja, mitől maradt el az előző jobb próbától.
- [ ] **Saját ghost run** – A jelenlegi teljesítményedet élőben a saját legjobb azonos bossfightodhoz méri.
- [ ] **Konzisztencia pontszám** – Ne csak a rekordot, hanem az egymás utáni stabil teljesítményt is értékelje.
- [ ] **Személyes teljesítményprofil** – Felismeri a tipikus erősségeidet, hibáidat és fejlődési irányodat.
- [~] **Session összefoglaló** – A Combat Log fejlécében fightszámot, harcidőt, damage-et, healt és legjobb group DPS-t összegez.
- [ ] **Heti fejlődési jelentés** – Boss- és class-szinten mutatja, miben javultál vagy estél vissza.
- [ ] **Saját célok** – Például kevesebb halál, jobb buff uptime vagy stabilabb rotáció követése.
- [ ] **Okos rekord** – Csak valóban összehasonlítható boss-, fázis- és partyhelyzeteket vessen össze.
- [ ] **Mechanika ára** – Megmutatja, mennyi sebzés vagy idő veszett el egy konkrét mechanika miatt.
- [ ] **Elkerülhető halál becslése** – Elkülöníti a mechanikai hibát, healhiányt és várható one-shotot.
- [ ] **Üresjárat-elemzés** – Megkeresi, hol nem történt cast, damage vagy heal, és mi lehetett az oka.
- [ ] **Elveszett skillalkalmak** – Kiszámolja, egy fontos cooldown hányszor férhetett volna még bele a fightba.
- [ ] **Burst igazító** – Megmutatja, hogy a nagy cooldownok mennyire estek egybe a party buffjaival.
- [ ] **Rotációs ujjlenyomat** – Vizuálisan összeveti a saját castmintádat a jobb próbáiddal.
- [ ] **Adaptáció felismerése** – Jelzi, ha egy mechanika után egyre gyorsabban állsz vissza a rotációba.
- [ ] **Role Score** – DPS helyett class- és rolefüggő értékelés damage, heal, túlélés és mechanikák alapján.
- [ ] **Fair Play Score** – Nem jutalmazza a haszontalan pad damage-et vagy a mechanika szándékos kihagyását.
- [ ] **Party szinergiatérkép** – Megmutatja, mely buffok, debuffok és burst ablakok erősítették egymást.
- [ ] **Buffütközés-figyelő** – Jelzi a rosszul időzített vagy feleslegesen egymásra rakott party cooldownokat.
- [ ] **Tank védelmi timeline** – Beérkező nagy sebzések, mitigation és védelmi cooldownok egy sorban.
- [ ] **Healer pressure graph** – Megmutatja, mikor és miért nőtt meg hirtelen a gyógyítási terhelés.
- [ ] **Megmentett játékosok** – Kiemeli azokat a healeket vagy shieldeket, amelyek valószínűleg halált előztek meg.
- [ ] **Mechanika-visszajátszás** – Egyszerű 2D nézetben visszajátssza a boss castokat, halálokat és fontos eseményeket.
- [?] **Pozíciós hőtérkép** – Saját mozgás, veszélyes zónák és halálhelyek összesítése, ha van elég adat.
- [ ] **Boss tanulókártya** – Minden wipe után automatikusan elmenti az újonnan látott mechanikát.
- [ ] **Csapatjegyzet** – A raid leader rövid feladatokat rendelhet role-hoz vagy játékoshoz egy bosson belül.
- [ ] **Pull terv** – Mechanikák mellé előre felírható interrupt, defensive, burst és felelős játékos.
- [ ] **Taktika overlay próba** – Harc nélkül, időzítve lejátssza a DBM riasztásokat gyakorláshoz.
- [ ] **Marker intelligens csoportosítás** – Nagy zoomnál összevonja a közeli pontokat, közelről szétnyitja őket.
- [ ] **„Mit csináljak most?” panel** – A kiválasztott cél alapján a következő három hasznos teendőt mutatja.
- [ ] **Farmnapló** – Megjegyzi, hol, mennyi idő alatt és mennyi célzott alapanyagot szereztél.
- [ ] **Saját dropstatisztika** – A személyes lootból valós, helyi dropbecslést készít, félrevezető garancia nélkül.
- [ ] **Célalapú farmterv** – Egy kiválasztott itemhez lebontja a szükséges dropot, craftot és napi limiteket.
- [ ] **Okos útvonal újratervezés** – Ha valamit már felvettél vagy kihagytál, menet közben újrarendezi az útvonalat.
- [ ] **Patch-hatásjelentés** – Frissítés után megmondja, mely buildjeidet, timereidet és guide-jaidat érinti a változás.
- [ ] **Build pillanatkép fight mellé** – Később is pontosan látható, milyen felszereléssel és skillekkel ment a run.
- [ ] **Ismeretlen adat postaláda** – Az új opcode-okat, skilleket és mobokat egy kutatási listába gyűjti.
- [ ] **Parser confidence** – Minden kísérleti adatnál látszik, mennyire biztos a felismerés.
- [ ] **Patch break detector** – Frissítés után azonnal jelzi, ha megváltozott egy fontos packet vagy adatstruktúra.
- [ ] **Capture Health** – Egyetlen állapotjelző megmondja, hogy adapter, packetek, karakter és opcode-ok rendben vannak-e.
- [ ] **Öndiagnosztika** – Egy gombbal végigteszteli az Npcapot, adminjogot, overlayt, NFG-ket és mentési helyet.
- [ ] **Biztonságos tesztmód** – Mentett vagy generált packetmintából játék nélkül próbálható a teljes UI és parser.
- [ ] **Nexforge Lab** – Elkülönített fejlesztői felület új opcode, parser és bossmechanika kipróbálására.
- [ ] **Hibajelentés-csomag** – Személyes adatok nélkül összecsomagolja a verziót, hibát és szükséges diagnosztikát.
- [ ] **Lokális intelligens kereső** – Természetes mondatból keres: „melyik boss dob cleric healing geart?”
- [ ] **Személyes Companion feed** – Egy helyen mutatja a következő eventet, félbehagyott célt és friss rekordot.
- [ ] **Quiet Mode** – Fontos mechanikán kívül mindent elnémít, hogy ne legyen információs zaj.
- [ ] **Streamer Safe Mode** – Egy kapcsolóval rejt minden nevet, szervert, chatet és érzékeny azonosítót.
- [ ] **Nexforge Share Code** – Build, map route, boss pack vagy UI-layout egy rövid kóddal átadható.
- [ ] **Közösségi adatbizalom** – Megosztott marker vagy taktika mellett látszik a verzió, ellenőrzöttség és pontosság.
- [ ] **Offline-first működés** – Minden alapfunkció internet nélkül is használható, a sync csak opcionális extra.

## Ajánlott megvalósítási sorrend

1. Stabil capture, party/combat state és pontos damage/heal.
2. Tartós Combat Log, fight-váltás és részletes elemzés.
3. Valódi Boss Assistant és bossadat-csomagok.
4. Stabil Map Helper, rétegek és progress tracking.
5. Timerek, automatikus checklist és értesítések.
6. Karakter/build/adatbázis eszközök.
7. OBS, helyi web overlay és opcionális megosztás.
8. Ranglisták és közösségi funkciók csak kiforrott privacy mellett.

## Kutatási források és inspiráció

- [AION2 DPS Meter – fight history, breakdown, rotation és boss timerek](https://github.com/cyberbadger6969/aion2-dps-meter)
- [Aion DPS Meter – combat, map, event, network, loot és checklist funkciók](https://github.com/SkeeveAN/Aion-DPS-Meter)
- [A2Tools – combat history, OBS, publikus logok és pluginek](https://www.a2tools.app/)
- [A2Naki – boss mod, fight replay, PvP és ranglisták](https://a2naki.com/)
- [Shugo.GG desktop app bemutató – automatikus checklist, pet radar és replay](https://www.reddit.com/r/Aion2/comments/1x3werk/shugogg_app_is_now_in_open_beta_free_desktop/)
- [Aion2Data – adatbázis, map, planner, drop és kalkulátor ötletek](https://aion2data.com/)
- [Aion2.gg – kliensből frissített adatbázis és interaktív map](https://aion2.gg/database)
- [Aion2 Atlas – map rétegek, progress, útvonalak és értesítések](https://aion2atlas.app/map/)
- [InteractiveMap – route planning, progress és közösségi útvonalak](https://www.reddit.com/r/Aion2/comments/1wh1y5x/aion_2_interactive_map_database_full_rundown/)

Megjegyzés: a külső projektek funkciólistája inspiráció. Nem kell a kódjukat vagy assetjeiket átvenni; a Nexforge Companion saját implementációt és saját adatforrást használjon.
