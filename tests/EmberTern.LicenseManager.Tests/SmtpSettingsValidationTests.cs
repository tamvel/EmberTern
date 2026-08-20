using EmberTern.LicenseManager.Email;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// What the settings refuse.
///
/// <para>⭐ <b>Validation here answers about the SETTINGS ALONE.</b> Whether a server accepts them is a
/// question only that server can answer, and this deliberately does not pretend otherwise — the same
/// posture EmberTern takes with connection errors, where the server's own message is authoritative and
/// the client adds no interpretation.</para>
/// </summary>
public sealed class SmtpSettingsValidationTests
{
    private static SmtpSettings Direct => new()
    {
        Host = "smtp.example.com",
        FromAddress = "licencje@example.com",
        Username = "licencje@example.com",
        Password = "secret",
    };

    [Fact]
    public void NothingConfiguredIsNotUsable()
    {
        Assert.False(SmtpSettings.Empty.IsConfigured);
        Assert.False(SmtpSettings.Empty.CanSendDirectly);
        Assert.NotEmpty(SmtpSettings.Empty.Validate());
    }

    /// <summary>
    /// ⭐⭐ A sender address ALONE is a valid configuration — it is everything an <c>.eml</c> needs.
    ///
    /// <para>⛔ Requiring a server would make file delivery depend on the very thing it exists to work
    /// without, which is the whole point of D8: e-mail is a convenience, never the only way out.</para>
    /// </summary>
    [Fact]
    public void ASenderAddressAloneIsEnoughToDeliverByFile()
    {
        var settings = new SmtpSettings { FromAddress = "licencje@example.com" };

        Assert.Empty(settings.Validate());
        Assert.True(settings.IsConfigured);
        Assert.False(settings.CanSendDirectly);
    }

    [Fact]
    public void AHostTurnsFileDeliveryIntoDirectSending()
    {
        Assert.True(Direct.CanSendDirectly);
        Assert.Empty(Direct.Validate());
    }

    [Fact]
    public void AMissingSenderAddressIsRefused()
    {
        var problems = (Direct with { FromAddress = "" }).Validate();

        Assert.Contains(problems, p => p.Contains("sender address", System.StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("nie-adres")]
    [InlineData("dwa@@example.com")]
    [InlineData("ze spacja@example.com")]
    [InlineData("@example.com")]
    [InlineData("brak-domeny@")]
    public void AnAddressThatCannotBeOneIsRefused(string address)
    {
        Assert.NotEmpty((Direct with { FromAddress = address }).Validate());
    }

    /// <summary>
    /// ⭐⭐ <b>The one combination that is refused rather than warned about.</b> A password on a
    /// connection that never becomes encrypted is a password on the wire, and that is not a choice this
    /// application lets an operator make by accident.
    /// </summary>
    [Fact]
    public void CredentialsWithoutStartTlsAreRefused()
    {
        var problems = (Direct with { Security = SmtpSecurity.None }).Validate();

        Assert.Contains(problems, p => p.Contains("STARTTLS", System.StringComparison.Ordinal));
    }

    /// <summary>⭐ An unauthenticated internal relay over plain SMTP is legal — that is what the mode is for.</summary>
    [Fact]
    public void AnUnauthenticatedRelayWithoutStartTlsIsAllowed()
    {
        var relay = new SmtpSettings
        {
            Host = "relay.internal",
            Port = 25,
            Security = SmtpSecurity.None,
            FromAddress = "licencje@example.com",
        };

        Assert.Empty(relay.Validate());
    }

    [Fact]
    public void APasswordWithoutAUsernameIsRefused()
    {
        Assert.NotEmpty((Direct with { Username = "" }).Validate());
    }

    /// <summary>⚠ Credentials with nowhere to send them is a half-finished form, not a configuration.</summary>
    [Fact]
    public void CredentialsWithoutAServerAreRefused()
    {
        var problems = (Direct with { Host = "" }).Validate();

        Assert.Contains(problems, p => p.Contains("no server", System.StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void APortOutsideTheLegalRangeIsRefused(int port)
    {
        Assert.NotEmpty((Direct with { Port = port }).Validate());
    }

    /// <summary>
    /// ⚠ A port is only meaningful with a host, so a nonsense port on a file-only configuration is not
    /// worth refusing — there is nothing it can break.
    /// </summary>
    [Fact]
    public void APortIsNotJudgedWhenThereIsNoServer()
    {
        var fileOnly = new SmtpSettings { FromAddress = "licencje@example.com", Port = 0 };

        Assert.Empty(fileOnly.Validate());
    }

    /// <summary>
    /// ⭐⭐ Implicit TLS is deliberately NOT a choice: <c>SmtpClient</c> implements STARTTLS only, so
    /// offering it would offer a decision that can have no effect — the defect that removed
    /// <c>ClientLibraryPath</c> from EmberTern's connection dialog.
    /// </summary>
    [Fact]
    public void ThereAreExactlyTwoTransportChoicesAndImplicitTlsIsNotOneOfThem()
    {
        var names = System.Enum.GetNames<SmtpSecurity>();

        Assert.Equal(2, names.Length);
        Assert.Contains(nameof(SmtpSecurity.StartTls), names);
        Assert.Contains(nameof(SmtpSecurity.None), names);
        Assert.DoesNotContain("Implicit", names);
    }

    /// <summary>⭐ The submission port every measured provider uses, so the form opens on it.</summary>
    [Fact]
    public void TheDefaultPortIsTheSubmissionPort()
    {
        Assert.Equal(587, SmtpSettings.DefaultPort);
        Assert.Equal(587, SmtpSettings.Empty.Port);
        Assert.Equal(SmtpSecurity.StartTls, SmtpSettings.Empty.Security);
    }
}
