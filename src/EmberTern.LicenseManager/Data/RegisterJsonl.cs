using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EmberTern.LicenseManager.Data;

/// <summary>
/// The escape hatch (§29.1): the whole register as plain JSON Lines, one object per row.
///
/// <para>⭐⭐ <b>Its purpose is that the register outlives this application.</b> If the License Manager is
/// ever unbuildable, a <c>.jsonl</c> file is still readable with <c>cat</c>, greppable, and parseable by
/// anything. That is why it is text and not a second binary format, and why every non-ASCII character is
/// written as itself rather than as <c>\uXXXX</c> — a Polish customer name that reads as escape sequences
/// defeats the one thing this file exists for.</para>
///
/// <para>⛔ <b>It is an EXPORT, and there is deliberately no import.</b> §29.1 describes a file you can
/// read when nothing else works; restoring a register is what the encrypted backup is for, and it is the
/// path with a snapshot, an integrity gate and a refusal. ⛔ Do not add a JSONL restore — a second write
/// path into a register would be exactly the uncontrolled route the backup design exists to prevent.</para>
///
/// <para>⭐ <b>Five record types</b> (D‑2), because five tables carry data: <c>customer</c>,
/// <c>license</c>, <c>artifact</c>, <c>current-artifact</c>, <c>audit</c>. ⚠ <c>current-artifact</c> and
/// <c>audit</c> are not decoration — without the first, the file cannot say which artifact a customer
/// should be holding, and without the second the history is flattened to its outcome. §29.1 predates
/// schema v2 and lists three; the two additions are the tables v2 and the audit contract added.</para>
///
/// <para>⛔ There is no header record. The type discriminator is on every line, which is what makes the
/// file processable one line at a time by a tool that has never seen the rest of it.</para>
/// </summary>
public static class RegisterJsonl
{
    /// <summary>The extension the License Manager writes.</summary>
    public const string FileExtension = ".jsonl";

    /// <summary>The <c>type</c> value on a customer line.</summary>
    public const string CustomerType = "customer";

    /// <summary>The <c>type</c> value on a licence line.</summary>
    public const string LicenseType = "license";

    /// <summary>The <c>type</c> value on an issued-artifact line.</summary>
    public const string ArtifactType = "artifact";

    /// <summary>The <c>type</c> value on a current-artifact pointer line.</summary>
    public const string CurrentArtifactType = "current-artifact";

    /// <summary>The <c>type</c> value on a history line.</summary>
    public const string AuditType = "audit";

    /// <summary>Every type this export writes, in the order it writes them.</summary>
    public static IReadOnlyList<string> Types { get; } =
        [CustomerType, LicenseType, ArtifactType, CurrentArtifactType, AuditType];

