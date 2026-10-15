using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Core.Interfaces.Repositories;
using ParkingSystem.Data.Configurations;
using ParkingSystem.Data.Context;

namespace ParkingSystem.Data.Repositories;

// מחלקת בסיס לכל ה-repositories. המקום היחיד שבו מתרגמים חריגות של EF ושל PostgreSQL
// לחריגה של Core, כדי שה-Service לא יצטרך להכיר אותן
public abstract class Repository : IRepository
{
    protected Repository(AppDbContext context)
    {
        Context = context;
    }

    protected AppDbContext Context { get; }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await Context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // ה-concurrency token (xmin) לא תאם: מישהו אחר שינה את השורה בין הקריאה לשמירה
            throw new ReservationConflictException("The data was changed by another request.", ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, ReservationConfiguration.SpotDateIndexName, ReservationConfiguration.UserDateIndexName))
        {
            // האינדקס הייחודי חסם הזמנה כפולה (רשת הביטחון של בסיס הנתונים)
            throw new ReservationConflictException("A reservation already exists for this spot or user on that date.", ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, UserConfiguration.EmailIndexName))
        {
            // שתי הרשמות מקבילות עם אותו אימייל: שתיהן עברו את הבדיקה, והאינדקס הייחודי עצר את השנייה
            throw new DuplicateEmailException("A user with this email already exists.", ex);
        }
    }

    // 23505 = unique_violation ב-PostgreSQL. מזהים לפי שם האינדקס המדויק, כדי שהפרת ייחודיות
    // של אילוץ אחד (למשל אימייל) לא תיחשב בטעות כהפרה של אילוץ אחר (למשל הזמנה)
    private static bool IsUniqueViolation(DbUpdateException ex, params string[] constraintNames) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName is not null
        && constraintNames.Contains(pg.ConstraintName);
}
