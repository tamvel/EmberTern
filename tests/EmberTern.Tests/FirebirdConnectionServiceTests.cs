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

    // --- In-place active-profile update (editing the connected connection) ---

    [Fact]
    public void ShouldReplaceActiveProfile_SameId_ReturnsTrue()
    {
        var active = new ConnectionProfile { Id = "abc", Name = "old" };
        var edited = new ConnectionProfile { Id = "abc", Name = "new" };

        Assert.True(FirebirdConnectionService.ShouldReplaceActiveProfile(active, edited));
    }

    [Fact]
    public void ShouldReplaceActiveProfile_DifferentId_ReturnsFalse()
    {
        var active = new ConnectionProfile { Id = "abc" };
        var other = new ConnectionProfile { Id = "xyz" };

        Assert.False(FirebirdConnectionService.ShouldReplaceActiveProfile(active, other));
    }

    [Fact]
    public void ShouldReplaceActiveProfile_NoActiveConnection_ReturnsFalse()
    {
        var edited = new ConnectionProfile { Id = "abc" };

        Assert.False(FirebirdConnectionService.ShouldReplaceActiveProfile(null, edited));
        Assert.False(FirebirdConnectionService.ShouldReplaceActiveProfile(edited, null));
    }

    [Fact]
    public void UpdateActiveProfile_NoActiveConnection_ReturnsFalseAndDoesNotRaise()
    {
        using var service = new FirebirdConnectionService();
        var raised = false;
        service.ActiveProfileUpdated += (_, _) => raised = true;

        // Nothing connected — the edit targets no live connection, so it's a no-op.
        var changed = service.UpdateActiveProfile(new ConnectionProfile { Id = "abc", Name = "x" });

        Assert.False(changed);
        Assert.False(raised);
        Assert.Null(service.ActiveProfile);
    }

    // --- P2: FB3+ server version gate (decision 8 / spec §1.3) ---
    //
    // The predicate reuses FirebirdDdlReader.ParseServerMajor, which parses the FULL driver ServerVersion
    // (e.g. "WI-V5.0.0.1306 Firebird 5.0"), NOT a bare "5.0.3" — so these inputs are realistic
    // ServerVersion strings. A pre-FB3 major is refused; an unparseable string fails OPEN (a live Srp
    // connection is FB3+ by construction, so we never falsely reject one we merely can't read).

    [Theory]
    // Positively pre-FB3 → rejected.
    [InlineData("WI-V2.5.9.27139 Firebird 2.5", false)]
    [InlineData("LI-V2.5.9.27139 Firebird 2.5", false)]
    [InlineData("Firebird 2.5", false)]
    [InlineData("WI-V1.5.6.5026 Firebird 1.5", false)]
    // FB3/4/5 → allowed.
    [InlineData("WI-V3.0.7.33374 Firebird 3.0", true)]
    [InlineData("WI-V4.0.2.2816 Firebird 4.0", true)]
    [InlineData("WI-V5.0.0.1306 Firebird 5.0", true)]
    [InlineData("LI-V5.0.1.1469 Firebird 5.0", true)]
    // Unparseable / empty → fail-open (major 0 ⇒ treated as supported).
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("some unexpected banner", true)]
    public void IsSupportedServerVersion_GatesAtFB3(string? serverVersion, bool expectedSupported)
        => Assert.Equal(expectedSupported, FirebirdConnectionService.IsSupportedServerVersion(serverVersion));

    [Fact]
    public void UnsupportedServerMessage_NamesRequiredVersion_AndTheDetectedServer()
    {
        var msg = FirebirdConnectionService.UnsupportedServerMessage("WI-V2.5.9.27139 Firebird 2.5");
        Assert.Contains("Firebird 3.0 or later", msg);            // names the required version
        Assert.Contains("WI-V2.5.9.27139 Firebird 2.5", msg);     // and the detected server, verbatim
    }

    [Fact]
    public void UnsupportedServerMessage_NullVersion_IsStillReadable()
    {
        var msg = FirebirdConnectionService.UnsupportedServerMessage(null);
        Assert.Contains("Firebird 3.0 or later", msg);
        Assert.Contains("unknown", msg);
    }
}