    // ⚠ Relaxed escaping so that "Żółw Sp. z o.o." is written as itself. The encoder's "unsafe" name is
    //   about HTML contexts — it declines to escape < > & — and this file is never HTML. Escaping every
    //   Polish character into \uXXXX would produce a technically valid file that fails the only
    //   requirement it has: that a human can read it without this application.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    /// <summary>
    /// The whole register, one JSON object per line.
    ///
    /// <para>⚠ Every read behind this is UNLIMITED, deliberately. The list views cap their results, which
    /// is right for a list and would be a silently truncated export here.</para>
    /// </summary>
    public static IReadOnlyList<string> Export(LicenseRegister register)
    {
        ArgumentNullException.ThrowIfNull(register);

        var lines = new List<string>();

        foreach (var customer in register.GetCustomers())
        {
            lines.Add(Write(writer =>
            {
                writer.WriteString("type", CustomerType);
                writer.WriteString("customerId", customer.CustomerId);
                writer.WriteString("name", customer.Name);
                WriteOptional(writer, "address", customer.Address);
                WriteOptional(writer, "firstName", customer.FirstName);
                WriteOptional(writer, "lastName", customer.LastName);
                WriteOptional(writer, "email", customer.Email);
                WriteOptional(writer, "notes", customer.Notes);
                writer.WriteString("createdAt", Stamp(customer.CreatedAt));
                writer.WriteString("updatedAt", Stamp(customer.UpdatedAt));
            }));
        }

        foreach (var license in register.GetAllLicenses())
        {
            lines.Add(Write(writer =>
            {
                writer.WriteString("type", LicenseType);
                writer.WriteString("lid", license.LicenseId);
                writer.WriteString("customerId", license.CustomerId);
                writer.WriteString("product", license.Product);
                writer.WriteNumber("seats", license.Seats);
                writer.WriteString("notBefore", Stamp(license.NotBefore));
                writer.WriteString("expiresAt", Stamp(license.ExpiresAt));
                WriteOptional(writer, "maintUntil",
                    license.MaintenanceUntil is { } maint ? Stamp(maint) : null);
                writer.WriteString("status", license.Status);

                // ⚠⚠ RETIREMENT TRAVELS. This export is the register's escape hatch — the file somebody
                //    reads when the application will not open — and a licence that came back from it
                //    without this field would silently return to the ACTIVE register. Rule #11: the
                //    export may not know less than the database.
                WriteOptional(writer, "retiredAt",
                    license.RetiredAt is { } retired ? Stamp(retired) : null);

                WriteOptional(writer, "notes", license.Notes);
                writer.WriteString("createdAt", Stamp(license.CreatedAt));
                writer.WriteString("updatedAt", Stamp(license.UpdatedAt));
            }));
        }

        foreach (var artifact in register.GetAllArtifacts())
        {
            lines.Add(Write(writer =>
            {
                writer.WriteString("type", ArtifactType);
                writer.WriteNumber("artifactId", artifact.ArtifactId);
                writer.WriteString("lid", artifact.LicenseId);
                writer.WriteString("kid", artifact.KeyId);
                writer.WriteString("issuedAt", Stamp(artifact.IssuedAt));
                writer.WriteString("reason", artifact.Reason);
                // ⭐ The signed payload and the token in full, verbatim. This is the line that makes the
                //    export a real escape hatch: a licence can be handed back to a customer from it with
                //    nothing but a text editor, exactly as §12.5 promises of the register itself.
                writer.WriteString("payloadJson", artifact.PayloadJson);
                writer.WriteString("token", artifact.Token);
                // ⚠ PROJECTED, not stored — see ArtifactStatuses. It is written so a reader of this file
                //    alone can tell current from superseded, which is the same promise artifact_status
                //    makes to a SQL tool.
                WriteOptional(writer, "status", artifact.Status);
            }));
        }

        foreach (var pointer in register.GetCurrentArtifactPointers())
        {
            lines.Add(Write(writer =>
            {
                writer.WriteString("type", CurrentArtifactType);
                writer.WriteString("lid", pointer.LicenseId);
                writer.WriteNumber("artifactId", pointer.ArtifactId);
                writer.WriteString("setAt", Stamp(pointer.SetAt));
            }));
        }

        // ⚠ int.MaxValue rather than the query's default 200: an export that stops at the newest two
        //    hundred history lines is a file that looks complete and is not.
        var audit = register.GetAudit(new AuditQuery { Limit = int.MaxValue });
        for (var i = audit.Count - 1; i >= 0; i--)
        {
            // ⭐ Reversed into chronological order. GetAudit answers a list view, where newest-first is
            //    what an operator wants; a file someone reads top to bottom wants the story in order.
            var entry = audit[i];
            lines.Add(Write(writer =>
            {
                writer.WriteString("type", AuditType);
                writer.WriteNumber("auditId", entry.AuditId);
                writer.WriteString("at", Stamp(entry.At));
                writer.WriteString("actor", entry.Actor);
                writer.WriteString("action", entry.Action);
                writer.WriteString("targetType", entry.TargetType);
                writer.WriteString("targetId", entry.TargetId);
                WriteOptional(writer, "beforeJson", entry.BeforeJson);
                WriteOptional(writer, "afterJson", entry.AfterJson);
                WriteOptional(writer, "note", entry.Note);
            }));
        }

        return lines;
    }

    /// <summary>The export as one document, newline-separated, with a trailing newline.</summary>
    public static string ExportText(LicenseRegister register) =>
        string.Join("\n", Export(register)) + "\n";

    // ⚠ A null is written as JSON null rather than omitted. Omission would make "the customer has no
    //   e-mail" and "this exporter did not know about e-mail" the same line, and the second is what a
    //   version skew looks like.
    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static string Stamp(DateTimeOffset value) =>
        Licensing.LicensePayload.FormatTimestamp(value);

    private static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
