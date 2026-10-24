# AION 2 Companion – feltárt hibák és kockázatok

Állapot: 2026-10-22

Ez egy első, statikus projektáttekintés eredménye. A forráskód nem lett javítva. A `dotnet build --no-restore` sikeres volt: **0 warning, 0 error**. Emiatt az alábbiak többsége futásidejű, integrációs, protokoll-értelmezési vagy karbantarthatósági probléma.

Jelölések:

- **Biztos:** a jelenlegi kódból közvetlenül látható.
- **Valószínű:** erős gyanú, de valódi capture-rel/UI-teszttel kell megerősíteni.
- **Protokollkockázat:** a hálózati formátum még részben reverse engineering alatt áll.

## Kritikus / hibás eredményt adhat

1. **A heal felismerése jelenleg hibás.** A `MainWindowViewModel.ApplyDamageHit` minden olyan találatot healnek tekint, amelynek célpontja egy party-tag. Emiatt az ellenfél party-tagra mért sebzése gyógyításként számolódhat. **Biztos.**

2. **A saját karakterre érkező heal nincs jól kezelve.** A saját karakter nincs benne a `PartyMembers` listában, ezért a rá érkező gyógyítás sebzésként vagy eldobott eseményként végezheti. A saját self-heal `Actor == Target` miatt kieshet. **Biztos.**

3. **A Death opcode nem zárja le a mob harcát.** A Death ág meghívja a `CombatEndCheck` metódust, de az nem kezel Death opcode-ot, és a mobot sem veszi ki a `_MobsInCombat` halmazból. A komment és a tényleges működés eltér. **Biztos.**

4. **A LeaveCombat eseményt a dispatcher nem küldi a combat-end feldolgozásra.** A `CombatEndCheck` ismeri, de az `OnFrameDecoded` csak HP és Disengage esetén hívja közvetlenül. **Biztos.**

5. **Megoldva: megszűnt a registry és az enum közötti endian-eltérés.** A wire-értékek egyetlen forrása most a titkosított `AION2_Opcodes.nfg`, ezért a név, ismertségi állapot és parser-dispatch ugyanazt az adatot használja. **Megoldva 2026-10-24.**

6. **Party leave/kick esetén a party nem törlődik.** Az esemény csak logolva van, majd a metódus visszatér. A név, tagok, ID-k és classadatok beragadhatnak. **Biztos.**

7. **A summon- és partyállapot több életcikluson át beragadhat.** A `_summonOwners`, `_pendingSummons`, `_pendingDamageByActor` és több party/combat állapot nincs következetesen törölve új karakter, új kapcsolat, capture-stop vagy partyváltás esetén. **Biztos.**

8. **Régi summonsebzés átcsúszhat egy új fightba.** A pufferelt találatoknak van időbélyegük, de nincs lejárati takarításuk; késői owner-felismerés visszadátumozhatja vagy összekeverheti a harcot. **Valószínű.**

9. **A direkt találatok felső korlátja csak 100 999.** A `DamageScanner` ennél nagyobb Hit/DoT értéket eldob. Nagy sebzésnél a meter alulmérhet. A külön `DotFrameParser` ettől eltérő, 99 999 999-es limitet használ. **Biztos / protokollkockázat.**

10. **A találatok deduplikációja nincs használva.** A `Deduplicate` metódus létezik, de a scanner nem hívja. Beágyazott vagy többször megtalált rekord duplán számolódhat. **Biztos / protokollkockázat.**

11. **A Characters wrapper teljes payloadja offsetenként scan-elődik.** Erős validáció ellenére bináris zajból is keletkezhet téves Hit találat. **Protokollkockázat.**

12. **A party-tag skill-validációja túl durva fallbacket használ.** Ismeretlen skillnél csak a decimális ID első két számjegye kerül összevetésre a class prefixével. Ez elfogadhat téves skilleket és eldobhat legitim közös/summon skilleket. **Biztos / protokollkockázat.**

13. **Az Assassin adatbázis neve el van írva.** A `GetSkillDatabase` az `Assasin` szöveget várja, miközben az NFG-ben `Assassin` szerepel. Az exact skill lookup ezért Assassin esetén nem fut, csak a prefix-fallback. **Biztos.**

14. **A Fighter class nincs teljesen támogatva.** A class NFG tartalmaz Fightert, de nincs hozzá skill NFG, loader-ág vagy `GetSkillDatabase` ág. A hivatkozott `fighter.png` sincs a class assetek között. **Biztos.**

