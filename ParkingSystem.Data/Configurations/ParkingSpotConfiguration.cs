using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSystem.Core.Entities;

namespace ParkingSystem.Data.Configurations;

public class ParkingSpotConfiguration : IEntityTypeConfiguration<ParkingSpot>
{
    // שם האינדקס קבוע כדי ש-Repository יזהה לפיו בדיוק כפילות של קוד מקום.
    // זה אותו שם ש-EF נתן ב-migration הקיים, ולכן אין שינוי בסכמה
    public const string CodeIndexName = "IX_ParkingSpots_Code";

    public void Configure(EntityTypeBuilder<ParkingSpot> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).IsRequired().HasMaxLength(20);
        builder.HasIndex(p => p.Code).IsUnique().HasDatabaseName(CodeIndexName);

        // ה-concurrency token. Npgsql ממפה אוטומטית מאפיין uint שמוגדר כ-RowVersion לעמודת המערכת xmin,
        // ש-PostgreSQL מעדכן בכל שינוי בשורה. EF מוסיף את הערך שנקרא לתנאי ה-WHERE של ה-UPDATE,
        // ואם מישהו אחר הספיק לשנות את השורה, לא תתעדכן אף שורה ותיזרק DbUpdateConcurrencyException
        builder.Property(p => p.Version).IsRowVersion();

        // many-to-many מול Feature: EF יוצר את טבלת הקישור לבד, וכאן רק קובעים את שמה
        builder.HasMany(p => p.Features)
            .WithMany(f => f.ParkingSpots)
            .UsingEntity(join => join.ToTable("ParkingSpotFeatures"));
    }
}
