namespace ParkingSystem.Core.Options;

public class ReservationRulesOptions
{
    public const string SectionName = "ReservationRules";

    // כמה ימים קדימה מותר להזמין
    public int MaxDaysAhead { get; set; } = 30;
}
