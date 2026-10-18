# ParkingSystem – מערכת להקצאת מקומות חניה

שרת **ASP.NET Core Web API** (‏.NET 8) שבו עובדים מזמינים מקום חניה ליום מסוים. זהו ה-repository של **השרת**. לקוח ה-React נמצא ב-repository נפרד (`parking-system-client`).

## המשאב המוגבל והתחרות עליו

**המשאב:** מקום חניה אחד ביום נתון. כל מקום יכול להיות מוזמן על ידי עובד אחד בלבד בכל יום.

**התחרות:** שני עובדים לוחצים "הזמן" על אותו מקום ולאותו יום באותו רגע. שניהם קוראים שהמקום פנוי, ושניהם מנסים לשמור.

**התגובה:** אחד מצליח (`201`). השני מקבל **`409 Conflict`** עם הודעה ברורה, וההזמנה שלו לא נשמרת. ההתנגשות נרשמת בלוג ברמת `Warning`.

המנגנון הוא **optimistic concurrency**, בשלוש שכבות:

| שכבה | איפה | מה היא עושה |
|---|---|---|
| 1. בדיקה עסקית | `ReservationService` | בודקת בתוך טרנזקציה אם המקום פנוי, ונותנת הודעה ברורה במקרה הרגיל |
| 2. concurrency token | `ParkingSpot.Version` = העמודה `xmin` של PostgreSQL | כל הזמנה מעדכנת גם את המקום, ולכן ה-`UPDATE` כולל `WHERE xmin = <הערך שנקרא>`. מי ששומר שני מקבל `DbUpdateConcurrencyException`, שהופכת ל-409 |
| 3. אינדקס ייחודי חלקי | בסיס הנתונים | `UNIQUE (ParkingSpotId, Date) WHERE Status = 'Active'` (וכן `(UserId, Date)`). גם אם שתי השכבות הראשונות נעקפו, הבסיס מסרב לשורה שנייה |

> **מחיר מוכר:** ה-token שייך למקום, ולכן שתי הזמנות לתאריכים **שונים** על אותו מקום באותו רגע עלולות להתנגש. השנייה תקבל 409, והמשתמש פשוט לוחץ שוב.

**הוכחה:** `ReservationConcurrencyTests` מריצה שני `DbContext` נפרדים: הראשון שומר והשני מקבל `DbUpdateConcurrencyException`. בדיקות נוספות מריצות 8 בקשות מקבילות (דרך ה-service וגם דרך HTTP): בדיוק אחת מצליחה והשאר מקבלות 409.

## ארכיטקטורה

| פרויקט | אחריות |
|---|---|
| `ParkingSystem.Core` | ישויות, enums, DTOs, ממשקים, `Result`, חריגות, `Options`. **לא מפנה לשום פרויקט או חבילה** |
| `ParkingSystem.Data` | `AppDbContext`, Fluent API, repositories, migrations, Seed (EF Core + Npgsql) |
| `ParkingSystem.Service` | לוגיקה עסקית, JWT, BCrypt, AutoMapper |
| `ParkingSystem.API` | Controllers, Middleware, רישום DI, Swagger, `nlog.config`, `Program.cs` |
| `ParkingSystem.Tests` | xUnit + Moq, ובדיקות אינטגרציה מול PostgreSQL |

```
API ──► Service ──► Core ◄── Data        (API מכיר את Data רק לרישום DI, migrations ו-Seed)
```

- ה-Controllers עובדים רק מול services דרך ממשקים מ-`Core`. ישויות אינן יוצאות ואינן נכנסות דרך ה-API, רק DTOs.
- **DI:** כל הרישום ב-`API/Extensions/ServiceCollectionExtensions.cs`. `DbContext`, repositories ו-services הם **Scoped**. מצפין הסיסמאות, יוצר ה-token והשעון הם **Singleton** (חסרי מצב). Singleton שמחזיק Scoped הוא באג: ה-`DbContext` נשאר חי בין בקשות, משותף בין threads (והוא אינו thread-safe), והנתונים שבו מתיישנים.
- **סדר ה-Middleware:** כותרות proxy ← טיפול גלובלי בשגיאות ← `CorrelationId` ← לוג בקשות ← Swagger ← CORS ← הגבלת קצב להתחברות ← Authentication ← Authorization ← Controllers.

## הרצה מקומית

דרישות: **.NET SDK 8**, **Docker Desktop** (ל-PostgreSQL 16) ו-**Git**. כל הפקודות מתיקיית השורש.

```bash
# 1. בסיס נתונים (PostgreSQL 16 ללא סיסמה, מקומי בלבד)
docker compose up -d

# 2. סודות (פעם אחת בכל מחשב). המפתח באורך 32 תווים לפחות
dotnet user-secrets set "Jwt:SecretKey" "<מפתח אקראי>" --project ParkingSystem.API
dotnet user-secrets set "SeedUsers:AdminPassword" "Admin-Demo-2026!" --project ParkingSystem.API
dotnet user-secrets set "SeedUsers:EmployeePassword" "Employee-Demo-2026!" --project ParkingSystem.API

# 3. הפעלה
dotnet run --project ParkingSystem.API
```

