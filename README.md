# ParkingSystem – מערכת להקצאת מקומות חניה

שרת **ASP.NET Core Web API** (‏.NET 8) שבו עובדים מזמינים מקום חניה ליום מסוים.
זהו ה-repository של **השרת**. לקוח ה-React נמצא ב-repository נפרד (`parking-system-client`, ראו [הגשה](#הגשה-ושני-ה-repositories)), והפריסה ל-Render מתוארת ב-[פריסה ל-Render](#פריסה-ל-render). מצב מלא ב-[מצב הפרויקט](#מצב-הפרויקט).

## תוכן עניינים
1. [המשאב המוגבל והתחרות עליו](#המשאב-המוגבל-והתחרות-עליו)
2. [ארכיטקטורה](#ארכיטקטורה)
3. [דרישות מקדימות](#דרישות-מקדימות)
4. [הרצה מקומית](#הרצה-מקומית)
5. [משתמשי דמו](#משתמשי-דמו)
6. [Swagger](#swagger)
7. [Endpoints והרשאות](#endpoints-והרשאות)
8. [כללים עסקיים](#כללים-עסקיים)
9. [הרצת בדיקות](#הרצת-בדיקות)
10. [קונפיגורציה וסודות](#קונפיגורציה-וסודות)
11. [לוגים](#לוגים)
12. [Migrations](#migrations)
13. [פריסה ל-Render](#פריסה-ל-render)
14. [הגשה ושני ה-repositories](#הגשה-ושני-ה-repositories)
15. [החלטות והסתייגויות ידועות](#החלטות-והסתייגויות-ידועות)
16. [מצב הפרויקט](#מצב-הפרויקט)

---

## המשאב המוגבל והתחרות עליו

**המשאב המוגבל:** מקום חניה אחד ביום נתון. כל מקום יכול להיות מוזמן על ידי עובד אחד בלבד בכל יום.

**איפה התחרות:** שני עובדים לוחצים "הזמן" על אותו מקום ולאותו יום באותו רגע. שניהם קוראים שהמקום פנוי, ושניהם מנסים לשמור.

**איך המערכת מגיבה:** רק אחד מהם מצליח (`201`). השני מקבל **`409 Conflict`** עם הודעה ברורה ("המקום כבר תפוס בתאריך זה"), וההזמנה שלו לא נשמרת. ההתנגשות נרשמת בלוג ברמת `Warning`.

המנגנון הוא **optimistic concurrency** ויש לו שלוש שכבות הגנה, שכל אחת מספיקה לבדה:

| שכבה | איפה | מה היא עושה |
|---|---|---|
| 1. בדיקה עסקית | `ReservationService` | בודקת בתוך טרנזקציה אם המקום פנוי ונותנת הודעה ברורה במקרה הרגיל. לא מספיקה לבדה כששתי בקשות מגיעות יחד |
| 2. concurrency token | `ParkingSpot.Version`, ממופה לעמודת המערכת `xmin` של PostgreSQL | כל הזמנה מעדכנת גם את המקום (`LastReservedAtUtc`), ולכן ה-`UPDATE` כולל `WHERE xmin = <הערך שנקרא>`. מי ששומר שני מקבל `DbUpdateConcurrencyException`, שה-`Repository` מתרגם ל-`ReservationConflictException` ול-409 |
| 3. אינדקס ייחודי חלקי | בסיס הנתונים | `UNIQUE (ParkingSpotId, Date) WHERE Status = 'Active'` (וכן `(UserId, Date)`). גם אם משהו עוקף את שתי השכבות הראשונות, בסיס הנתונים מסרב לשורה שנייה. הזמנה מבוטלת אינה חוסמת הזמנה חדשה |

> **מחיר מוכר:** ה-token שייך למקום עצמו, ולכן שתי הזמנות לתאריכים **שונים** על אותו מקום באותו רגע בדיוק עלולות להתנגש, והשנייה תקבל 409. המשתמש פשוט לוחץ שוב.

**הוכחה:** בדיקה עם שני `DbContext` נפרדים (`ReservationConcurrencyTests`) מראה שהראשון שומר והשני מקבל `DbUpdateConcurrencyException`. בדיקות נוספות מריצות 8 בקשות מקבילות (דרך ה-service וגם דרך HTTP), ובכל אחת **בדיוק אחת מצליחה והשאר מקבלות 409**. ראו [הרצת בדיקות](#הרצת-בדיקות).

---

## ארכיטקטורה

ה-solution בנוי מארבעה פרויקטים ופרויקט בדיקות:

| פרויקט | אחריות | מכיל |
|---|---|---|
| `ParkingSystem.Core` | הגדרות בלבד. **לא מפנה לשום פרויקט או חבילה** | ישויות, enums, DTOs, ממשקים של repositories ושל services, `Result`, חריגות, `Options` |
| `ParkingSystem.Data` | גישה לנתונים | `AppDbContext`, הגדרות Fluent API, repositories, migrations, Seed (EF Core + Npgsql) |
| `ParkingSystem.Service` | לוגיקה עסקית | `AuthService`, `ParkingSpotService`, `ReservationService`, `FeatureService`, יצירת JWT, הצפנת סיסמאות (BCrypt), הגדרות AutoMapper |
| `ParkingSystem.API` | הצגה ו-HTTP | Controllers, Middleware, רישום DI, Swagger, `nlog.config`, `Program.cs` |
| `ParkingSystem.Tests` | בדיקות | xUnit + Moq, בדיקות אינטגרציה מול PostgreSQL |

**כיוון התלויות** (כל חץ הוא "מפנה אל"):

```
API ──► Service ──► Core ◄── Data
 │                    ▲
 └────────────────────┴── (API מפנה גם ל-Core ול-Data, ל-Data רק לצורך רישום ה-DI והפעלת migrations ו-Seed בעלייה)
```

- ה-Controllers אינם מכירים `DbContext` או repository: הם עובדים רק מול services דרך ממשקים מ-`Core`.
- ישויות אינן יוצאות ואינן נכנסות דרך ה-API. כל הבקשות והתשובות הן DTOs.
- **Dependency Injection:** כל הרישום בקובץ אחד, `ParkingSystem.API/Extensions/ServiceCollectionExtensions.cs`:
  - `DbContext`, ה-repositories והשירותים: **Scoped** (אינסטנס לכל בקשה).
  - מצפין הסיסמאות, יוצר ה-token והשעון: **Singleton** (חסרי מצב ואינם תלויים ב-`DbContext`).
  - **למה לא להפוך שירות ל-Singleton:** Singleton נוצר פעם אחת ונשאר חי כל חיי האפליקציה. אם הוא מחזיק בתוכו שירות Scoped (כמו DbContext), ה-DbContext נשאר חי איתו לאורך כל הבקשות: כמה בקשות משתמשות באותו DbContext במקביל (הוא אינו בטוח ל-threads), הנתונים במטמון שלו מתיישנים, וגם ה-concurrency token נשבר. לכן רק רכיבים חסרי מצב שאינם תלויים ב-DbContext רשומים כ-Singleton.
- **Middleware**, בסדר הזה: כותרות proxy (`X-Forwarded-*`) ← טיפול גלובלי בשגיאות ← `CorrelationId` ← לוג בקשות ← Swagger ← הפניה ל-HTTPS (בפיתוח בלבד) ← CORS ← הגבלת קצב להרשמה והתחברות ← Authentication ← Authorization ← Controllers. בנוסף, `GET /healthz` לבדיקת חיים בענן.

מבנה התיקיות:

```
project_dotnet/
├── ParkingSystem.sln
├── docker-compose.yml          # PostgreSQL 16 מקומי
├── Dockerfile, .dockerignore   # תמונת Docker של השרת (נדרשת ל-Render)
├── render.yaml                 # Render Blueprint: שירות השרת ובסיס הנתונים
├── ParkingSystem.Core/         # Entities, Enums, DTOs, Interfaces, Common, Options, Exceptions
├── ParkingSystem.Data/         # Context, Configurations, Repositories, Migrations, Seed
├── ParkingSystem.Service/      # Services, Security, Mapping
├── ParkingSystem.API/          # Controllers, Middleware, Extensions, Swagger, nlog.config
└── ParkingSystem.Tests/        # Services, Security, Mapping, Api, Integration, Helpers
```

---

## דרישות מקדימות

- **.NET SDK 8** ומעלה (הפרויקטים מכוונים ל-`net8.0`).
- **Docker Desktop** להרצת PostgreSQL מקומי (או PostgreSQL 16 שמותקן אצלכם).
- **Git**.
- לא חובה: הכלי `dotnet-ef`, רק אם רוצים ליצור migration חדש (ראו [Migrations](#migrations)).

---

## הרצה מקומית

כל הפקודות מורצות מתיקיית השורש (איפה ש-`ParkingSystem.sln`).

### 1. הרמת בסיס הנתונים
```bash
docker compose up -d
```
זה מרים את PostgreSQL 16 בקונטיינר `parking-system-db`, בבסיס נתונים `parking_system`, על `127.0.0.1:5432`.

> **חשוב:** הקונטיינר מוגדר **בלי סיסמה** (`POSTGRES_HOST_AUTH_METHOD: trust`) והפורט פתוח **למחשב המקומי בלבד**. כך אין שום סיסמה כתובה ב-repository. זה מיועד לפיתוח מקומי בלבד.

### 2. הגדרת סודות (User Secrets)
מפתח ה-JWT וסיסמאות משתמשי הדמו **אינם נשמרים בקוד או ב-Git**, ולכן חייבים להגדיר אותם פעם אחת בכל מחשב:

```bash
# מפתח חתימה ל-JWT: לפחות 32 תווים. דוגמה ליצירת מפתח אקראי ב-PowerShell:
#   $b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
dotnet user-secrets set "Jwt:SecretKey" "<המפתח שנוצר>" --project ParkingSystem.API

# סיסמאות משתמשי הדמו (ראו "משתמשי דמו")
dotnet user-secrets set "SeedUsers:AdminPassword" "Admin-Demo-2026!" --project ParkingSystem.API
dotnet user-secrets set "SeedUsers:EmployeePassword" "Employee-Demo-2026!" --project ParkingSystem.API
```

- **בלי `Jwt:SecretKey` השרת מסרב לעלות**, עם הודעה ברורה. זה מכוון.
- בלי סיסמאות הדמו השרת עולה, אבל משתמשי הדמו לא נוצרים (נרשמת אזהרה).
- מחרוזת החיבור לבסיס הנתונים נמצאת ב-`ParkingSystem.API/appsettings.Development.json`: `Host=localhost;Port=5432;Database=parking_system;Username=postgres`. כדי להשתמש בבסיס נתונים אחר, הגדירו `ConnectionStrings:Default` באותה דרך (`dotnet user-secrets set`).

### 3. הפעלת השרת
```bash
dotnet run --project ParkingSystem.API
```
בעלייה השרת **מחיל לבד את ה-migrations ומזין נתוני התחלה**: 3 מאפיינים, 20 מקומות חניה (4 קומות) ושני משתמשי הדמו. אין צורך בשום פעולה ידנית בבסיס הנתונים.

הכתובת: **http://localhost:5258** (פרופיל `http`), ובפרופיל `https` גם `https://localhost:7172`.

### 4. הפעלת הלקוח (React)
הלקוח נמצא ב-repository הנפרד `parking-system-client` (תיקייה לצד תיקיית השרת). דורש **Node.js 22.12 ומעלה**:
```bash
cd ../parking-system-client
npm install
npm run dev
```
הכתובת: **http://localhost:5173**. הכתובות והפורטים אומתו בהרצה בפועל: שרת `5258`, לקוח `5173`, בדיקת תקינות `http://localhost:5258/healthz`.

### הפעלה בפקודה אחת (Windows)
אחרי שלב 2 (סודות) אפשר להפעיל הכול יחד: בסיס נתונים, שרת ולקוח (כל אחד בחלון נפרד):
```powershell
powershell -ExecutionPolicy Bypass -File scripts\start-local.ps1
```
הסקריפט לא מכיל סיסמאות. אם `Jwt:SecretKey` חסר הוא יוצר מפתח אקראי ושומר אותו ב-User Secrets. סיסמאות הדמו נשארות באחריותכם (שלב 2).

### פתרון תקלות נפוצות
| תקלה | פתרון |
|---|---|
| השרת לא עולה ומתלונן על `Jwt:SecretKey` | להריץ את פקודות `user-secrets set` משלב 2 |
| התחברות כמשתמש דמו נכשלת | הסיסמאות לא הוגדרו לפני העלייה הראשונה. להגדיר אותן ולאפס את בסיס הנתונים (הסעיף הבא) |
| שגיאת חיבור ל-PostgreSQL | לוודא ש-Docker Desktop פועל ושהקונטיינר `parking-system-db` במצב `healthy` (`docker ps`) |
| הלקוח מציג שגיאת רשת או CORS | לוודא שהשרת רץ על `5258` ושהלקוח נפתח מ-`http://localhost:5173` בדיוק |
| הפורט תפוס | לסגור את התהליך שמחזיק בו (`netstat -ano`) או לשנות פורט ב-`launchSettings.json` |
| `npm run dev` נכשל | לבדוק `node -v` (נדרש 22.12+) ולהריץ `npm install` |

### איפוס בסיס הנתונים המקומי
```bash
docker compose down -v      # מוחק את הקונטיינר ואת הנתונים
docker compose up -d
```
(חשוב לדעת: משתמשי הדמו נוצרים רק כשהטבלה ריקה מהם. אם שינית סיסמה ב-User Secrets אחרי שהמשתמשים כבר נוצרו, צריך לאפס.)

---

## משתמשי דמו

נוצרים אוטומטית בעלייה הראשונה, אם הסיסמאות הוגדרו (שלב 2 למעלה):

| תפקיד | אימייל | סיסמה (לפי הפקודות שלמעלה) |
|---|---|---|
| **Admin** | `admin@parking.local` | `Admin-Demo-2026!` |
| **Employee** | `employee@parking.local` | `Employee-Demo-2026!` |

אלו סיסמאות **דמו בלבד**, ואין להשתמש בהן במקום אחר. הן אינן נמצאות בקובצי ההגדרות, רק בפקודות ההגדרה כאן. אפשר לבחור סיסמאות אחרות בפקודות `user-secrets set`.

התחברות: `POST /api/tokens` מחזיר `token`. הרשמה עצמית (`POST /api/users`) יוצרת תמיד **Employee**.

---

## Swagger

אחרי הפעלת השרת: **http://localhost:5258/swagger**. הממשק נפתח אוטומטית בהרצה דרך Visual Studio או `dotnet run`.

כדי לבדוק פעולות מוגנות:
1. הפעילו `POST /api/tokens` עם אימייל וסיסמה של משתמש דמו.
2. העתיקו את הערך של `token` מהתשובה.
3. לחצו על **Authorize** (למעלה בצד ימין), הדביקו את ה-token **בלי** המילה `Bearer`, ולחצו Authorize.
4. פעולות מוגנות מסומנות בנעילה.

כל הפעולות מתועדות: תקציר, DTOs של הבקשה והתשובה (כולל כללי הוולידציה), ותיאור לכל קוד תגובה אפשרי.
> Swagger פעיל **בכל סביבה**, כולל Production וכולל Render (כדי שאפשר יהיה לבדוק את ה-API שם). לכיבוי: `Swagger__Enabled=false`.

---

## Endpoints והרשאות

כל התשובות לשגיאות הן JSON אחיד (`ProblemDetails`) עם `status`, `title` ו-`correlationId`. שגיאות ולידציה כוללות גם `errors` לכל שדה.

| Method | נתיב | מי מורשה | הצלחה | שגיאות אפשריות |
|---|---|---|---|---|
| POST | `/api/users` | פתוח | 200 + token | 400, 409 (אימייל קיים), 429 |
| POST | `/api/tokens` | פתוח | 200 + token | 400, 401, 429 |
| GET | `/api/features` | מחובר | 200 | 401 |
| GET | `/api/parking-spots` | מחובר | 200 (עמוד) | 400, 401 |
| GET | `/api/parking-spots/{id}` | מחובר | 200 | 401, 404 |
| POST | `/api/parking-spots` | **Admin** | 201 + `Location` | 400, 401, 403, 409 (קוד קיים) |
| PUT | `/api/parking-spots/{id}` | **Admin** | 200 | 400, 401, 403, 404, 409 |
| DELETE | `/api/parking-spots/{id}` | **Admin** | 204 | 401, 403, 404, 409 (יש הזמנות עתידיות) |
| POST | `/api/reservations` | **Employee** | 201 + `Location` | 400, 401, 403, 404, **409** |
| GET | `/api/reservations/{id}` | הבעלים או Admin | 200 | 401, 403, 404 |
| GET | `/api/reservations/mine` | **Employee** | 200 (עמוד) | 400, 401, 403 |
| GET | `/api/reservations` | **Admin** | 200 (עמוד) | 400, 401, 403 |
| DELETE | `/api/reservations/{id}` | הבעלים או Admin | 204 | 401, 403, 404 |

**פרמטרים של `GET /api/parking-spots`:** `page`, `pageSize` (1-100), `floor`, `isActive`, `featureId`, `search` (חלק מהקוד), `date` (`yyyy-MM-dd`), `available` (דורש `date`), `sortBy` (`Code` או `Floor`) ו-`sortDir` (`Asc` או `Desc`). עם `date`, כל מקום מסומן `isAvailable`. ה-pagination, הסינון והמיון מתבצעים בבסיס הנתונים (`Skip`/`Take` בתוך השאילתה).

**דוגמה: הזמנה** (אחרי התחברות כ-Employee):
```http
POST /api/reservations
Authorization: Bearer <token>
Content-Type: application/json

{ "parkingSpotId": 3, "date": "2026-11-02" }
```
- `201 Created` עם `Location: /api/reservations/<id>` כשהצליח.
- `409 Conflict` אם המקום כבר תפוס בתאריך הזה, או שמשתמש אחר הקדים אותך.

**הרשאות:**
- **Admin:** מנהל מקומות חניה (יצירה, עדכון, הסרה), רואה את כל ההזמנות ויכול לבטל כל הזמנה.
- **Employee:** מזמין מקום, רואה את ההזמנות שלו ומבטל אותן. **אינו** יכול לנהל מקומות.
- **Admin אינו מזמין** (הזמנה מותרת לעובד בלבד).
- הרשאות נאכפות **בשרת** לפי ה-`role` שב-token החתום. זהות המשתמש נלקחת מה-token ולא מגוף הבקשה.

---

## כללים עסקיים

נאכפים בשכבת ה-Service, ולא ב-Controller:

- הזמנה היא ליום שלם אחד.
- אי אפשר להזמין תאריך שעבר, או רחוק יותר מ-**30 ימים** קדימה (`ReservationRules:MaxDaysAhead`).
- אפשר להזמין רק מקום **פעיל**.
- לכל משתמש הזמנה **פעילה אחת** ליום.
- ביטול משנה את הסטטוס ל-`Cancelled` וההזמנה נשמרת בהיסטוריה. המקום מתפנה מיד.
- "מחיקת" מקום חניה היא **הסרה מהשימוש** (`IsActive = false`): המקום נשאר עם ההיסטוריה שלו ואי אפשר להזמין אותו יותר. אפשר להחזיר אותו בעדכון. מקום עם הזמנות עתידיות פעילות לא יוסר (409).
- קוד מקום ואימייל משתמש ייחודיים (כולל כשמגיעות שתי בקשות באותו רגע).

---

## הרצת בדיקות

```bash
dotnet test ParkingSystem.sln
```

**227 בדיקות** (xUnit + Moq):

| קבוצה | מה נבדק |
|---|---|
| Service (Moq) | הצלחה **ודחייה** של כל פעולה: הזמנה, ביטול, מקומות חניה, הרשמה והתחברות |
| אבטחה | הצפנת סיסמאות, יצירת token ואימותו (חתימה, תוקף, Issuer, Audience, זיוף תפקיד, `alg: none`) |
| ולידציה ו-`ModelState` | כללי Data Annotations על כל ה-DTOs, וה-`ModelState` בכל פעולה ב-Controllers |
| DI, Middleware וכתובת החיבור | lifetimes, CorrelationId, טיפול בשגיאות, לוג בקשות, המרת כתובת `postgres://` של Render |
| **אינטגרציה (PostgreSQL)** | התחרות: שני `DbContext`, אינדקסים ייחודיים, ו-8 בקשות מקבילות; בדיקות HTTP מקצה לקצה; מסמך ה-Swagger; מוכנות לענן (CORS, כותרות proxy, `/healthz`, Production) |

### בדיקות שדורשות PostgreSQL
**51 בדיקות** (אינטגרציה, HTTP מקצה לקצה, Swagger ומוכנות לענן) מפעילות את ה-API או את בסיס הנתונים בפועל, ולכן דורשות PostgreSQL זמין:

```bash
docker compose up -d      # לפני dotnet test
```

- כל מחלקת בדיקות יוצרת **בסיס נתונים זמני משלה**, מחילה עליו את ה-migrations האמיתיים, ומוחקת אותו בסיום. **בסיס נתוני הפיתוח שלכם לא נפגע.**
- אם PostgreSQL **לא זמין**, 51 הבדיקות האלה **מדלגות** (`Skipped`) עם הודעה ברורה, ו-176 הבדיקות האחרות עוברות. הן לא נכשלות.
- כברירת מחדל הבדיקות מתחברות ל-`Host=localhost;Port=5432;Username=postgres` (בסיס הנתונים מ-`docker-compose`). לשימוש בשרת אחר, הגדירו את משתנה הסביבה `PARKING_TEST_POSTGRES` למחרוזת חיבור שמאפשרת `CREATE DATABASE`.

---

## קונפיגורציה וסודות

אין בקוד או ב-Git אף סיסמה, מפתח או token אמיתיים.

| הגדרה | מאיפה היא מגיעה |
|---|---|
| `ConnectionStrings:Default` | `appsettings.Development.json` (בסיס נתונים מקומי, בלי סיסמה), או User Secrets / משתנה סביבה |
| `Jwt:SecretKey` | **User Secrets** בפיתוח, או משתנה סביבה. **חובה** |
| `SeedUsers:AdminPassword`, `SeedUsers:EmployeePassword` | User Secrets או משתנה סביבה. אופציונלי |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` | `appsettings.json` (אינן סוד) |
| `ReservationRules:MaxDaysAhead` | `appsettings.json` |
| `Cors:AllowedOrigins` | כתובת הלקוח (כמה כתובות מופרדות בפסיק). בפיתוח: `http://localhost:5173` ב-`appsettings.Development.json`. בענן: משתנה סביבה. **בלי הגדרה, אף אתר אחר לא יכול לקרוא ל-API מהדפדפן** |
| `RateLimiting:AuthPermitLimit`, `RateLimiting:AuthWindowSeconds` | הגבלת קצב להרשמה והתחברות לכל כתובת IP. ברירת מחדל: 10 בקשות ב-60 שניות, ואחריהן 429 עם `Retry-After`. המונה בזיכרון של המופע (מתאים למופע יחיד). משתמשים מאחורי אותה כתובת (למשל רשת משותפת בהדגמה) חולקים מונה |
| `Swagger:Enabled` | ברירת מחדל `true` בכל סביבה |
| `PORT` | נקבע על ידי Render. השרת מאזין לפורט הזה על `0.0.0.0` (מקומית: הכתובות מ-`launchSettings.json`) |

משתני סביבה נכתבים עם `__` במקום `:`, למשל `Jwt__SecretKey`. כתובת בסיס הנתונים יכולה להיות מחרוזת רגילה (`Host=...`) או כתובת `postgres://user:password@host:5432/db` כמו ש-Render נותן (השרת ממיר אותה).

---

## לוגים

NLog (`nlog.config`) כותב לקונסולה ולקובץ יומי: `ParkingSystem.API/bin/<Debug|Release>/net8.0/logs/parking-<תאריך>.log` (14 הימים האחרונים נשמרים). תיקיית ה-`bin` מוחרגת מ-Git.

פורמט שורה: `זמן | רמה | מי כתב | CorrelationId | הודעה`.

- **כל בקשה** נרשמת (שיטה, כתובת ללא query string, קוד תשובה, משך) עם ה-`CorrelationId`, שחוזר גם ללקוח בכותרת `X-Correlation-Id`.
- **`Error`:** כל חריגה שלא טופלה.
- **`Warning`:** כל התנגשות על משאב (הפסד בתחרות על מקום).
- **`Debug`** נכתב בפיתוח בלבד.
- **לא נכתבים** סיסמאות, tokens, כותרות או גוף בקשות.

---

## Migrations

המערכת משתמשת ב-EF Core Code-First עם שני migrations (`InitialCreate`, `AddReservationDateIndex`). הם **מוחלים אוטומטית בעלייה**, ולכן אין צורך להריץ אותם ידנית.

רק כדי ליצור migration חדש:
```bash
dotnet tool install --global dotnet-ef --version 8.*
dotnet ef migrations add <שם> -p ParkingSystem.Data -s ParkingSystem.API
```

---

## פריסה ל-Render

השרת והלקוח נפרסים **בנפרד** ב-Render. כל מה שנדרש לכך כבר כתוב ב-repository, ונבדק מקומית: תמונת ה-Docker נבנתה והורצה בדיוק כמו ש-Render מריץ אותה (משתמש לא-root, פורט מ-`PORT`, כתובת `postgres://`, סביבת Production), והתחברות, Seed, Swagger, CORS וכותרת `Location` ב-https עבדו.

> **מה עוד לא בוצע:** הפריסה בפועל. היא דורשת חשבון Render וחיבורו ל-GitHub (פעולות שרק בעלת החשבון יכולה לבצע). מדובר בשלבים הבאים.

### שלבים
1. **להעלות את שני ה-repositories ל-GitHub** (ראו [הגשה](#הגשה-ושני-ה-repositories)).
2. **שרת:** ב-Render לחצו **New > Blueprint** ובחרו את ה-repository של השרת. `render.yaml` יוצר שירות Web (Docker, אזור Frankfurt) ובסיס נתונים PostgreSQL. Render ישאל על הערכים הבאים:

   | משתנה | מה להזין |
   |---|---|
   | `SeedUsers__AdminPassword` | סיסמת משתמש ה-Admin (למשל `Admin-Demo-2026!`) |
   | `SeedUsers__EmployeePassword` | סיסמת משתמש ה-Employee (למשל `Employee-Demo-2026!`) |
   | `Cors__AllowedOrigins` | אפשר להשאיר זמנית ריק, ולעדכן בשלב 4 |

   השאר נקבע אוטומטית: `ConnectionStrings__Default` מגיע מבסיס הנתונים, ו-`Jwt__SecretKey` נוצר אקראי על ידי Render (אינו נשמר ב-Git).
3. **לוודא שהשרת עלה:** `https://<שם-השירות>.onrender.com/healthz` מחזיר `{"status":"ok"}`, ו-`/swagger` נפתח. הבנייה הראשונה לוקחת כמה דקות. בעלייה השרת מחיל את ה-migrations ומזין את נתוני ההתחלה לבד.
4. **לקוח:** ב-repository של הלקוח: **New > Blueprint** (ראו ה-README שלו). הזינו `VITE_API_URL` = כתובת השרת. אחרי שהלקוח עלה, חזרו לשרת, **Environment > `Cors__AllowedOrigins`** = כתובת הלקוח (בלי `/` בסוף), ושמרו.
5. **לבדוק מקצה לקצה:** פתחו את הלקוח, התחברו עם משתמש דמו, והזמינו מקום.

### מגבלות של התוכנית החינמית (לפי התיעוד של Render, נבדק ב-2026-10-17)
- שירות Web חינמי **נרדם אחרי 15 דקות בלי תעבורה**, וההתעוררות לוקחת כדקה. יש 750 שעות חינמיות בחודש לכל workspace.
- בסיס נתונים חינמי **פג תוקף 30 יום אחרי יצירתו** (ואז 14 ימי חסד לפני שהוא נמחק), עד 1GB, ובסיס נתונים חינמי אחד לכל workspace. **לפני ההגשה כדאי לוודא שהתוקף לא פג.**
- אתר סטטי (הלקוח) חינמי, בלי מגבלת התעוררות.

### בדיקה מקומית של תמונת ה-Docker
```bash
docker build -t parking-system-api .
```
והרצה (לדוגמה מול בסיס הנתונים של `docker-compose`, באותה רשת):
```bash
docker run --rm -p 8080:10000 --network project_dotnet_default \
  -e PORT=10000 \
  -e "ConnectionStrings__Default=postgres://postgres@parking-system-db:5432/parking_system" \
  -e "Jwt__SecretKey=<מפתח מקומי באורך 32 תווים לפחות>" \
  parking-system-api
```

---

## הגשה ושני ה-repositories

הדרישות: שני repositories נפרדים ב-GitHub, שרת ולקוח, עם `.gitignore` מתאים בכל אחד.

| repository | תיקייה מקומית | סטטוס |
|---|---|---|
| שרת | `project_dotnet` | קיים ב-GitHub: `parking-system-server` (פרטי). **יש commits מקומיים שטרם נדחפו** |
| לקוח | `parking-system-client` (תיקייה אחות) | commit מקומי בלבד, **עדיין אין repository ב-GitHub** |

להעלאה (פעם אחת, מחשבון ה-GitHub שלכם):
```bash
# שרת: ה-remote כבר מוגדר
cd project_dotnet
git push origin main

# לקוח: יוצרים repository ריק ב-GitHub (בלי README ובלי .gitignore), ואז:
cd ../parking-system-client
git remote add origin https://github.com/<החשבון-שלכם>/parking-system-client.git
git push -u origin main
```
- שני ה-repositories **נקיים מסודות** (נבדק, כולל כל ההיסטוריה). ה-`.gitignore` מחריג `bin`, `obj`, `node_modules`, `dist`, לוגים, `.env` וקובצי User Secrets.
- כדי שהבודקת תוכל לראות אותם, הוסיפו אותה כ-Collaborator, או הפכו את ה-repositories לציבוריים.

---

## החלטות והסתייגויות ידועות

- **PostgreSQL ו-`xmin`:** ב-PostgreSQL אין `rowversion`, ולכן ה-concurrency token ממופה לעמודת המערכת `xmin`. כל התאריכים נשמרים ב-UTC.
- **AutoMapper 14.0.0:** מסומנת בפגיעות ידועה (CVE-2026-32933, מניעת שירות במיפוי גרפים עמוקים או מעגליים). הגרסאות המתוקנות דורשות מפתח רישיון, והדרישות מציינות AutoMapper. הסיכון מקובל כאן כי המיפויים שטוחים ובעומק קבוע ואינם רצים על קלט של משתמש. ההתראה מוסתרת **במפורש** עם הסבר ב-`ParkingSystem.Service.csproj`. מומלץ לבדוק מחדש אם היקף המיפוי גדל.
- **מיפוי:** גם התשובות וגם הבקשות (הרשמה, יצירה ועדכון של מקום, הזמנה) ממופות ב-AutoMapper. שדות שהשרת קובע בעצמו (מזהה, גרסה, בעל ההזמנה, זמן יצירה, hash של סיסמה) מוחרגים במיפוי, והשירות ממלא אותם.
- **סיסמה:** 8 עד 72 **בייטים** (לא תווים): BCrypt קורא רק 72 בייטים, ותו בעברית תופס 2 בייטים, ולכן 36 אותיות בעברית הן המקסימום.
- **Swagger בייצור:** פעיל בכל סביבה (כולל Render), כי הדרישות מבקשות Swagger פעיל. הוא חושף את מבנה ה-API בלבד, ללא נתונים. אפשר לכבות עם `Swagger__Enabled=false`.
- **שמות ה-endpoints של Auth:** `POST /api/users` (הרשמה) ו-`POST /api/tokens` (התחברות), כמשאבים ולא כשמות פעולות. ההרשמה מחזירה 200 עם ה-token (ולא 201), כי אין endpoint שמציג משתמש בודד שאליו אפשר להצביע ב-`Location`.
- **`UseHttpsRedirection`:** פעיל בפיתוח בלבד. בהרצה מקומית על HTTP בלבד נרשמת אזהרה "Failed to determine the https port" בכל בקשה, והיא לא משפיעה על התפקוד. בענן ה-HTTPS נעצר ב-load balancer של Render.
- **כותרות proxy:** השרת מקבל `X-Forwarded-*` מכל מקור (ה-proxy של Render אינו בכתובת קבועה). ההשפעה היחידה היא על הסכמה והדומיין שבכותרת `Location`.
- **לוג שגיאות של EF:** EF Core כותב `Error` על כל שמירה שנכשלה, גם כשהאינדקס הייחודי עצר הזמנה כפולה בתחרות והשרת החזיר 409 תקין. כדי שלא יטעה, הקטגוריה הזו מושתקת ב-`nlog.config`: כשל מטופל נרשם כ-`Warning` על ידי השירותים, וחריגה שלא טופלה נרשמת כ-`Error` על ידי ה-middleware.
- **אין endpoint לקידום משתמש ל-Admin.** Admin נוצר רק ב-Seed. זו תוספת אפשרית, שאינה בתכולת הפרויקט.
- בתחרות על הזמנות לתאריכים שונים באותו מקום (ראו למעלה) ייתכן 409 מיותר שנפתר בניסיון חוזר.

---

## מצב הפרויקט

| חלק | מצב |
|---|---|
| שרת API: שכבות, DI, EF Core + migrations + Seed, JWT ו-roles, ולידציה, Middleware, NLog | ✅ הושלם |
| טיפול בתחרות על המשאב (שלוש שכבות) והוכחתו בבדיקות | ✅ הושלם |
| Swagger (כפתור Authorize, תיעוד קודי תגובה) | ✅ הושלם |
| בדיקות אוטומטיות בשרת (227) | ✅ הושלם |
| לקוח React (repository נפרד: 4 מסכים, 22 בדיקות, נבדק בדפדפן אמיתי מול השרת) | ✅ הושלם (commit מקומי) |
| הכנת השרת והלקוח ל-Render (Dockerfile, Blueprints, CORS, `/healthz`, כותרות proxy) | ✅ הושלם ונבדק מקומית |
| העלאת שני ה-repositories ל-GitHub | ⬜ נדרשת פעולה שלכם (ראו [הגשה](#הגשה-ושני-ה-repositories)) |
| פריסה בפועל ל-Render | ⬜ נדרשת פעולה שלכם (ראו [פריסה ל-Render](#פריסה-ל-render)) |
