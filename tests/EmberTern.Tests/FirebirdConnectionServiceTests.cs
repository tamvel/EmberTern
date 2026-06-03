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

    [Fact]
    public void MapFbErrorText_LegacyAuthInMessage_ReturnsActionableHint()
    {
        var profile = new ConnectionProfile { Username = "SYSDBA" };

        var result = FirebirdConnectionService.MapFbErrorText(
            "Not supported plugin 'Legacy_Auth' from server", profile, errorCode: 0);

        Assert.Contains("Legacy_Auth", result);
        Assert.Contains("CREATE USER SYSDBA", result);
        Assert.Contains("USING PLUGIN Srp", result);
    }

    [Fact]
    public void MapFbErrorText_PluginKeyword_ReturnsActionableHint()
    {
        var profile = new ConnectionProfile { Username = "ADMIN" };

        var result = FirebirdConnectionService.MapFbErrorText(
            "Auth plugin not supported", profile, errorCode: 0);

        Assert.Contains("plugin mismatch", result);
        Assert.Contains("CREATE USER ADMIN", result);
    }

    [Fact]
    public void MapFbErrorText_WrongPassword_NoLegacyAuthHint()
    {
        var profile = new ConnectionProfile { Username = "SYSDBA" };

        var result = FirebirdConnectionService.MapFbErrorText(
            "Your user name and password are not defined", profile, errorCode: 335544472);

        Assert.Equal("Invalid username or password for 'SYSDBA'.", result);
        Assert.DoesNotContain("Legacy_Auth", result);
        Assert.DoesNotContain("plugin", result);
        Assert.DoesNotContain("CREATE USER", result);
    }

    [Fact]
    public void MapFbErrorText_FileNotFound_NoLegacyAuthHint()
    {
        var profile = new ConnectionProfile { DatabasePath = @"C:\nope.fdb" };

        var result = FirebirdConnectionService.MapFbErrorText(
            "I/O error: file not found", profile, errorCode: 0);

        Assert.Equal(@"Database file not found: C:\nope.fdb", result);
        Assert.DoesNotContain("Legacy_Auth", result);
        Assert.DoesNotContain("plugin", result);
    }

    [Fact]
    public void MapFbErrorText_UnknownError_FallsThroughToRawMessage_NoLegacyAuthHint()
    {
        var profile = new ConnectionProfile { Username = "SYSDBA" };

        var result = FirebirdConnectionService.MapFbErrorText(
            "Some other Firebird-specific error condition", profile, errorCode: 0);

        Assert.StartsWith("Firebird error: ", result);
        Assert.DoesNotContain("Legacy_Auth", result);
        Assert.DoesNotContain("CREATE USER", result);
    }

    [Fact]
    public void MapFbErrorText_EmptyMessage_DoesNotTriggerHint()
    {
        var profile = new ConnectionProfile { Username = "SYSDBA" };

        var result = FirebirdConnectionService.MapFbErrorText(string.Empty, profile, errorCode: 0);

        Assert.DoesNotContain("Legacy_Auth", result);
        Assert.DoesNotContain("plugin", result);
    }
}
