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

        Assert.DoesNotContain("SocketException", ex.Message);
        Assert.DoesNotContain("FbException", ex.Message);
        Assert.Contains("127.0.0.1:1", ex.Message);
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

        Assert.DoesNotContain("Exception", ex.Message);
    }
}