בעלייה השרת **מחיל לבד את ה-migrations ומזין נתונים**: 3 מאפיינים, 20 מקומות חניה ושני משתמשי דמו. אין שום פעולה ידנית בבסיס הנתונים.

- השרת: **http://localhost:5258**, Swagger: **http://localhost:5258/swagger**, בדיקת חיים: `/healthz`.
- בלי `Jwt:SecretKey` השרת מסרב לעלות (מכוון). בלי סיסמאות הדמו הוא עולה, אך משתמשי הדמו לא נוצרים.
- מחרוזת החיבור נמצאת ב-`appsettings.Development.json`. להחלפה: `dotnet user-secrets set "ConnectionStrings:Default" "..."`.
- **לקוח:** ב-repository `parking-system-client` (דורש Node.js 22.12+): `npm install` ואז `npm run dev`, בכתובת http://localhost:5173.
- **הכול בפקודה אחת (Windows), אחרי שלב 2:** `powershell -ExecutionPolicy Bypass -File scripts\start-local.ps1`.
- **איפוס בסיס הנתונים:** `docker compose down -v` ואז `docker compose up -d`. משתמשי הדמו נוצרים רק פעם אחת, ולכן שינוי סיסמה דורש איפוס.

## משתמשי דמו

| תפקיד | אימייל | סיסמה |
|---|---|---|
| **Admin** | `admin@parking.local` | `Admin-Demo-2026!` |
| **Employee** | `employee@parking.local` | `Employee-Demo-2026!` |

סיסמאות דמו בלבד, והן אינן בקובצי ההגדרות. הרשמה עצמית (`POST /api/users`) יוצרת תמיד Employee.

ב-Swagger: התחברו ב-`POST /api/tokens`, העתיקו את ה-`token`, לחצו **Authorize** והדביקו אותו **בלי** המילה `Bearer`.

## Endpoints והרשאות

כל השגיאות הן JSON אחיד (`ProblemDetails`) עם `status`, `title` ו-`correlationId`. הרשאות נאכפות בשרת לפי ה-`role` שב-token החתום.

| Method | נתיב | מי מורשה | הצלחה | שגיאות |
|---|---|---|---|---|
| POST | `/api/users` | פתוח | 200 + token | 400, 409, 429 |
| POST | `/api/tokens` | פתוח | 200 + token | 400, 401, 429 |
| GET | `/api/features` | מחובר | 200 | 401 |
| GET | `/api/parking-spots` | מחובר | 200 (עמוד) | 400, 401 |
| GET | `/api/parking-spots/{id}` | מחובר | 200 | 401, 404 |
| POST | `/api/parking-spots` | **Admin** | 201 + `Location` | 400, 401, 403, 409 |
| PUT | `/api/parking-spots/{id}` | **Admin** | 200 | 400, 401, 403, 404, 409 |
| DELETE | `/api/parking-spots/{id}` | **Admin** | 204 | 401, 403, 404, 409 |
| POST | `/api/reservations` | **Employee** | 201 + `Location` | 400, 401, 403, 404, **409** |
| GET | `/api/reservations/{id}` | הבעלים או Admin | 200 | 401, 403, 404 |
| GET | `/api/reservations/mine` | **Employee** | 200 (עמוד) | 400, 401, 403 |
| GET | `/api/reservations` | **Admin** | 200 (עמוד) | 400, 401, 403 |
| DELETE | `/api/reservations/{id}` | הבעלים או Admin | 204 | 401, 403, 404 |

`GET /api/parking-spots` תומך ב-`page`, `pageSize`, `floor`, `isActive`, `featureId`, `search`, `date`, `available`, `sortBy`, `sortDir`. העימוד, הסינון והמיון נעשים בבסיס הנתונים (`Skip`/`Take` בתוך השאילתה).

## כללים עסקיים

נאכפים בשכבת ה-Service:

- הזמנה היא ליום שלם, לא בעבר ולא יותר מ-30 ימים קדימה (`ReservationRules:MaxDaysAhead`), ורק למקום **פעיל**.
- לכל משתמש הזמנה **פעילה אחת** ליום. ביטול משנה סטטוס ל-`Cancelled` ומפנה את המקום מיד.
- "מחיקת" מקום היא הסרה מהשימוש (`IsActive = false`). מקום עם הזמנות עתידיות לא יוסר (409).
- קוד מקום ואימייל משתמש ייחודיים, גם כששתי בקשות מגיעות יחד.

## בדיקות

```bash
docker compose up -d      # ל-51 בדיקות האינטגרציה
dotnet test ParkingSystem.sln
```

