using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Metadata;

/// <summary>
/// Pure composition of a <b>portable object script</b> — the structure DDL of a single
/// object followed by its <c>COMMENT ON</c> statements. "Portable" = describes the OBJECT,
/// never the security environment: this layer emits no <c>GRANT</c>/<c>REVOKE</c>/role/user
/// DDL by construction (it only ever appends comments the caller supplies).
/// <para>
/// Zero I/O, zero Firebird types — the acquisition (structure DDL + descriptions from the
/// catalog) lives in <c>MetadataExportService</c>; this class only lays the pieces out and
/// picks the right <see cref="DdlGenerator"/> comment builder per kind.
/// </para>
/// </summary>
public static class PortableDdl
{
    /// <summary>
    /// The <c>COMMENT ON &lt;kind&gt; "name" IS '…'</c> statement for the object, or
    /// <c>null</c> when the description is null/blank. A portable export never emits
    /// <c>COMMENT … IS NULL</c> noise — a missing comment simply produces no statement.
    /// (Index/column comments are produced by their own <see cref="DdlGenerator"/> builders.)
    /// </summary>
    public static string? ObjectComment(MetadataObjectKind kind, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        return kind switch
        {
            MetadataObjectKind.Table or MetadataObjectKind.SystemTable => DdlGenerator.BuildCommentTable(name, description),
            MetadataObjectKind.View => DdlGenerator.BuildCommentView(name, description),
            MetadataObjectKind.Procedure => DdlGenerator.BuildCommentProcedure(name, description),
            MetadataObjectKind.Function => DdlGenerator.BuildCommentFunction(name, description),
            MetadataObjectKind.Trigger => DdlGenerator.BuildCommentTrigger(name, description),
            MetadataObjectKind.Package => DdlGenerator.BuildCommentPackage(name, description),
            MetadataObjectKind.Domain => DdlGenerator.BuildCommentDomain(name, description),
            MetadataObjectKind.Generator => DdlGenerator.BuildCommentSequence(name, description),
            MetadataObjectKind.Exception => DdlGenerator.BuildCommentException(name, description),
            MetadataObjectKind.Index => DdlGenerator.BuildCommentIndex(name, description),
            _ => null,
        };
    }

    /// <summary>
    /// Assembles the final script: <paramref name="structureDdl"/> first (kept verbatim — it
    /// may itself be multi-statement, e.g. a table's CREATE + ALTER ADD CONSTRAINT + CREATE
    /// INDEX), then each non-blank trailing statement (typically <c>COMMENT ON …</c>),
    /// separated by a blank line. Every appended block is terminated with a single <c>;</c>
    /// (added only when absent — never doubled). Null/blank entries are skipped.
    /// </summary>
    public static string Compose(string structureDdl, IEnumerable<string?>? trailingStatements = null)
    {
        var sb = new StringBuilder();
        AppendStatement(sb, structureDdl);
        if (trailingStatements is not null)
        {
            foreach (var s in trailingStatements)
                AppendStatement(sb, s);
        }
        return sb.ToString();
    }

    private static void AppendStatement(StringBuilder sb, string? statement)
    {
        if (string.IsNullOrWhiteSpace(statement)) return;
        var text = statement.TrimEnd();
        if (sb.Length > 0) sb.Append("\n\n");
        sb.Append(text);
        if (!text.EndsWith(";", System.StringComparison.Ordinal)) sb.Append(';');
    }
}
