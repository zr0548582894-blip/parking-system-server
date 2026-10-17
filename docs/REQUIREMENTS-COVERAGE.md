# דרישות מול בדיקות

מצב נכון ל-227 בדיקות בשרת (51 דורשות PostgreSQL), 22 בדיקות לקוח, 17 בדיקות E2E של הלקוח ו-15 בדיקות דפדפן.
העמודה "תוצאה" היא תוצאת ההרצה האחרונה בביקורת. "לא נבדק" מפרט במפורש מה אין לו כיסוי.

| # | דרישה | בדיקה שמכסה | תוצאה | לא נבדק |
|---|---|---|---|---|
| 1 | 4 שכבות (Core/Data/Service/API) | נבדק בבנייה (הפניות בין פרויקטים) | עבר (בנייה 0 אזהרות) | אין בדיקת ארכיטקטורה אוטומטית שאוסרת הפניה אסורה |
| 2 | EF Core Code-First, migrations, seed | `PostgresDatabaseFixture` מריץ migrations על בסיס חדש לכל מחלקה, ו-`ApiTestHost` מזין seed; בדיקות E2E קוראות 20 מקומות | עבר | אין בדיקת `Down` של migrations; אין בדיקת הרצה כפולה של seed (idempotency) ייעודית |
| 3 | REST + Swagger | `SwaggerDocumentTests` (8), `DeploymentReadinessTests` | עבר | תוכן התיאורים בעברית לא נבדק מילולית |
| 4 | ולידציית DTO ו-ModelState | `DtoValidationTests` (כולל גבולות עימוד, סיסמה בבייטים), `ControllerTests`, E2E `InvalidRegistration...`, `InvalidQueryParameters...` | עבר | |
| 5 | AutoMapper | `MappingProfileTests` (כולל `AssertConfigurationIsValid`) | עבר | פגיעות AutoMapper 14.0.0 מתועדת, לא נבדקת (ראו דוח) |
| 6 | DI ו-lifetimes | `DependencyInjectionTests` (6) | עבר | |
| 7 | async + CancellationToken | נבדק בקוד/בנייה, השירותים מקבלים `ct` | עבר חלקית | אין בדיקה שמוודאת ביטול בקשה באמצע שאילתה |
| 8 | Middleware: שגיאות גלובליות ו-CorrelationId | `MiddlewareTests` (9), `RequestLoggingMiddlewareTests` (6) | עבר | |
| 9 | הגדרות וסודות | `PostgresConnectionStringTests`, בדיקת סודות ב-Git (סריקה ידנית) | עבר | אין בדיקה אוטומטית שאין סוד ב-Git (נסרק ידנית ב-HEAD ובהיסטוריה) |
| 10 | JWT ושני תפקידים | `JwtTokenServiceTests`, `JwtAuthenticationTests` (10), E2E `RolesAreEnforcedOnTheServer`, `ApiEndToEndTests` 401/403 | עבר | פעיל מול טוקן אחרי מחיקת משתמש נבדק רק להזמנה (401), לא לכל נקודת קצה |
| 11 | סיסמאות מוצפנות | `BcryptPasswordHasherTests` (כולל מגבלת 72 בייטים) | עבר | |
| 12 | משאב מוגבל: הצלחה ודחייה | `ReservationServiceTests` (22), E2E הזמנה/409 | עבר | |
| 13 | Optimistic concurrency (xmin, 409, שני DbContext) | `ReservationConcurrencyTests` (6, כולל `TwoContexts...`), E2E `ParallelReservationsOverHttp...` | עבר | תרחיש "תאריכים שונים על אותו מקום באותו רגע" (התנגשות שווא מתועדת) לא נבדק כתכונה |
| 14 | התנגשויות כפולות (מקום+יום, משתמש+יום) | אינדקסים ייחודיים: `UniqueIndex_...` (3 בדיקות), `UniqueConstraintTranslationTests` (3) | עבר | |
| 15 | ביטול הזמנה | `ReservationServiceTests`, E2E `CancellingFreesTheSpot...` | עבר | |
| 16 | עימוד ומקרי קצה | `DtoValidationTests`, E2E `HugePageNumber...`, `PageBeyondTheLastPage...` | עבר | אין בדיקת Repository ייעודית למיון/סינון משולב; מכוסה דרך E2E בלבד |
| 17 | NLog: לוג בקשות עם CorrelationId, Error על חריגות, Warning על התנגשות, בלי סודות | `RequestLoggingMiddlewareTests`, `MiddlewareTests`, `ReservationServiceTests` (אזהרות), ריצות חיות: 0 שורות ERROR בניסויים | עבר | אין בדיקה שמחפשת סוד בקובץ הלוג בפועל |
| 18 | הגבלת קצב להתחברות/הרשמה (תוספת) | E2E `Login_AfterTooManyAttempts...`, `RateLimit_DoesNotAffectOtherEndpoints`, `Login_WithinTheLimit...`; קונטיינר: 429 החל מהבקשה ה-11 | עבר | זיהוי IP מאחורי proxy אמיתי של Render לא נבדק; המונה בזיכרון (מופע יחיד) |
| 19 | CORS | `DeploymentReadinessTests` (6), E2E של הלקוח (preflight) | עבר | |
| 20 | Docker ו-Render | `DeploymentReadinessTests`, בניית תמונה והרצה בקונטיינר (healthz, כניסה, Swagger, 429, משתמש לא-root) | עבר מקומית | פריסה בפועל ל-Render לא בוצעה ולא נבדקה (דורשת חשבון) |
| 21 | לקוח: התחברות, עימוד, הזמנה, הודעת 409 | `api.test.js`, `session.test.js`, `errors.test.js` (22); `scripts/e2e.mjs` (17); בדפדפן Edge אמיתי (15) | עבר | אין בדיקות רכיבי React (רינדור) ברמת יחידה; מסכי Admin לא קיימים בלקוח |
| 22 | README | קריאה ידנית | | אין בדיקה אוטומטית שההוראות רצות מאפס על מחשב נקי |