15. **A skillnév fallback téves skillt választhat.** Ha nincs exact ID, a `FindSkill` az első négy decimális karakter alapján az első találatot adja vissza. Több azonos prefixű skillnél hibás név/ikon jelenhet meg. **Biztos / adatfüggő.**

16. **A mob halálának és HP-jának értelmezése még bizonytalan.** A HP parser a payload végétől számított fix offseten csak 16 bites HP-t olvas. Nagy HP vagy eltérő rekordforma hibás combat-endet okozhat. **Protokollkockázat.**

17. **A CombatEnd parser legalább 6 bájtot követel, miközben csak egy varint ID-t olvas.** Rövidebb, de érvényes frame eldobódhat. **Biztos / protokollkockázat.**

18. **A Spawn feldolgozás túl tág.** A Summon parser minden támogatott, pontosan 102 bájtos payloadot mobként kezel, továbbá minden Spawn opcode korán mobággá válik. Ez summon rekordokat rejthet el vagy más frame-et mobnak nézhet. **Biztos / protokollkockázat.**

19. **A Summon parser dobhat kezeletlen kivételt.** Bizonyos ágak `PacketReader` műveletei nincsenek try/catch-ben; hibás, de megfelelő hosszú frame `FormatException`-t okozhat. **Biztos.**

20. **Egyetlen summon-konfliktus leállítja a teljes linklista feldolgozását.** A `TryLinkSummons` egyik konfliktuságában `return` van, így a frame további, esetleg helyes linkjei is kimaradnak. **Biztos.**

21. **A pending summon lista nincs méret- vagy időkorláttal takarítva.** Hosszú session alatt fals jelöltek felhalmozódhatnak. **Biztos.**

22. **A jelenlegi mobnév beragadhat az előző harcból.** `_currentMobName` nincs konzisztensen nullázva fightváltáskor, és kezdetben null lehet, miközben az ellenőrzés üres stringet vizsgál. **Biztos.**

23. **Élő snapshotban a mob maximum HP mindig 0.** Emiatt a játékosrészletek „mob HP százalék” értéke mindig `–`. **Biztos.**

24. **Nincs fight history.** Új sebzéses fight indulásakor `_damageByPlayer.Clear()` törli az előző harc eredményét. A TODO-ban szereplő visszanézés nincs implementálva. **Biztos.**

## Capture és wire réteg

25. **A capture minden TCP és UDP forgalmat figyel.** A BPF filter csak `tcp or udp`, nincs játékfolyamat-, szerver-IP- vagy portszűrés. Ez felesleges terhelés és adatvédelmi kockázat. **Biztos.**

26. **Az UDP csomagokat begyűjti, de a frame-dekóder nem dolgozza fel.** Ettől még endpoint/probe számlálás és UI-dispatch történik. **Biztos.**

27. **Minden nyers csomaghoz UI-thread művelet kerül sorba.** Nagy hálózati forgalomnál a `Dispatcher.UIThread.Post` eláraszthatja az UI queue-t, miközben csak egy számláló nő. **Biztos / terhelésfüggő.**

28. **A capture callback minden kivételt csendben elnyel.** Hibás csomag mellett valódi programhiba, decoderhiba vagy handlerhiba is láthatatlan marad. **Biztos.**

29. **A játék szerverportja több helyen hardcode-olt `13328`.** Másik régió, szerver vagy port esetén a party és az irányfelismerés leállhat. **Biztos / környezetfüggő.**

30. **A `WireFrameFlags.FromServer/FromClient` sosem kerül beállításra.** Az exportált flag mező nem tartalmaz valódi irányt, pedig a modell támogatná. **Biztos.**

31. **Az ismeretlen opcode-ok egy része soha nem jut el az Unknown számlálóig.** A frame plausibility ellenőrzés csak registry-találatot vagy előre engedélyezett opcode-családot fogad el. Új családok frame-jei dekódolás előtt elvesznek. **Biztos.**

32. **A decoder `trusted` paramétere nincs használva.** A kód azt sugallja, hogy lenne eltérő megbízhatósági mód, de ténylegesen nincs. **Biztos.**

33. **A frame buffer korlátja extrém nagy egyedi inputnál megkerülhető.** Ha egyetlen `Feed` adatblokk nagyobb a limitnél és a régi buffer üres, a törlési képlet nem vágja le az új adatot. Normál Ethernet csomagnál ritka. **Biztos / alacsony kockázat.**

34. **A decoded frame lista telítődés után drága.** Minden új frame `RemoveAt(0)` művelettel tol el legfeljebb 10 000 elemet. Hosszú, aktív capture-nél folyamatos többletterhelés lehet. **Biztos.**

