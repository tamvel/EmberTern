using System;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

public class CharsetSupportTests
{
    [Theory]
    [InlineData("WIN1250")]
    [InlineData("WIN1252")]
    [InlineData("ISO8859_2")]
    public async Task TestConnection_WithCharset_DoesNotFailOnCharset(string charset)
    {
        using var service = new FirebirdConnectionService();
        var profile = new ConnectionProfile
        {
            Name = "test",
            Host = "127.0.0.1",
            Port = 1, // force immediate socket refusal
            DatabasePath = "nope.fdb",
            Username = "SYSDBA",
            Password = "x",
            Charset = charset,
        };

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(
            () => service.TestConnectionAsync(profile));

        // The failure should be about the socket, not the charset.
        Assert.DoesNotContain("character set", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("charset", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("UTF8")]
    [InlineData("WIN1250")]
    [InlineData("WIN1252")]
    [InlineData("ISO8859_1")]
    [InlineData("ISO8859_2")]
    [InlineData("NONE")]
    public void DriverAcceptsCatalogCharsets(string charset)
    {
        // Touch the service so its static initializer registers code pages.
        _ = new FirebirdConnectionService();

        var builder = new FbConnectionStringBuilder { Charset = charset };
        Assert.Equal(charset, builder.Charset);
    }

    [Fact]
    public void EveryCatalogedCharsetIsDriverAccepted()
    {
        _ = new FirebirdConnectionService();

        foreach (var charset in CharsetCatalog.Supported)
        {
            var builder = new FbConnectionStringBuilder();
            // must not throw "Invalid character set specified."
            builder.Charset = charset;
        }
    }
}
