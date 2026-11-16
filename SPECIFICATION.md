# Házi feladat specifikáció: E-book Olvasó

## 1. Cél és rövid leírás
Egy kliens-szerver architektúrájú (Blazor frontend + C# ASP.NET Core backend) webes alkalmazás, amely lehetővé teszi a felhasználók számára saját e-book fájljaik feltöltését, menedzselését és olvasását. Az alkalmazás nyilvántartja a felhasználók saját könyvtárát, automatikusan menti az olvasási pozíciót, és lehetőséget biztosít egyedi könyvjelzők elhelyezésére.

## 2. Szerepkörök
- **Látogató**: Csak a publikus kezdőoldalt (`/`), valamint a bejelentkezési (`/login`) és regisztrációs (`/register`) felületet éri el.
- **Bejelentkezett felhasználó**: Saját fiókkal rendelkezik. Hozzáfér a saját könyvtárához, új fájlokat tölthet fel, könyveket törölhet, olvashat (`/reader/{id}`), és könyvjelzőket hozhat létre.

## 3. Főbb forgatókönyvek
1. **Autentikáció**: A látogató regisztrál, majd bejelentkezik a rendszerbe.
2. **Könyvtár kezelés**: A felhasználó feltölt egy új fájlt, amely a feldolgozás után megjelenik a személyes listájában.
3. **Olvasás és progresszió**: A felhasználó megnyit egy könyvet. A rendszer betölti a legutolsó olvasási pozíciót. Lapozáskor az aktuális pozíció mentésre kerül a backenden.
4. **Könyvjelzőzés**: A felhasználó egy érdekes résznél könyvjelzőt helyez el, amit elnevezhet. Később a könyv adatlapjáról/menüjéből egy kattintással visszaugorhat erre a pontra.

## 4. Bizonytalanságok és azok feloldása
- **Kiterjesztések**: Az alkalmazás csak a 2 legnépszerűbb: **PDF (`.pdf`)** és **EPUB (`.epub`)** formátumokat támogatja, minden más kiterjesztést a szerver elutasít (`400 Bad Request`).
- **Felhasználók megkülönböztetése és privát könyvtárak védelme**: A rendszer felhasználónév és sózott PBKDF2-SHA256 jelszóhash párossal azonosítja a fiókokat, bejelentkezéskor pedig munkamenet-tokent (`Bearer`) állít ki. A szerver minden adatlekérési és módosítási kérésnél ellenőrzi az aktuális munkamenet jogosultságát, és csak a saját feltöltésekhez enged hozzáférést (`401 Unauthorized` / `403 Forbidden`).
- **Egyidejű olvasás több böngészőből/eszközről**: Az adatbázis mindig az időben legutoljára beérkező pozíciómentést tekinti érvényesnek (**Last Write Wins** elv UTC időbélyeg alapján).
- **Érvényes kiterjesztésű, de olvashatatlan vagy sérült fájl**: A backend feltöltéskor alapvető integritás-ellenőrzést végez (PDF esetén `%PDF-` fejléc, `%%EOF` lezárás és oldalszerkezet; EPUB esetén ZIP archívum épsége, `container.xml`/`.opf` és olvasható XHTML fejezetek), és hibás fájlszerkezet esetén elutasítja a mentést (`400 Bad Request`).

## 5. Elfogadási feltételek (Tesztelési esetek)
1. **(Normál)** A látogató egyedi felhasználónévvel és jelszóval sikeresen fiókot hoz létre. Sikeres bejelentkezés után a regisztrált felhasználó átirányításra kerül a főoldalra, ahol hiánytalanul megjelenik a korábban feltöltött könyveinek listája.
2. **(Normál)** A bejelentkezett felhasználó egy érvényes EPUB/PDF fájlt tölt fel, amely megjelenik a saját olvasmányainak listájában.
3. **(Hibás)** Ha a felhasználó rossz formátumú / túl nagy méretű / sérült fájlt töltene fel; a rendszer a kérést elutasítja, a felület hibaüzenetet mutat.
4. **(Normál)** Olvasás közben a felhasználó a "Könyvjelző létrehozása" gombra kattint. A könyvjelző sikeresen elmentésre kerül, és a könyv menüjéből kiválasztva a nézet pontosan a mentett tartalomra ugrik.
5. **(Normál)** A felhasználó megnyit egy feltöltött könyvet; a megjelenítő sikeresen betölti a fájl tartalmát az első oldalon. A felhasználó a könyvolvasóban a következő oldalra lapoz; a felület frissül, az új pozíciót pedig a rendszer elküldi a szervernek. Felhasználó bezárja a könyvet az `x` oldalon, majd később újra megnyitja; a rendszer automatikusan az `x` oldalra ugrik.
6. **(Hibás)** Egy felhasználó / látogató hibás adatokkal próbál bejelentkezni vagy manipulált hálózati kéréssel próbál meg lekérni egy másik felhasználóhoz tartozó dokumentumokat; a szerver jogosultsági hiba miatt elutasítja a kérést.
7. **(Határeset)** A felhasználó nem létező oldalra próbál navigálni, a rendszer az utolsó / legelső oldalt mutatja hibaüzenet nélkül.
8. **(Határeset)** A felhasználó duplikált könyvet próbál feltölteni vagy duplikált könyvjelzőt elhelyezni, a rendszer közli a hibát és tovább működik.
