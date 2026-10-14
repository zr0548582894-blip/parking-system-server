namespace ParkingSystem.Core.Entities;

public class ParkingSpot
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Floor { get; set; }
    public bool IsActive { get; set; } = true;

    // מתעדכן בכל הזמנה, כדי שתמיד יישלח UPDATE למקום ושה-concurrency token ייבדק
    public DateTime? LastReservedAtUtc { get; set; }

    // ה-concurrency token. שכבת Data ממפה אותו ל-xmin של PostgreSQL ב-Fluent API,
    // ולכן Core נשאר בלי שום תלות ב-EF
    public uint Version { get; set; }

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<Feature> Features { get; set; } = new List<Feature>();
}