**227 בדיקות** (xUnit + Moq): הצלחה ודחייה של כל פעולת service, אבטחה (JWT, BCrypt), ולידציה ו-`ModelState`, DI ו-Middleware, ואינטגרציה מול PostgreSQL (התחרות, בדיקות HTTP מקצה לקצה, Swagger, מוכנות לענן). כל מחלקת אינטגרציה יוצרת בסיס נתונים זמני משלה ומוחקת אותו בסיום, ובסיס הפיתוח לא נפגע. בלי PostgreSQL, 51 הבדיקות האלה מדלגות (`Skipped`) ולא נכשלות. שרת אחר: משתנה הסביבה `PARKING_TEST_POSTGRES`.

## קונפיגורציה, סודות ולוגים

אין בקוד או ב-Git סיסמה, מפתח או token. `Jwt:SecretKey` וסיסמאות הדמו מגיעים מ-User Secrets (או ממשתני סביבה, עם `__` במקום `:`). בענן הם מוגדרים ב-Render. `Cors:AllowedOrigins` קובע איזה אתר מורשה לקרוא ל-API, והגבלת הקצב (`RateLimiting:*`) מגבילה הרשמה והתחברות ל-10 בקשות בדקה לכל IP (אחריהן 429).

**לוגים (NLog):** נכתבים לקונסולה ולקובץ יומי ב-`ParkingSystem.API/bin/<Debug|Release>/net8.0/logs/`. פורמט: `זמן | רמה | מי כתב | CorrelationId | הודעה`. כל בקשה נרשמת עם ה-`CorrelationId` (שחוזר גם בכותרת `X-Correlation-Id`), כל חריגה ברמת `Error`, וכל התנגשות על משאב ברמת `Warning`. לא נכתבים סיסמאות, tokens או גוף בקשות.

## Migrations

EF Core Code-First עם שני migrations (`InitialCreate`, `AddReservationDateIndex`), שמוחלים אוטומטית בעלייה. ליצירת migration חדש:
```bash
dotnet ef migrations add <שם> -p ParkingSystem.Data -s ParkingSystem.API
```

## פריסה ל-Render

השרת והלקוח נפרסים בנפרד, ושניהם באוויר:

- **שרת:** https://parking-system-api-j020.onrender.com (Swagger ב-`/swagger`, בדיקת חיים ב-`/healthz`)
- **לקוח:** https://parking-system-client-ijof.onrender.com

משתמשי הדמו שלמעלה עובדים גם שם. אחרי חוסר פעילות השרת נרדם, ולכן הבקשה הראשונה עשויה לקחת כדקה.

ה-`Dockerfile` וה-`render.yaml` נמצאים ב-repository. להקמה מחדש:

1. העלו את ה-repository ל-GitHub, וב-Render בחרו **New > Blueprint**. נוצרים שירות Web ובסיס נתונים PostgreSQL.
2. הזינו `SeedUsers__AdminPassword` ו-`SeedUsers__EmployeePassword`. `Jwt__SecretKey` ו-`ConnectionStrings__Default` נקבעים אוטומטית.
3. ודאו ש-`https://<שירות>.onrender.com/healthz` מחזיר `{"status":"ok"}`.
4. העלו את הלקוח (Blueprint ב-repository שלו) עם `VITE_API_URL` = כתובת השרת, ואז הגדירו בשרת `Cors__AllowedOrigins` = כתובת הלקוח (בלי `/` בסוף).

בתוכנית החינמית: השרת נרדם אחרי 15 דקות בלי תעבורה (התעוררות של כדקה), ובסיס הנתונים **פג תוקף אחרי 30 יום**, כך שכדאי לוודא שהוא בתוקף לפני ההגשה.

## החלטות והסתייגויות

- **`xmin`:** ב-PostgreSQL אין `rowversion`, ולכן ה-concurrency token ממופה ל-`xmin`. כל התאריכים נשמרים ב-UTC.
- **AutoMapper 14.0.0:** מסומנת בפגיעות ידועה (CVE-2026-32933, מניעת שירות במיפוי גרפים עמוקים או מעגליים). הגרסאות המתוקנות דורשות רישיון, והדרישות מציינות AutoMapper. הסיכון מקובל כי המיפויים שטוחים ואינם רצים על קלט של משתמש. ההתראה מוסתרת במפורש עם הסבר ב-`ParkingSystem.Service.csproj`.
- **הרשמה** מחזירה 200 עם ה-token ולא 201, כי אין endpoint למשתמש בודד שאליו אפשר להצביע ב-`Location`.
- **לוג שגיאות של EF** מושתק ב-`nlog.config`: EF כותב `Error` גם כשהאינדקס הייחודי עצר הזמנה כפולה והשרת החזיר 409 תקין. כשל מטופל נרשם כ-`Warning` על ידי השירותים, וחריגה שלא טופלה נרשמת כ-`Error`.
