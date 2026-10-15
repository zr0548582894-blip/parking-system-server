namespace ParkingSystem.Tests.Helpers;

// שעון מזויף לבדיקות: תמיד אומר אותה שעה, כדי לבדוק לוגיקה שתלויה בזמן בלי להיות תלויים בשעון האמיתי
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
