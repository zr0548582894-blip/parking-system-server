using Microsoft.Extensions.Logging;

namespace ParkingSystem.Tests.Helpers;

// לוגר לבדיקות: שומר כל הודעה שנכתבה (כבר מעוצבת), כדי שאפשר יהיה לבדוק מה נכתב ובאיזו רמה
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
