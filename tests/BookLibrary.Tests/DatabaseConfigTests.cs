using BookLibrary.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BookLibrary.Tests;

// The connection string reader (no database needed).
public class DatabaseConfigTests
{
    static NpgsqlConnectionStringBuilder Read(string? value)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(value == null ? [] : new Dictionary<string, string?> { ["ConnectionStrings:Default"] = value })
            .Build();
        return new NpgsqlConnectionStringBuilder(DatabaseConfig.GetConnectionString(config));
    }

    [Theory]
    [InlineData("Kj84mXpq29Lw")]
    [InlineData("Ml@den14")]
    [InlineData("[MyDog+MyCat=8legs]")]
    [InlineData("a#b/c?d:e")]
    public void Supabase_url_keeps_the_password_exactly_as_pasted(string password)
    {
        var b = Read($"postgresql://postgres.abc:{password}@aws-0-eu-west-1.pooler.supabase.com:5432/postgres");
        Assert.Equal("aws-0-eu-west-1.pooler.supabase.com", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("postgres", b.Database);
        Assert.Equal("postgres.abc", b.Username);
        Assert.Equal(password, b.Password);
    }

    [Fact]
    public void Encoded_password_is_decoded() =>
        Assert.Equal("p@ss#w/d", Read("postgresql://u:p%40ss%23w%2Fd@host.example.com/db").Password);

    [Fact]
    public void Remote_hosts_get_encryption_and_local_hosts_do_not()
    {
        Assert.Equal(SslMode.Require, Read("postgresql://u:p@db.example.com:5432/db").SslMode);
        Assert.Equal(SslMode.Require, Read("Host=db.example.com;Database=x;Username=u;Password=p").SslMode);
        Assert.NotEqual(SslMode.Require, Read("Host=localhost;Database=x;Username=u;Password=p").SslMode);
    }

    [Fact]
    public void An_explicit_ssl_mode_is_kept() =>
        Assert.Equal(SslMode.Disable, Read("postgresql://u:p@db.example.com:5432/db?sslmode=disable").SslMode);

    [Fact]
    public void Quotes_saved_by_mistake_are_ignored() =>
        Assert.Equal("db.example.com", Read("'postgresql://u:p@db.example.com:5432/db'").Host);

    [Fact]
    public void Missing_setting_explains_what_to_set()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Read(null));
        Assert.Contains("ConnectionStrings:Default", ex.Message);
    }

    [Fact]
    public void Bad_port_error_does_not_repeat_the_password()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Read("postgresql://u:SecretPw1@host:abc/db"));
        Assert.DoesNotContain("SecretPw1", ex.Message);
    }
}
