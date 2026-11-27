using Npgsql;

namespace BookLibrary.Data;

// Builds the Postgres connection string from configuration, so no database address or
// password is ever stored in the code:
//   ConnectionStrings:Default  (user secrets in development, ConnectionStrings__Default on a server)
// Both formats work:
//   Host=...;Port=5432;Database=postgres;Username=...;Password=...
//   postgresql://user:password@host:5432/postgres      (the form Supabase shows)
public static class DatabaseConfig
{
    public static string GetConnectionString(IConfiguration config)
    {
        var raw = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "No database configured. Set ConnectionStrings:Default, for example:\n" +
                "  dotnet user-secrets set \"ConnectionStrings:Default\" \"Host=localhost;Database=booklibrary;Username=postgres;Password=...\"\n" +
                "On a server set the environment variable ConnectionStrings__Default. See the README.");

        raw = raw.Trim();
        var builder = raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                   || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            ? FromUri(raw)
            : new NpgsqlConnectionStringBuilder(raw);

        // Hosted databases such as Supabase require an encrypted connection. Turn it on for any
        // remote host unless the connection string already chose a mode.
        // ShouldSerialize is true only when the setting was actually given (ContainsKey is true for every known setting).
        if (!builder.ShouldSerialize("SSL Mode") && !IsLocal(builder.Host))
        {
            // Require encrypts the connection without checking the server certificate's issuer.
            // For strict checking, use SSL Mode=VerifyFull with the provider's CA certificate.
            builder.SslMode = SslMode.Require;
        }

        return builder.ConnectionString;
    }

    static NpgsqlConnectionStringBuilder FromUri(string raw)
    {
        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/') is { Length: > 0 } db ? Uri.UnescapeDataString(db) : "postgres",
            Username = Uri.UnescapeDataString(userInfo[0]),
        };
        if (userInfo.Length > 1) builder.Password = Uri.UnescapeDataString(userInfo[1]);

        // Extra options after the "?" such as sslmode=require.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = Uri.UnescapeDataString(kv[0]);
            var value = Uri.UnescapeDataString(kv[1]);
            builder[key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) ? "SSL Mode" : key] = value;
        }
        return builder;
    }

    static bool IsLocal(string? host) =>
        string.IsNullOrEmpty(host) || host is "localhost" or "127.0.0.1" or "::1" || host.StartsWith('/');
}
