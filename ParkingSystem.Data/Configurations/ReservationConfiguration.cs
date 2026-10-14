using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;

namespace ParkingSystem.Data.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    // שמות האינדקסים הם קבועים כדי ש-Repository יזהה לפיהם התנגשות של הזמנות
    public const string SpotDateIndexName = "UX_Reservations_Spot_Date_Active";
    public const string UserDateIndexName = "UX_Reservations_User_Date_Active";

    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Date).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        // Restrict: אי אפשר למחוק משתמש או מקום שיש להם הזמנות
        builder.HasOne(r => r.User)
            .WithMany(u => u.Reservations)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ParkingSpot)
            .WithMany(p => p.Reservations)
            .HasForeignKey(r => r.ParkingSpotId)
            .OnDelete(DeleteBehavior.Restrict);

        // אינדקס רגיל לשאילתות שמסננות וממיינות לפי תאריך בלבד (רשימות ובדיקות הזמנות עתידיות)
        builder.HasIndex(r => r.Date);

        // הגנה אחרונה מפני כפילות, ברמת בסיס הנתונים. האינדקסים חלקיים (filtered):
        // הם חלים רק על הזמנות פעילות, כך שמקום שהזמנה עליו בוטלה אפשר להזמין שוב
        var activeOnly = $"\"Status\" = '{nameof(ReservationStatus.Active)}'";

        // מקום אחד לא יכול להיות מוזמן פעמיים באותו יום
        builder.HasIndex(r => new { r.ParkingSpotId, r.Date })
            .IsUnique()
            .HasFilter(activeOnly)
            .HasDatabaseName(SpotDateIndexName);

        // משתמש אחד לא יכול להחזיק שתי הזמנות פעילות באותו יום
        builder.HasIndex(r => new { r.UserId, r.Date })
            .IsUnique()
            .HasFilter(activeOnly)
            .HasDatabaseName(UserDateIndexName);
    }
}
