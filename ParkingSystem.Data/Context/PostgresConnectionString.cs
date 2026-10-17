using Npgsql;

namespace ParkingSystem.Data.Context;

// Render (וספקי ענן אחרים) נותנים את כתובת בסיס הנתונים בצורת URL: postgres://user:password@host:5432/database.
// Npgsql מצפה לצורה Host=...;Username=...;Password=... , ולכן ממירים. מחרוזת שכבר בצורה הרגילה עוברת כמו שהיא
public static class PostgresConnectionString
{
    public static string Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured (appsettings, User Secrets or the ConnectionStrings__Default environment variable).");
        }

        var isUrl = connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                    || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        return isUrl ? FromUrl(new Uri(connectionString)) : connectionString;
    }

    private static string FromUrl(Uri uri)
    {
        // שם משתמש וסיסמה ב-URL מקודדים (למשל @ הופך ל-%40), ולכן מפענחים אותם
        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0])
        };

        if (credentials.Length > 1)
        {
            builder.Password = Uri.UnescapeDataString(credentials[1]);
        }

        // ?sslmode=require וכדומה
        var sslMode = ReadQueryValue(uri.Query, "sslmode");
        if (sslMode is not null && TryParseSslMode(sslMode, out var parsed))
        {
            builder.SslMode = parsed;
        }

        return builder.ConnectionString;
    }

    private static string? ReadQueryValue(string query, string key) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault();

    private static bool TryParseSslMode(string value, out SslMode mode) =>
        Enum.TryParse(value.Replace("-", string.Empty), ignoreCase: true, out mode);
}
