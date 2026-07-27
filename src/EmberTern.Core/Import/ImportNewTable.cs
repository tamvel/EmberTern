using System;
using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Import;

/// <summary>
/// ⭐ <b>The one owner of "what does this column definition become".</b> Every downstream form of an
/// <see cref="ImportColumnDefinition"/> — the DDL previewed, the DDL executed, and the
/// <see cref="ColumnSpec"/> the converter and the preview validate against — is produced here, from the same
/// <see cref="DdlGenerator.FormatTypeOrDomain"/> call.
/// <para>
/// <b>Why that matters more than it looks:</b> §3.6 promises the converted preview shows exactly what will
/// reach the database. For an existing table that is easy — the catalog says what the column is. For a table
/// that does not exist yet, somebody has to say what its columns <em>will</em> be, and if the preview's idea of
/// the type and the <c>CREATE TABLE</c>'s idea came from two pieces of code, they would eventually disagree —
/// and the disagreement would show up as rows rejected by a table the module itself designed.
/// Here they are one string.
/// </para>
/// <para>
/// It creates no second column model and no second DDL generator (§4.6): <see cref="ImportColumnDefinition"/>
/// converts to the shared <see cref="FieldDefinition"/>, and <see cref="DdlGenerator"/> does the rest —
/// the same generator the New Table tab uses.
/// </para>
/// </summary>
public static class ImportNewTable
{
    /// <summary>The declared type as it will appear in the DDL — <c>VARCHAR(20)</c>, <c>NUMERIC(15,2)</c>,
    /// <c>BLOB SUB_TYPE 1</c>, <c>DATE</c>. Exactly the shape <see cref="ImportTargetType.Resolve(string)"/>
    /// reads, because it is the shape the catalog will report back after the table is created.</summary>
    public static string TypeText(ImportColumnDefinition column)
        => DdlGenerator.FormatTypeOrDomain(ToFieldDefinition(column));

    /// <summary>One import column as the shared field definition the DDL generator takes.</summary>
    public static FieldDefinition ToFieldDefinition(ImportColumnDefinition column)
    {
        if (column is null) throw new ArgumentNullException(nameof(column));

        var basicType = (column.BasicType ?? string.Empty).Trim().ToUpperInvariant();

        return new FieldDefinition
        {
            Name = column.Name,
            BasicType = basicType.Length == 0 ? "VARCHAR" : basicType,

            // ⚠ ImportColumnDefinition carries ONE Size, FieldDefinition splits it into Size (text length) and
            // Precision (numeric). That is not sloppiness on either side: they are the same "(n)" argument in
            // the DDL, and FieldTypeRules.UsesSize already treats them as one question. Feeding the right slot
            // is this method's whole job — which is why nothing else is allowed to do the conversion.
            Size = IsTextType(basicType) ? column.Size : null,
            Precision = IsExactNumericType(basicType) ? column.Size : null,
            Scale = IsExactNumericType(basicType) ? column.Scale : null,
            BlobSubType = basicType == "BLOB"
                ? (BlobSubType)(column.BlobSubType ?? 1)
                : null,

            NotNull = column.NotNull,
        };
    }

    /// <summary>The columns as a <see cref="TableSpec"/>. No primary key, no identity, no defaults: an import
    /// target is a place to put the file, and inventing constraints the source says nothing about would be the
    /// guessing §0 forbids. Everything on the grid is the user's to set.</summary>
    public static TableSpec BuildSpec(IReadOnlyList<ImportColumnDefinition> columns)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));

        var spec = new TableSpec();
        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name)) continue;
            spec.Fields.Add(ToFieldDefinition(column));
        }
        return spec;
    }

    /// <summary>The <c>CREATE TABLE</c> statement — from <see cref="DdlGenerator.BuildCreateTable(string, TableSpec)"/>,
    /// the same generator the New Table tab uses (§4.6: nothing new).</summary>
    public static string BuildCreateSql(string tableName, IReadOnlyList<ImportColumnDefinition> columns)
        => DdlGenerator.BuildCreateTable(tableName, BuildSpec(columns));

    /// <summary>
    /// The <c>DROP TABLE</c> statement for the clean-up offered when an import into a table this module created
    /// fails (§0.5). Quoted through the shared <see cref="DdlGenerator.Quote"/> so the name round-trips exactly
    /// as it was created.
    /// </summary>
    public static string BuildDropSql(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));

        return "DROP TABLE " + DdlGenerator.Quote(tableName.Trim());
    }

    /// <summary>
    /// ⭐ The table as it <em>will</em> be, expressed as an <see cref="ImportTarget"/> — so mapping, readiness,
    /// the converted preview and "Validate" all work on a new table before a single line of DDL has run.
    /// <para>
    /// That is the whole point of having it: <b>a dry run against a table that does not exist yet</b> tells the
    /// user whether the inferred types will actually hold their data — <em>while the decision is still
    /// reversible</em>. Once the <c>CREATE</c> has run it is committed and a Rollback cannot take it back
    /// (§0.5 / gotcha #213), so the one moment this answer is worth anything is before that.
    /// </para>
    /// <para>
    /// After a real <c>CREATE</c> the coordinator re-reads the target from the CATALOG instead of keeping this
    /// projection: the writer must work against what Firebird actually built, not against what we asked for.
    /// The projection is a prediction; the catalog is the fact.
    /// </para>
    /// </summary>
    public static ImportTarget Project(string tableName, IReadOnlyList<ImportColumnDefinition> columns)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));

        var specs = new List<ColumnSpec>(columns.Count);
        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name)) continue;
            specs.Add(new ColumnSpec(column.Name.Trim(), TypeText(column), null, column.NotNull));
        }

        // A brand-new table has no triggers by construction, which is worth stating rather than leaving the
        // reader to wonder: the BEFORE INSERT warning (R6) is about a table that already had a life.
        return new ImportTarget(tableName ?? string.Empty, specs, Array.Empty<string>());
    }

    /// <summary>Text types take a length; the rest do not.</summary>
    private static bool IsTextType(string basicType)
        => basicType is "CHAR" or "VARCHAR" or "CSTRING";

    /// <summary>Exact numerics take a precision and a scale.</summary>
    private static bool IsExactNumericType(string basicType)
        => basicType is "NUMERIC" or "DECIMAL";
}
