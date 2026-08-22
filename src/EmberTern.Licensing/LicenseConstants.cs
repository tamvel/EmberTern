namespace EmberTern.Licensing;

/// <summary>
/// The fixed values of the licence contract. Everything here is part of the artifact's meaning, so
/// changing one is a format decision, not a tweak — see <c>docs/design/licensing-system.md</c> §13.
/// </summary>
public static class LicenseConstants
{
    /// <summary>
    /// The value the payload's <c>prod</c> field must carry. Guards against an artifact issued for a
    /// different product being accepted here — cheap now, and the only defence if this signing key is
    /// ever used for a second product.
    /// </summary>
    public const string ProductId = "EmberTern";

    /// <summary>
    /// The highest payload version (<c>lv</c>) this build understands. ⭐ A licence declaring a higher
    /// version is REFUSED, never partially honoured: §13.4 rule 2 says any field whose <i>ignoring</i>
    /// would be unsafe travels with an <c>lv</c> bump, so ignoring an unknown high version is exactly the
    /// mistake the rule exists to prevent. The first such field will be V2's <c>iid</c> device binding.
    /// </summary>
    public const int MaxSupportedPayloadVersion = 1;

    /// <summary>
    /// How long after <c>exp</c> the product stays fully usable, with a persistent warning.
    /// ⭐ Not generosity — a correctness requirement: renewal in V1 is a human process (an administrator
    /// sends a file), so an expiry that bricks the tool at midnight on day zero turns a routine
    /// purchase-order delay into a work stoppage. Ratified as decision O2.
    /// </summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromDays(14);

    /// <summary>How long before <c>exp</c> the (dismissible) renewal reminder starts.</summary>
    public static readonly TimeSpan ExpiryWarningWindow = TimeSpan.FromDays(30);

    /// <summary>The extension of the licence artifact.</summary>
    public const string FileExtension = ".etlic";

    /// <summary>
    /// The file name the customer receives (decision O6 — always this, never the customer's name, which
    /// would put a company name into a filename that travels by e-mail).
    /// </summary>
    public const string DeliveredFileName = "EmberTern" + FileExtension;

    /// <summary>The file name EmberTern stores the accepted licence under.</summary>
    public const string StoredFileName = "license" + FileExtension;
}