35. **Az endpoint tracker nem ürül és nem limitált.** Sok különböző endpoint esetén a dictionary a session végéig nő. **Biztos.**

36. **Az endpoint tracker IPv6-ot eldob, miközben a capture és a TCP endpoint parser részben támogatja.** **Biztos.**

37. **A capture nincs automatikusan leállítva/dispose-olva az ablak bezárásakor.** A ViewModel nem `IDisposable`, és nincs window-close cleanup bekötve. **Biztos.**

38. **A capture indulása nincs startup szinten levédve.** A hálózati adapterlista/Npcap elérési hibája a `MainWindowViewModel` konstruktorában történik; ezen a részen nincs felhasználóbarát fallback. **Valószínű / környezetfüggő.**

39. **Nincs Npcap/admin jogosultság dokumentáció vagy ellenőrzés.** A promiscuous capture emiatt telepítéstől és jogosultságtól függően már induláskor hibázhat. **Biztos projekt-hiányosság.**

40. **A frame időpontja nem feltétlenül az első TCP-szegmens ideje.** Több szegmensből összeálló frame a legutóbbi feed idejét kaphatja, ami a timeline pontosságát ronthatja. **Valószínű.**

## Party, karakter és állapotkezelés

41. **A `_SelfId == null` ellenőrzés értelmetlen.** `_SelfId` nem nullable `uint`, ezért ez nem védi ki a még fel nem ismert karaktert; a tényleges üres érték 0. **Biztos.**

42. **Az azonos self ID-val érkező későbbi karakterfrissítés teljesen kimarad.** A `SearchSelf` azonnal visszatér, így hiányos első class/név/gear adat nem tud később javulni. **Biztos.**

43. **A party teljes frissítése eldobódik, ha a party neve nem olvasható.** Akkor is változatlan marad a roster, ha a tagnevek egyébként sikeresen felismerhetők. **Biztos.**

44. **A party-név és tagparser fix offsetes/heurisztikus.** Bináris payloadból olvasható UTF-8 szöveg tévesen játékosnévként bekerülhet, vagy valódi név kimaradhat. **Protokollkockázat.**

45. **A party class felismerése a név utáni 4 bájtos jelölteket próbálgatja.** Véletlen kis szám érvényes job ID-nak látszhat, így rossz class kerülhet a taghoz. **Protokollkockázat.**

46. **A party-tag hozzáadás case-sensitive, más részek case-insensitive összehasonlítást használnak.** Eltérő kis-/nagybetűzés duplikált vagy fel nem oldott tagot okozhat. **Biztos.**

47. **A `GetOwnerName` nullreferenciát okozhat.** A `FirstOrDefault(...).Name` a null eredményt dereferálja a `?? "?"` előtt. **Biztos, ha inkonzisztens owner ID érkezik.**

48. **Az `AddIDSToPartyMembers` tartalmaz egy üres `foreach` ciklust.** Funkciója nincs; valószínű félbehagyott debugkód. **Biztos.**

49. **A party partial update soha nem távolít el tagot.** Ez szándékos óvatosság, de leave/kick reset hiányával együtt beragadt rosterhez vezet. **Biztos.**

50. **A saját és party skill-owner feloldás nem egységes.** A class-validáció csak közvetlen party actorra fut; selfre és már feloldott summonra nem ugyanazok a szabályok vonatkoznak. **Biztos.**

## UI és ViewModel bekötések

51. **A kezdő Dashboard DataContextje valószínűleg null marad.** A `MainWindow` konstruktora a `DataContext` beállítása előtt hívja a `ShowPage` metódust, amely explicit a jelenlegi null értéket adja a lapnak. Navigálás után már jó lehet. **Valószínű, Avalonia runtime-teszttel ellenőrizendő.**

52. **A Capture Monitor nem létező `PartyParseStatus` propertyre bindol.** A property csak a demo ViewModelben van, az éles `MainWindowViewModel`-ben nincs. Mivel compiled binding ki van kapcsolva, buildhiba helyett üres UI várható. **Biztos.**

53. **A Capture Monitor rossz mezőre bindolja a party-tag nevét.** A template `Item2`-t kér, de az éles elem `PartyMemberEntry`, amelynek `Name` propertyje van. **Biztos.**

54. **A Combat Analytics nem létező `CombatRows` propertyre bindol.** Ez csak a demo ViewModelben található, ezért a rangsor üres marad. **Biztos.**

55. **A Combat Analytics felső számai fix mintaértékek.** Nem kapcsolódnak az élő meter snapshothoz. **Biztos.**

