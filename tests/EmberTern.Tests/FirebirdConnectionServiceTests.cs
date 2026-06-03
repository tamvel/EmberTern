using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class FirebirdConnectionServiceTests
{
    [Fact]
    public async Task TestConnection_AgainstRefusedPort_ReturnsReadableMessage()
    {
        using var service = new FirebirdConnectionService();
        var profile = new ConnectionProfile
        {
            Name = "bogus",
            Host = "127.0.0.1",
            Port = 1,
            DatabasePath = "nope.fdb",
            Username = "SYSDBA",
            Password = "x",
        };

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(
            () => service.TestConnectionAsync(profile));

        // Generic path: endpoint prefix + the server's own message verbatim.
        Assert.StartsWith("Could not connect to 127.0.0.1:1:", ex.Message);
    }

    [Fact]
    public async Task TestConnection_AgainstUnknownHost_ReturnsReadableMessage()
    {
        using var service = new FirebirdConnectionService();
        var profile = new ConnectionProfile
        {
            Name = "bogus",
            Host = "this-host-does-not-exist-embertern.invalid",
            Port = 3050,
            DatabasePath = "nope.fdb",
            Username = "SYSDBA",
            Password = "x",
        };

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(
            () => service.TestConnectionAsync(profile));

        Assert.StartsWith("Could not connect to this-host-does-not-exist-embertern.invalid:3050:", ex.Message);
    }

    // No special-casing: a Legacy_Auth message now returns the raw server message
    // like everything else — no hint, no interpretation.
    [Fact]
    public void MapErrorMessage_LegacyAuthInMessage_ReturnsRawServerMessage()
    {
        var profile = new ConnectionProfile { Username = "SYSDBA", Host = "localhost", Port = 3050 };
        var ex = new System.Exception("Not supported plugin 'Legacy_Auth' from server");

        var result = FirebirdConnectionService.MapErrorMessage(ex, profile);

        Assert.Equal("Could not connect to localhost:3050: Not supported plugin 'Legacy_Auth' from server", result);
        Assert.DoesNotContain("CREATE USER", result);
        Assert.DoesNotContain("USING PLUGIN", result);
    }

    // Wrong password / missing user / anything else: raw server message, no hint.
    [Fact]
    public void MapErrorMessage_OtherError_ReturnsRawServerMessage()
    {
        var profile = new ConnectionProfile { Username = "SYSDBA", Host = "db1", Port = 3055 };
        var ex = new System.Exception("Your user name and password are not defined.");

        var result = FirebirdConnectionService.MapErrorMessage(ex, profile);

        Assert.Equal("Could not connect to db1:3055: Your user name and password are not defined.", result);
        Assert.DoesNotContain("CREATE USER", result);
    }
}
