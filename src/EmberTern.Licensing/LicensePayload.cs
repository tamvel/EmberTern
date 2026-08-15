using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace EmberTern.Licensing;

/// <summary>
/// The signed assertion about a customer's rights — <c>docs/design/licensing-system.md</c> §9.
///
/// <para>⭐ <b>A licence is an assertion about rights, not a description of a person or a computer.</b>
/// Address, first name, last name, e-mail, internal notes and every hardware signal are deliberately
/// absent: they would turn a file that travels by e-mail into a personal-data leak, and the client uses
/// none of them. They live in the License Manager's register instead (§12.2).</para>
///
/// <para>⚠ <b>Unknown fields are ignored, and that is only safe because of one rule:</b> any field whose
/// <i>ignoring</i> would be unsafe travels with an <c>lv</c> bump (§13.4 rule 2). V2's device binding
/// <c>iid</c> is the first such field, which is why a bound licence is <c>lv = 2</c> and a V1 build
/// refuses it outright rather than silently ignoring the binding.</para>
/// </summary>
public sealed record LicensePayload
{
    // ⛔ Persisted verbatim inside signed artifacts. APPEND-ONLY: never rename, never reuse.
    internal const string FieldVersion = "lv";
    internal const string FieldKeyId = "kid";
    internal const string FieldAlgorithm = "alg";
    internal const string FieldLicenseId = "lid";
    internal const string FieldProduct = "prod";
    internal const string FieldLicensee = "lic";
    internal const string FieldSeats = "seats";
    internal const string FieldIssuedAt = "iat";
    internal const string FieldNotBefore = "nbf";
    internal const string FieldExpiresAt = "exp";
    internal const string FieldMaintenanceUntil = "maint";

    /// <summary>
    /// The one accepted timestamp shape: RFC 3339, UTC, second precision, e.g.
    /// <c>2027-08-15T23:59:59Z</c>.
    ///
    /// <para>⭐ Deliberately a single exact format rather than a lenient parse. We own the only issuer, so
    /// there is no interoperability to buy, and a lenient parser is how <c>"2027-08-15"</c> silently
    /// becomes midnight in whatever the reader's local zone happens to be — a licence that expires at a
    /// different instant on two machines. ⚠ Adding an accepted format later is backward compatible (old
    /// artifacts still parse); removing one is not.</para>
    /// </summary>
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>Payload schema version — <c>lv</c>.</summary>
    public required int Version { get; init; }

    /// <summary>Which trusted key signed this — <c>kid</c>. ⭐ Selects the key AND the algorithm.</summary>
    public required string KeyId { get; init; }

    /// <summary>Wire id of the signature algorithm — <c>alg</c>. ⛔ Cross-checked only; never selects.</summary>
    public required string AlgorithmId { get; init; }

    /// <summary>Stable licence identity across renewals — <c>lid</c>.</summary>
    public required string LicenseId { get; init; }

    /// <summary>Must equal <see cref="LicenseConstants.ProductId"/> — <c>prod</c>.</summary>
    public required string Product { get; init; }

    /// <summary>The displayed licensee name — <c>lic</c>. Required, and shown in About and Settings.</summary>
    public required string Licensee { get; init; }

    /// <summary>Contractual seat count — <c>seats</c>. ⚠ Displayed, never enforced (decision D2).</summary>
    public required int Seats { get; init; }

    /// <summary>When this artifact was issued — <c>iat</c>. ⭐ The freshness ordering key for replacement.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>Start of validity — <c>nbf</c>.</summary>
    public required DateTimeOffset NotBefore { get; init; }

    /// <summary>End of validity — <c>exp</c>.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Perpetual-fallback boundary — <c>maint</c>. Builds released after this date are not covered.
    /// ⚠ Nothing issues it in V1; the gate exists because it cannot be retrofitted onto clients already in
    /// the field (§3, §13.5).
    /// </summary>
    public DateTimeOffset? MaintenanceUntil { get; init; }

    /// <summary>
    /// Reads a payload out of decoded UTF-8 JSON. Never throws.
    ///
    /// <para>⚠ <b>A successful parse means "well-formed", not "trustworthy".</b> Nothing here has been
    /// authenticated — the signature is checked by <see cref="LicenseVerifier"/> afterwards, over the
    /// encoded segment. The only fields a caller may act on before that are <see cref="KeyId"/> and
    /// <see cref="Version"/>, and only to decide which key to verify with and whether to refuse outright.</para>
    /// </summary>
    /// <param name="detail">
    /// The offending field name when parsing fails — a technical token for <c>[Copy details]</c>.
    /// ⛔ Never rendered as prose.
    /// </param>
    public static bool TryParse(
        byte[] utf8Json,
        [NotNullWhen(true)] out LicensePayload? payload,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);