56. **A Capture Monitor eseménynaplója üres marad.** Az `AddLogEntry` metódust semmi nem hívja. **Biztos.**

57. **A `RecentFindings` lista éles módban nem frissül.** Létrejön, de nincs bele író kód. **Biztos.**

58. **Az `Endpoints` UI-collection nincs feltöltve.** Az `EndpointTracker` gyűjt, de snapshotja sosem kerül az observable collectionbe. **Biztos.**

59. **Csak a Dashboardról nyitott DPS overlay kap élő frissítést.** Az Analytics és Settings új, demo snapshotos `DpsOverlayWindow` példányt hoz létre, amely nincs a ViewModel statikus `_dpsOverlay` mezőjéhez kötve. **Biztos.**

60. **A DPS overlay kezdetben demo adatot mutat.** Az élő snapshot csak a következő timer/damage push után cseréli le, és csak a globális példányon. **Biztos.**

61. **A DPS/PlayerDetails/Timeline ablakok nem include-olják a projekt Theme stílusát.** Mégis `ui`, `quiet`, `primary` classokat használnak. A Theme csak a MainWindow lokális stílusa, ezért az overlay gombjai eltérően jelenhetnek meg. A DBM overlay külön include-olja. **Biztos.**

62. **A Settings checkboxok nem csinálnak semmit.** Nincs ViewModel binding vagy eseménykezelő; csak lokális vizuális állapotuk van. **Biztos.**

63. **A főablak és Dashboard státuszai részben statikusak.** A címsor „Készenlét”, a harc/DPS/boss állapot és az automatikus mérés feliratai nem követik az éles capture/combat állapotot. **Biztos.**

64. **A navigáció minden alkalommal új View példányt hoz létre.** A lapok lokális állapota elveszik, például checkboxok és egyéb session UI-beállítások. **Biztos.**

65. **A Dashboard kártyasorrend csak memóriában él.** Újranyitás/újraindítás után elveszik; ezt a UI is jelzi. **Biztos.**

66. **A Boss Assistant és DBM overlay csak minta/statikus tartalom.** Nincs parser-, encounter- vagy timer-adat bekötve. **Biztos.**

67. **A Guides oldal statikus példa.** Nem az NFG class/skill adatbázisból épül. **Biztos.**

68. **Az automatikus capture nincs implementálva.** A felület szövege szerint végül automatikus lesz, jelenleg csak kézi Start gombbal indul. **Biztos.**

69. **A kompatibilitási `UpdateDps` útvonalon az overlay összesítői hibásak.** Totálokkal, de események nélkül épít snapshotot; a csoport idősor és share 0/üres marad. **Biztos.**

70. **A nagyon rövid fightok rate-je torzul.** Az overlay legalább 1 másodperccel oszt, míg a snapshot valódi durationje lehet kisebb. **Biztos.**

71. **Hibás class szín összeomlaszthatja az overlay refresh-t.** A `Brush.Parse` nincs védve; egy rossz NFG color string kivételt dobhat. **Biztos / adatfüggő.**

72. **Az export parancsoknál nincs hibakezelés.** Desktop írási hiba, jogosultság vagy hibás útvonal UI-thread kivételt okozhat. **Biztos / környezetfüggő.**

## Adatok és biztonság

73. **Megoldva: az NFG v2 már titkosított.** AES-256-GCM, fájlonkénti HKDF-só és nonce védi; az NXFG v1 csak migrációs kompatibilitásként maradt olvasható. A futtatható alkalmazásból történő célzott kulcskinyerés továbbra sem zárható ki. **Megoldva 2026-10-22.**

74. **Megoldva az NFG v2-ben: a SHA-256 helyett hitelesített titkosítás védi a payloadot és a fejlécet.** Az örökölt v1 fájlokra az eredeti megállapítás továbbra is igaz, ezért a Studio konvertálást ajánl fel. **Megoldva 2026-10-22.**

75. **Az alkalmazás érzékeny opcode-okat is eltárolhat/exportálhat.** A registry maga jelzi, hogy Login/Account payload érzékeny lehet, az export mégis nyers hexet ír a Desktopra redaction és figyelmeztetés nélkül. **Biztos.**

76. **A ForgeData betöltési hibák csak debug logba kerülnek.** Debug nélkül az adatbázis null maradhat úgy, hogy a felhasználó nem kap egyértelmű hibajelzést. **Biztos.**

77. **A távoli skillikon URL-ek nincsenek támogatva.** A loader HTTPS esetén szándékosan nullt ad vissza. Ha a lokális ikon hiányzik, nem lesz fallback. **Biztos.**

