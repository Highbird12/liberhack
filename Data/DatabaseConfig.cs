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

        raw = raw.Trim().Trim('"', '\'').Trim(); // tolerate quotes that were saved by mistake
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

    // Reads postgresql://user:password@host:port/database?options by hand instead of with System.Uri,
    // which rejects passwords containing characters such as @ # / ? [ ]. The password is everything
    // between the first ":" and the LAST "@", so Supabase's generated passwords work as pasted.
    // (A literal % in a password should be written as %25.) Errors never include the connection string.
    static NpgsqlConnectionStringBuilder FromUri(string raw)
    {
        var rest = raw[(raw.IndexOf("://", StringComparison.Ordinal) + 3)..];

        string? userInfo = null;
        var at = rest.LastIndexOf('@');
        if (at >= 0)
        {
            userInfo = rest[..at];
            rest = rest[(at + 1)..];
        }

        var query = "";
        var q = rest.IndexOf('?');
        if (q >= 0) { query = rest[(q + 1)..]; rest = rest[..q]; }

        var database = "";
        var slash = rest.IndexOf('/');
        if (slash >= 0) { database = rest[(slash + 1)..].Trim('/'); rest = rest[..slash]; }

        var hostPart = rest;
        var port = 5432;
        var colon = hostPart.LastIndexOf(':');
        if (colon >= 0 && !hostPart.EndsWith(']'))
        {
            if (!int.TryParse(hostPart[(colon + 1)..], out port) || port is < 1 or > 65535)
                throw new InvalidOperationException(
                    "The database connection string is not valid: the port after the host name is not a number. " +
                    "Expected postgresql://user:password@host:5432/postgres");
            hostPart = hostPart[..colon];
        }
        if (string.IsNullOrWhiteSpace(hostPart))
            throw new InvalidOperationException(
                "The database connection string is not valid: no host name found. " +
                "Expected postgresql://user:password@host:5432/postgres");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = hostPart.Trim('[', ']'),
            Port = port,
            Database = database.Length > 0 ? Uri.UnescapeDataString(database) : "postgres",
        };
        if (userInfo != null)
        {
            var sep = userInfo.IndexOf(':');
            builder.Username = Uri.UnescapeDataString(sep >= 0 ? userInfo[..sep] : userInfo);
            if (sep >= 0) builder.Password = Uri.UnescapeDataString(userInfo[(sep + 1)..]);
        }

        // Extra options after the "?" such as sslmode=require.
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
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
