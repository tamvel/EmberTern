using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using EmberTern.Licensing;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// The substitutions a message template may ask for, and the values they take.
///
/// <para>⭐⭐ <b>ONE catalogue, read by the templates and by the guard that checks them.</b> A placeholder
/// a template spells but this list does not carry is a build fault the composer refuses — because the
/// alternative is a customer receiving the literal text <c>{{Seats}}</c>, which no test that asserts only
/// "the message was composed" would ever notice.</para>
///
/// <para>⭐⭐ <b>Every value comes from the SIGNED payload of the artifact being attached</b> — never from
/// the licence row, never from a second model assembled for the message. §14.2 states the rule and the
/// reason: <i>the e-mail body never repeats a claim it could get wrong</i>. The register's own row and the
/// artifact can differ perfectly legitimately (terms saved but not yet issued), and a message built from
/// the row would then describe a licence the customer does not hold.</para>
///
/// <para>⚠ The two exceptions are exactly the two facts a licence deliberately does NOT carry: where to
/// reply, and under whose name. Both come from the SMTP settings — the operator's own configuration —
/// because §9's payload is an assertion about rights and carries no contact details at all.</para>
/// </summary>
public static class MessagePlaceholders
{
    /// <summary>The customer's name, exactly as it was signed — payload <c>lic</c>.</summary>
    public const string Licensee = "Licensee";

    /// <summary>The product the licence is for — payload <c>prod</c>.</summary>
    public const string Product = "Product";

    /// <summary>The contractual seat count — payload <c>seats</c> (decision D2: stated, never enforced).</summary>
    public const string Seats = "Seats";

    /// <summary>First day of validity — payload <c>nbf</c>.</summary>
    public const string ValidFrom = "ValidFrom";

    /// <summary>Last day of validity — payload <c>exp</c>.</summary>
    public const string ValidUntil = "ValidUntil";

    /// <summary>The stable licence identity — payload <c>lid</c>.</summary>
    public const string LicenceId = "LicenceId";

    /// <summary>The attachment's name. ⛔ Always <c>EmberTern.etlic</c> (decision O6).</summary>
    public const string FileName = "FileName";

    /// <summary>Who the message is signed by — the SMTP sender's display name.</summary>
    public const string SenderName = "SenderName";

    /// <summary>Where the customer writes back — the SMTP sender's address.</summary>
    public const string SenderAddress = "SenderAddress";

    /// <summary>
    /// The one day format the message uses, resolved against the MESSAGE's culture.
    ///
    /// <para>⚠ Deliberately not the culture's own long-date pattern: <c>pl</c> prefixes a weekday there,
    /// which reads as an appointment rather than as a contractual boundary. Month name and year in the
    /// reader's language is what §14.2 asks for — <i>"valid from 15 August 2026 to 15 August 2027"</i>.</para>
    /// </summary>
    public const string DayFormat = "d MMMM yyyy";

    /// <summary>Every placeholder a template may use. ⛔ The only list; the guard reads THIS.</summary>
    public static IReadOnlyList<string> All { get; } = new ReadOnlyCollection<string>(
    [
        Licensee,
        Product,
        Seats,
        ValidFrom,
        ValidUntil,
        LicenceId,
        FileName,
        SenderName,
        SenderAddress,
    ]);

    /// <summary>
    /// The values for one message.
    ///
    /// <para>⚠ <b>The dates are CALENDAR DAYS in UTC, not instants.</b> <c>exp</c> is the last second of
    /// the day the operator chose (<c>LicenseDay.EndOf</c>), so the day the customer must read is the UTC
    /// date of that instant. Converting to the reader's local zone would move an expiry across midnight
    /// for half the world and make the message disagree with the licence.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Values(
        LicensePayload payload, SmtpSettings settings, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(culture);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Licensee] = payload.Licensee,
            [Product] = payload.Product,
            [Seats] = payload.Seats.ToString(culture),
            [ValidFrom] = Day(payload.NotBefore, culture),
            [ValidUntil] = Day(payload.ExpiresAt, culture),
            [LicenceId] = payload.LicenseId,
            [FileName] = LicenseConstants.DeliveredFileName,

            // ⚠ An address alone is a valid sender (SmtpSettings.FromName is optional), and a message
            //    signed with an empty line would look like a fault. The address is the honest fallback.
            [SenderName] = string.IsNullOrWhiteSpace(settings.FromName)
                ? settings.FromAddress
                : settings.FromName,
            [SenderAddress] = settings.FromAddress,
        };
    }

    private static string Day(DateTimeOffset value, CultureInfo culture) =>
        value.UtcDateTime.Date.ToString(DayFormat, culture);
}