78. **A skillikon választásnál az üres lokális path elfedheti az URL-t.** A `skill?.Icon ?? skill?.IconUrl` csak nullt vizsgál, az üres stringet nem. **Biztos / adatfüggő.**

79. **A Fighter ikon hivatkozása hibás.** Az NFG `Data/Assets/Classes/fighter.png` fájlt kér, de a fájl nem található. **Biztos.**

80. **Az adatfájlok minősége vegyesnek tűnik.** Több class első skillje közös placeholder ikonra mutat, az ID-k formátuma sem teljesen egységes. Ezt adatvalidációval/capture-rel kell ellenőrizni. **Adatkockázat.**

## Kód- és repository-karbantartás

81. **Nincs automata tesztprojekt.** A TCP reassembly, frame/LZ4 decoder, varint, parserek, combat state és meter számítás regressziói nem védettek. **Biztos.**

82. **Nincs README vagy telepítési/futtatási dokumentáció.** Nincs leírva a .NET/Npcap követelmény, jogosultság, debug flag, capture használat vagy export adatvédelme. **Biztos.**

83. **A repository még commit nélküli.** Nincs stabil baseline, amihez a későbbi változásokat lehetne hasonlítani. **Biztos.**

84. **A `.gitignore` hiányos.** `.idea`, `*.DotSettings.user`, Python build/dist, generált CSV/JSONL, zip snapshotok és több tool artifact nincs kizárva. **Biztos.**

85. **Generált binárisok és build artifactok vannak a projektben.** A `Tools/build`, `Tools/dist`, `Tools/dist1` nagy, platformfüggő és nehezen review-zható fájlokat tartalmaz. **Biztos.**

86. **A `Styles.zip` és `ViewModels.zip` a forrás részleges másolatai.** Könnyen elavulnak, és nem egyértelmű, melyik az igazságforrás. **Biztos.**

87. **Megoldva: a demo ViewModel kikerült a projektből.** Az éles `LogEntry` külön fájlba került, a sablonadatok és demo szövegek már nem fordulnak az alkalmazással. **Megoldva 2026-10-24.**

88. **A `ViewModels/LogEntry.cs` gyakorlatilag üres.** A fájlnév alapján itt várható modell valójában a demo fájlban van. **Biztos.**

89. **Több örökölt/duplikált típus használaton kívüli.** Például `Frame`/`FrameFlags` a `WireFrame` mellett, `OpcodeProbe`, `PartyResetParser` és a külön `DotFrameParser`. Ez növeli a félreértés esélyét. **Biztos.**

90. **A `MainWindowViewModel` túl sok felelősséget kezel.** Capture lifecycle, protokoll-dispatch, party, summon, combat, adatbázis, export és overlayfrissítés egy több mint 2000 soros osztályban van. Ez erősen növeli a regressziós kockázatot. **Biztos.**

91. **Több állapot publikus/statikus vagy közvetlenül mutálható.** Például a statikus `_dpsOverlay`, a ForgeDataLoader publikus mezői és a mutable party modellek szoros csatolást okoznak. **Biztos.**

92. **A névadás és formázás inkonzisztens.** Példák: `_SelfId`, `_KnowMobs`, `_MobsInCombat`, `isSelfOwner`, `Assasin`, `elementalistDatabse`, `TheFuck`, egy soros demo osztályok és duplikált `using` sorok. **Biztos.**

93. **A forrásban nagy, aktuális TODO blokk van konkrét feladatkövetés helyett.** Több ismert hiba és terv csak kommentben szerepel, státusz/teszt nélkül. **Biztos.**

94. **A debug konzolban duplikált `Categories.Remove("debug")` hívás van.** Funkcionálisan ártalmatlan, de félbehagyott tisztítás jele. **Biztos.**

95. **A csproj redundánsan veszi fel az ikonresource-okat.** A teljes `Data/Assets/**/*` mellett külön `Data/Assets/Icons/**/*` include is szerepel; Windows alatt ez átfedő lehet. A build jelenleg nem jelez hibát. **Biztos / alacsony kockázat.**

## Rövid prioritási sorrend későbbre

1. Heal/damage elkülönítés és combat-end események.
2. Party/summon/session állapot korrekt resetje.
3. Capture szűkítése és érzékeny exportok kezelése.
4. Éles UI-bindingok és az egyetlen élő overlay útvonal rendezése.
5. Parser fixture-ök és automata tesztek létrehozása.
6. Repository baseline, `.gitignore` és generált artifactok rendezése.