        payload = null;
        detail = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json);
        }
        catch (JsonException)
        {
            detail = "json";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                detail = "root";
                return false;
            }

            if (!TryReadInt(root, FieldVersion, out var version, ref detail) ||
                !TryReadString(root, FieldKeyId, out var keyId, ref detail) ||
                !TryReadString(root, FieldAlgorithm, out var algorithmId, ref detail) ||
                !TryReadString(root, FieldLicenseId, out var licenseId, ref detail) ||
                !TryReadString(root, FieldProduct, out var product, ref detail) ||
                !TryReadString(root, FieldLicensee, out var licensee, ref detail) ||
                !TryReadInt(root, FieldSeats, out var seats, ref detail) ||
                !TryReadTimestamp(root, FieldIssuedAt, out var issuedAt, ref detail) ||
                !TryReadTimestamp(root, FieldNotBefore, out var notBefore, ref detail) ||
                !TryReadTimestamp(root, FieldExpiresAt, out var expiresAt, ref detail))
            {
                return false;
            }

            DateTimeOffset? maintenanceUntil = null;
            if (root.TryGetProperty(FieldMaintenanceUntil, out var maintenanceElement) &&
                maintenanceElement.ValueKind != JsonValueKind.Null)
            {
                if (!TryReadTimestamp(root, FieldMaintenanceUntil, out var maintenance, ref detail))
                {
                    return false;
                }

                maintenanceUntil = maintenance;
            }

            if (version < 1)
            {
                detail = FieldVersion;
                return false;
            }

            if (seats < 0)
            {
                detail = FieldSeats;
                return false;
            }

            payload = new LicensePayload
            {
                Version = version,
                KeyId = keyId,
                AlgorithmId = algorithmId,
                LicenseId = licenseId,
                Product = product,
                Licensee = licensee,
                Seats = seats,
                IssuedAt = issuedAt,
                NotBefore = notBefore,
                ExpiresAt = expiresAt,
                MaintenanceUntil = maintenanceUntil,
            };

            return true;
        }
    }

    /// <summary>
    /// Serialises this payload to the UTF-8 JSON that gets signed.
    ///
    /// <para>⚠ <b>For the ISSUER only.</b> A verifier never calls this: it authenticates the bytes it
    /// received, and re-serialising would reintroduce exactly the canonicalisation dependency the format
    /// was designed to avoid. It lives here so that field names and timestamp shape have one definition
    /// shared by EmberTern, the License Manager and the tests.</para>
    /// </summary>
    public byte[] WriteJson()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber(FieldVersion, Version);
            writer.WriteString(FieldKeyId, KeyId);
            writer.WriteString(FieldAlgorithm, AlgorithmId);
            writer.WriteString(FieldLicenseId, LicenseId);
            writer.WriteString(FieldProduct, Product);
            writer.WriteString(FieldLicensee, Licensee);
            writer.WriteNumber(FieldSeats, Seats);
            writer.WriteString(FieldIssuedAt, FormatTimestamp(IssuedAt));
            writer.WriteString(FieldNotBefore, FormatTimestamp(NotBefore));
            writer.WriteString(FieldExpiresAt, FormatTimestamp(ExpiresAt));
            if (MaintenanceUntil is { } maintenance)
            {
                writer.WriteString(FieldMaintenanceUntil, FormatTimestamp(maintenance));
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Renders a timestamp in the one accepted shape (<see cref="TimestampFormat"/>).</summary>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static bool TryReadString(
        JsonElement root, string name, out string value, ref string? detail)
    {
        value = string.Empty;

        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            detail = name;
            return false;
        }

        var text = element.GetString();
        if (string.IsNullOrEmpty(text))
        {
            detail = name;
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryReadInt(JsonElement root, string name, out int value, ref string? detail)
    {
        value = 0;

        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out value))
        {
            detail = name;
            return false;
        }

        return true;
    }

    private static bool TryReadTimestamp(
        JsonElement root, string name, out DateTimeOffset value, ref string? detail)
    {
        value = default;

        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            detail = name;
            return false;
        }

        var text = element.GetString();
        if (text is null ||
            !DateTimeOffset.TryParseExact(
                text,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out value))
        {
            detail = name;
            return false;
        }

        return true;
    }
}
