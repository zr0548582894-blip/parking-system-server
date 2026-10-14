namespace ParkingSystem.Core.Entities;

public class Feature
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // קשר many-to-many: EF ייצור לבד את טבלת הקישור
    public ICollection<ParkingSpot> ParkingSpots { get; set; } = new List<ParkingSpot>();
}
