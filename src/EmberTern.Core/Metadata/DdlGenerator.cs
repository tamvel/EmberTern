using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EmberTern.Core.Metadata;

/// <summary>
/// Target shape for <see cref="DdlGenerator.BuildAlterStatements"/>. The
/// caller fills this with the *desired* end state of a column; the diff
/// against the live <see cref="FieldInfo"/> produces the minimum-set of
/// ALTER statements. Properties unset (<see cref="TypeClause"/> null,
/// <see cref="Description"/> null, etc.) mean "leave unchanged" — only set
/// what the user actually edited.
/// </summary>
public sealed class AlterFieldTarget
{
    /// <summary>New column name. Set even when the user didn't rename — the
    /// diff compares against the original; identical values produce no rename
    /// statement.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Pre-formatted type clause ready for <c>ALTER COLUMN TYPE …</c>
    /// (e.g. <c>"VARCHAR(50)"</c>, <c>"INTEGER"</c>, <c>"DOMAIN_NAME"</c>).
    /// Null means "no type change". Dialog edit calls
    /// <see cref="DdlGenerator.FormatTypeOrDomain"/> on a
    /// <see cref="FieldDefinition"/>; inline edit picks the user-typed
    /// string OR the selected domain.</summary>
    public string? TypeClause { get; set; }

    public bool NotNull { get; set; }

    /// <summary>Default expression (empty/whitespace → DROP DEFAULT). Treated
    /// as null and empty equivalent in the diff.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>COMMENT ON COLUMN value. Null and empty are equivalent in the
    /// diff; emit IS NULL when the user cleared a previously-present comment.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Pure DDL emitter. Every output is a fragment of standard Firebird SQL —
/// identifiers are always quoted with <c>"</c>, internal quotes doubled, so
/// names with lowercase letters or reserved words round-trip safely.
/// </summary>
/// <remarks>
/// No I/O, no FbConnection. <c>TableDetailTabViewModel</c> calls these to
/// build <see cref="PendingDdlChange"/> entries; tests cover the shape of the
/// generated SQL directly.
/// </remarks>
public static class DdlGenerator
{
    public static string Quote(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return "\"\"";
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Minimal CREATE TABLE skeleton — single INTEGER NOT NULL PRIMARY KEY column
    /// named ID. The "+ New Table" button emits this; further structure is added
    /// via the Pola sub-tab edit toolbar.
    /// </summary>
    public static string BuildCreateTable(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));

        return $"CREATE TABLE {Quote(tableName.Trim())} (\n  ID INTEGER NOT NULL PRIMARY KEY\n)";
    }

    /// <summary>
    /// <c>DROP TABLE …</c>. Caller is responsible for confirming the
    /// destructive intent. EmberTern never auto-drops dependents — if Firebird
    /// rejects the drop because of a dependency, that error surfaces to the user.
    /// </summary>
    public static string BuildDropTable(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        return $"DROP TABLE {Quote(tableName.Trim())}";
    }

    /// <summary><c>DROP VIEW …</c>. Caller confirms the destructive intent.</summary>
    public static string BuildDropView(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("View name is required.", nameof(name));
        return $"DROP VIEW {Quote(name.Trim())}";
    }

    /// <summary><c>DROP PROCEDURE …</c>.</summary>
    public static string BuildDropProcedure(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure name is required.", nameof(name));
        return $"DROP PROCEDURE {Quote(name.Trim())}";
    }

    /// <summary><c>DROP TRIGGER …</c>.</summary>
    public static string BuildDropTrigger(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Trigger name is required.", nameof(name));
        return $"DROP TRIGGER {Quote(name.Trim())}";
    }

    /// <summary><c>DROP FUNCTION …</c>.</summary>
    public static string BuildDropFunction(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Function name is required.", nameof(name));
        return $"DROP FUNCTION {Quote(name.Trim())}";
    }

    /// <summary>
    /// Dispatches to the right DROP builder for a schema object kind, so the tree's
    /// single generic Delete path has one entry point. Security kinds (Role/User) go
    /// through the Security Manager instead; SystemTable has no drop path.
    /// </summary>
    public static string BuildDrop(MetadataObjectKind kind, string name) => kind switch
    {
        MetadataObjectKind.Table => BuildDropTable(name),
        MetadataObjectKind.View => BuildDropView(name),
        MetadataObjectKind.Procedure => BuildDropProcedure(name),
        MetadataObjectKind.Trigger => BuildDropTrigger(name),
        MetadataObjectKind.Function => BuildDropFunction(name),
        MetadataObjectKind.Package => BuildDropPackage(name),
        MetadataObjectKind.Generator => BuildDropSequence(name),
        MetadataObjectKind.Domain => BuildDropDomain(name),
        MetadataObjectKind.Exception => BuildDropException(name),
        MetadataObjectKind.Index => BuildDropIndex(name),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No tree Delete path for this object kind."),
    };

    /// <summary>
    /// <c>ALTER TABLE … DROP …</c>. Caller is responsible for confirming the
    /// destructive intent.
    /// </summary>
    public static string BuildDropField(string tableName, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required.", nameof(fieldName));

        return $"ALTER TABLE {Quote(tableName.Trim())} DROP {Quote(fieldName.Trim())}";
    }

    /// <summary>
    /// <c>ALTER TABLE … ALTER … POSITION n</c>. Firebird positions are 1-based.
    /// </summary>
    public static string BuildMoveField(string tableName, string fieldName, int oneBasedPosition)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        if (oneBasedPosition < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedPosition), "Position must be >= 1.");

        return string.Format(
            CultureInfo.InvariantCulture,
            "ALTER TABLE {0} ALTER {1} POSITION {2}",
            Quote(tableName.Trim()), Quote(fieldName.Trim()), oneBasedPosition);
    }

    /// <summary>
    /// Builds the full ADD-FIELD DDL. May return multiple top-level statements
    /// joined by ';' when autoincrement-by-generator is requested — the executor
    /// splits on top-level semicolons (no string literals to worry about for
    /// these statements).
    /// </summary>
    public static string BuildAddField(string tableName, FieldDefinition def)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (def is null) throw new ArgumentNullException(nameof(def));
        if (string.IsNullOrWhiteSpace(def.Name))
            throw new ArgumentException("Field name is required.", nameof(def));

        var table = Quote(tableName.Trim());
        var name = Quote(def.Name.Trim());

        // A computed column derives EVERYTHING from its expression. Firebird
        // rejects DEFAULT / NOT NULL / CHECK / PRIMARY KEY / IDENTITY / a backing
        // generator on a COMPUTED BY column, so when it's set we emit ONLY the
        // COMPUTED BY clause and skip all the other clauses below (#2).
        var isComputed = !string.IsNullOrWhiteSpace(def.ComputedExpression);

        var column = new StringBuilder();
        column.Append("ALTER TABLE ").Append(table).Append(" ADD ").Append(name).Append(' ');

        if (isComputed)
        {
            column.Append("COMPUTED BY (").Append(def.ComputedExpression!.Trim()).Append(')');
        }
        else
        {
            column.Append(FormatTypeOrDomain(def));

            if (!string.IsNullOrWhiteSpace(def.DefaultValue))
            {
                column.Append(" DEFAULT ").Append(def.DefaultValue.Trim());
            }

            if (def.NotNull)
            {
                column.Append(" NOT NULL");
            }

            if (def.AutoIncrement == AutoIncrementMode.Identity)
            {
                // FB3+ syntax — preferred for INTEGER PKs going forward.
                column.Append(" GENERATED BY DEFAULT AS IDENTITY");
            }

            if (!string.IsNullOrWhiteSpace(def.CheckExpression))
            {
                column.Append(" CHECK (").Append(def.CheckExpression.Trim()).Append(')');
            }

            if (def.PrimaryKey)
            {
                column.Append(" PRIMARY KEY");
            }
        }

        var statements = new StringBuilder();
        statements.Append(column);

        // Autoincrement via generator: emits CREATE GENERATOR + CREATE TRIGGER
        // alongside the column add. Identity mode is inline (above) so no extra
        // statements. Skipped entirely for computed columns.
        if (!isComputed && def.AutoIncrement == AutoIncrementMode.NewGenerator)
        {
            var genName = (def.GeneratorName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(genName))
            {
                // Sensible default if the user didn't pick a name.
                genName = "GEN_" + tableName.Trim().ToUpperInvariant() + "_" + def.Name.Trim().ToUpperInvariant();
            }
            statements.Append(';');
            statements.Append('\n');
            statements.Append("CREATE GENERATOR ").Append(Quote(genName));

            statements.Append(';');
            statements.Append('\n');
            statements.Append(BuildAutoIncTrigger(tableName.Trim(), def.Name.Trim(), genName, def.TriggerName));
        }
        else if (!isComputed && def.AutoIncrement == AutoIncrementMode.ExistingGenerator)
        {
            var genName = (def.GeneratorName ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(genName))
            {
                statements.Append(';');
                statements.Append('\n');
                statements.Append(BuildAutoIncTrigger(tableName.Trim(), def.Name.Trim(), genName, def.TriggerName));
            }
        }

        return statements.ToString();
    }

    /// <summary>
    /// BEFORE INSERT trigger that assigns NEW."FIELD" = GEN_ID("GEN", 1) when
    /// the inserter leaves it NULL. Body is wire-formatted with the FB-standard
    /// SET TERM gymnastics absent — EmberTern uses isql-friendly statement
    /// boundaries (single semicolons; the FbCommand path doesn't need SET TERM).
    /// </summary>
    public static string BuildAutoIncTrigger(string tableName, string fieldName, string generatorName, string? triggerName)
    {
        var trigger = (triggerName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trigger))
        {
            trigger = "BI_" + tableName.ToUpperInvariant() + "_" + fieldName.ToUpperInvariant();
        }

        var sb = new StringBuilder();
        sb.Append("CREATE TRIGGER ").Append(Quote(trigger))
          .Append(" FOR ").Append(Quote(tableName))
          .Append(" ACTIVE BEFORE INSERT POSITION 0\n")
          .Append("AS BEGIN\n")
          .Append("  IF (NEW.").Append(Quote(fieldName)).Append(" IS NULL) THEN\n")
          .Append("    NEW.").Append(Quote(fieldName))
          .Append(" = GEN_ID(").Append(Quote(generatorName)).Append(", 1);\n")
          .Append("END");
        return sb.ToString();
    }

    /// <summary>
    /// Renders the type part of an ADD FIELD: either <c>DOMAIN "NAME"</c> or
    /// the basic SQL type with the right modifiers. Falls back to INTEGER if
    /// nothing has been picked yet — keeps the live DDL preview meaningful
    /// even while the user is still filling in the dialog.
    /// </summary>
    public static string FormatTypeOrDomain(FieldDefinition def)
    {
        if (!string.IsNullOrWhiteSpace(def.Domain))
        {
            // Firebird syntax: ADD <name> <DOMAIN_NAME> — no DOMAIN keyword.
            return Quote(def.Domain!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(def.TypeOf))
        {
            // "COLUMN TABLE.COL" → "TYPE OF COLUMN TABLE.COL".
            return "TYPE OF " + def.TypeOf!.Trim();
        }

        var type = (def.BasicType ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(type)) type = "INTEGER";

        return type switch
        {
            "CHAR" or "VARCHAR" or "CSTRING"
                => def.Size is { } size ? $"{type}({size.ToString(CultureInfo.InvariantCulture)})" : type,
            "NUMERIC" or "DECIMAL"
                => FormatNumericType(type, def.Precision, def.Scale),
            "BLOB"
                => def.BlobSubType is { } sub
                    ? $"BLOB SUB_TYPE {((int)sub).ToString(CultureInfo.InvariantCulture)}"
                    : "BLOB",
            _ => type,
        };
    }

    private static string FormatNumericType(string type, int? precision, int? scale)
    {
        if (precision is null) return type;
        if (scale is null)
        {
            return $"{type}({precision.Value.ToString(CultureInfo.InvariantCulture)})";
        }
        return $"{type}({precision.Value.ToString(CultureInfo.InvariantCulture)},{scale.Value.ToString(CultureInfo.InvariantCulture)})";
    }

    // ─── Full CREATE TABLE (used by CreateTableDialog) ─────────────────────

    /// <summary>
    /// Renders a complete <c>CREATE TABLE</c> from a <see cref="TableSpec"/>:
    /// persistent or global temporary; column list; inline PRIMARY KEY
    /// constraint when one or more fields are flagged as PK; per-field
    /// autoincrement (CREATE SEQUENCE + CREATE TRIGGER appended after the
    /// CREATE TABLE); optional COMMENT ON TABLE for the description; per-field
    /// COMMENT ON COLUMN for any field that carries one.
    ///
    /// Multiple statements are joined by <c>;</c> — the executor
    /// (<see cref="FirebirdDdlExecutor.SplitStatements"/>) tracks BEGIN/END
    /// nesting so trigger bodies stay intact.
    /// </summary>
    public static string BuildCreateTable(string tableName, TableSpec spec)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var table = tableName.Trim();
        var qTable = Quote(table);

        var sb = new StringBuilder();
        sb.Append(spec.Kind switch
        {
            TableKind.TempDeleteRows or TableKind.TempPreserveRows => "CREATE GLOBAL TEMPORARY TABLE ",
            _ => "CREATE TABLE ",
        });
        sb.Append(qTable).Append(" (\n");

        var pkColumns = new List<string>();
        for (int i = 0; i < spec.Fields.Count; i++)
        {
            var field = spec.Fields[i];
            if (string.IsNullOrWhiteSpace(field.Name)) continue;
            if (i > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');

            sb.Append("  ").Append(Quote(field.Name.Trim())).Append(' ');

            // Computed columns derive everything from the expression — no
            // DEFAULT / NOT NULL / CHECK / IDENTITY / PK (Firebird rejects them
            // on a COMPUTED BY column). Emit only the COMPUTED BY clause (#2).
            var fieldIsComputed = !string.IsNullOrWhiteSpace(field.ComputedExpression);
            if (fieldIsComputed)
            {
                sb.Append("COMPUTED BY (").Append(field.ComputedExpression!.Trim()).Append(')');
            }
            else
            {
                sb.Append(FormatTypeOrDomain(field));

                if (!string.IsNullOrWhiteSpace(field.DefaultValue))
                {
                    sb.Append(" DEFAULT ").Append(field.DefaultValue.Trim());
                }
                if (field.NotNull)
                {
                    sb.Append(" NOT NULL");
                }
                if (field.AutoIncrement == AutoIncrementMode.Identity)
                {
                    sb.Append(" GENERATED BY DEFAULT AS IDENTITY");
                }
                if (!string.IsNullOrWhiteSpace(field.CheckExpression))
                {
                    sb.Append(" CHECK (").Append(field.CheckExpression.Trim()).Append(')');
                }
            }

            if (i < spec.Fields.Count - 1 || HasPkColumns(spec)) sb.Append(',');
            sb.Append('\n');

            if (field.PrimaryKey && !fieldIsComputed) pkColumns.Add(field.Name.Trim());
        }

        if (pkColumns.Count > 0)
        {
            // Named PK so the constraint is addressable later (drop/rename).
            sb.Append("  CONSTRAINT ").Append(Quote("PK_" + table.ToUpperInvariant()))
              .Append(" PRIMARY KEY (");
            for (int i = 0; i < pkColumns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Quote(pkColumns[i]));
            }
            sb.Append(")\n");
        }

        sb.Append(')');

        if (spec.Kind == TableKind.TempDeleteRows)
        {
            sb.Append("\nON COMMIT DELETE ROWS");
        }
        else if (spec.Kind == TableKind.TempPreserveRows)
        {
            sb.Append("\nON COMMIT PRESERVE ROWS");
        }

        // Per-field autoincrement via legacy generator + trigger pattern.
        // Each generates two extra statements after the CREATE TABLE.
        foreach (var field in spec.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name)) continue;
            if (field.AutoIncrement != AutoIncrementMode.NewGenerator) continue;
            // Computed columns can't be autoincremented — skip their generator/trigger.
            if (!string.IsNullOrWhiteSpace(field.ComputedExpression)) continue;

            var fieldName = field.Name.Trim();
            var genName = (field.GeneratorName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(genName))
            {
                genName = "GEN_" + table.ToUpperInvariant() + "_" + fieldName.ToUpperInvariant();
            }
            var triggerName = (field.TriggerName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(triggerName))
            {
                triggerName = table.ToUpperInvariant() + "_BI_" + fieldName.ToUpperInvariant();
            }

            sb.Append(";\nCREATE SEQUENCE ").Append(Quote(genName));
            sb.Append(";\nCREATE TRIGGER ").Append(Quote(triggerName))
              .Append(" FOR ").Append(qTable)
              .Append(" ACTIVE BEFORE INSERT POSITION 0\n")
              .Append("AS BEGIN\n")
              .Append("  IF (NEW.").Append(Quote(fieldName)).Append(" IS NULL) THEN\n")
              .Append("    NEW.").Append(Quote(fieldName))
              .Append(" = NEXT VALUE FOR ").Append(Quote(genName)).Append(";\n")
              .Append("END");
        }

        // Optional COMMENT ON TABLE for the table-level description.
        if (!string.IsNullOrWhiteSpace(spec.Description))
        {
            sb.Append(";\n").Append(BuildCommentTable(table, spec.Description));
        }

        // Per-field column comments.
        foreach (var field in spec.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name)) continue;
            if (string.IsNullOrWhiteSpace(field.Description)) continue;
            sb.Append(";\n").Append(BuildCommentColumn(table, field.Name.Trim(), field.Description));
        }

        return sb.ToString();
    }

    private static bool HasPkColumns(TableSpec spec)
    {
        foreach (var f in spec.Fields)
        {
            // Must match the pkColumns collection above — computed columns are
            // never part of the PK, so they don't count toward the trailing comma.
            if (f.PrimaryKey && string.IsNullOrWhiteSpace(f.ComputedExpression)) return true;
        }
        return false;
    }

    // ─── ALTER COLUMN — used by the inline editing path on the Pola grid ───

    public static string BuildRenameField(string tableName, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(oldName))
            throw new ArgumentException("Old field name is required.", nameof(oldName));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("New field name is required.", nameof(newName));

        return string.Format(
            CultureInfo.InvariantCulture,
            "ALTER TABLE {0} ALTER {1} TO {2}",
            Quote(tableName.Trim()), Quote(oldName.Trim()), Quote(newName.Trim()));
    }

    /// <summary>Toggles a column's NOT NULL constraint. FB3+ syntax.</summary>
    public static string BuildSetNotNull(string tableName, string fieldName, bool notNull)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required.", nameof(fieldName));

        return string.Format(
            CultureInfo.InvariantCulture,
            "ALTER TABLE {0} ALTER {1} {2} NOT NULL",
            Quote(tableName.Trim()), Quote(fieldName.Trim()), notNull ? "SET" : "DROP");
    }

    /// <summary>Sets a column default. Pass null/whitespace as
    /// <paramref name="defaultExpression"/> to DROP DEFAULT.</summary>
    public static string BuildSetDefault(string tableName, string fieldName, string? defaultExpression)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required.", nameof(fieldName));

        if (string.IsNullOrWhiteSpace(defaultExpression))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "ALTER TABLE {0} ALTER {1} DROP DEFAULT",
                Quote(tableName.Trim()), Quote(fieldName.Trim()));
        }
        return string.Format(
            CultureInfo.InvariantCulture,
            "ALTER TABLE {0} ALTER {1} SET DEFAULT {2}",
            Quote(tableName.Trim()), Quote(fieldName.Trim()), defaultExpression.Trim());
    }

    /// <summary>Changes a column's type. Caller is responsible for ensuring
    /// the new type is compatible — FB rejects most cross-family conversions
    /// at the engine level (the resulting <c>FbException</c> surfaces as a
    /// Compile failure with the server's own message).</summary>
    public static string BuildAlterType(string tableName, string fieldName, string newType)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        if (string.IsNullOrWhiteSpace(newType))
            throw new ArgumentException("New type is required.", nameof(newType));

        return string.Format(
            CultureInfo.InvariantCulture,
            "ALTER TABLE {0} ALTER {1} TYPE {2}",
            Quote(tableName.Trim()), Quote(fieldName.Trim()), newType.Trim());
    }

    /// <summary>Emits <c>COMMENT ON COLUMN "T"."F" IS '...'</c> — single
    /// quotes inside the comment are doubled per SQL string-literal rules.
    /// Pass null/whitespace to clear the comment (<c>IS NULL</c>).</summary>
    public static string BuildCommentColumn(string tableName, string fieldName, string? comment)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required.", nameof(fieldName));

        var t = Quote(tableName.Trim());
        var f = Quote(fieldName.Trim());
        if (string.IsNullOrWhiteSpace(comment))
        {
            return string.Format(CultureInfo.InvariantCulture,
                "COMMENT ON COLUMN {0}.{1} IS NULL", t, f);
        }
        return string.Format(CultureInfo.InvariantCulture,
            "COMMENT ON COLUMN {0}.{1} IS '{2}'", t, f, EscapeSqlLiteral(comment));
    }

    /// <summary>Like <see cref="BuildCommentColumn"/> but for tables.</summary>
    public static string BuildCommentTable(string tableName, string? comment)
        => BuildRelationComment("TABLE", tableName, comment);

    /// <summary>Like <see cref="BuildCommentTable"/> but for views — Firebird
    /// requires the dedicated <c>COMMENT ON VIEW</c> form (a view is not a table
    /// to the COMMENT statement).</summary>
    public static string BuildCommentView(string viewName, string? comment)
        => BuildRelationComment("VIEW", viewName, comment);

    /// <summary>Like <see cref="BuildCommentTable"/> but for stored procedures —
    /// Firebird's <c>COMMENT ON PROCEDURE</c> form. (The shared helper is named
    /// "RelationComment" for historical reasons; a procedure isn't a relation,
    /// but the COMMENT statement shape is identical.)</summary>
    public static string BuildCommentProcedure(string procedureName, string? comment)
        => BuildRelationComment("PROCEDURE", procedureName, comment);

    /// <summary>Like <see cref="BuildCommentProcedure"/> but for functions —
    /// Firebird's <c>COMMENT ON FUNCTION</c> form (FB3+).</summary>
    public static string BuildCommentFunction(string functionName, string? comment)
        => BuildRelationComment("FUNCTION", functionName, comment);

    /// <summary>Firebird's <c>COMMENT ON PACKAGE</c> form (FB3+).</summary>
    public static string BuildCommentPackage(string packageName, string? comment)
        => BuildRelationComment("PACKAGE", packageName, comment);

    // ─── Package header / body reconstruction ──────────────────────────────
    //
    // A package has TWO source artifacts: the header (declarations) in
    // RDB$PACKAGES.RDB$PACKAGE_HEADER_SOURCE and the body (implementation) in
    // RDB$PACKAGE_BODY_SOURCE. Each stored BLOB is the text after AS (analogous
    // to RDB$PROCEDURE_SOURCE — gotcha #114); the reader strips any leading AS
    // (gotcha #139) before calling these so reconstruction is robust regardless
    // of the stored prefix. Names quoted only when needed (QuoteLight), matching
    // the fetched-source form of the other source editors.

    /// <summary>Editable header statement for the Package tab —
    /// <c>CREATE OR ALTER PACKAGE name AS &lt;headerSource&gt;</c> (recompilable in place).</summary>
    public static string BuildCreateOrAlterPackageHeader(string name, string headerSource)
        => BuildPackageStatement("CREATE OR ALTER PACKAGE ", name, headerSource);

    /// <summary>Editable body statement for the Body tab —
    /// <c>RECREATE PACKAGE BODY name AS &lt;bodySource&gt;</c> (Firebird has no
    /// CREATE OR ALTER PACKAGE BODY; RECREATE is idempotent whether or not a body exists).</summary>
    public static string BuildRecreatePackageBody(string name, string bodySource)
        => BuildPackageStatement("RECREATE PACKAGE BODY ", name, bodySource);

    private static string BuildPackageStatement(string verb, string name, string source)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Package name is required.", nameof(name));
        // Uniform '\n' (not AppendLine's platform newline) so the reconstructed source
        // doesn't mix \r\n wrapper lines with the \n-delimited body.
        var sb = new StringBuilder();
        sb.Append(verb).Append(QuoteLight(name.Trim())).Append('\n');
        sb.Append("AS\n");
        sb.Append(string.IsNullOrWhiteSpace(source) ? "BEGIN\nEND" : source.Trim());
        return sb.ToString();
    }

    /// <summary>Read-only combined DDL for the DDL tab — <c>CREATE PACKAGE … AS …</c>
    /// followed (when a body exists) by <c>RECREATE PACKAGE BODY … AS …</c>.</summary>
    public static string BuildPackageDdl(string name, string headerSource, string? bodySource)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Package name is required.", nameof(name));
        var sb = new StringBuilder();
        sb.Append(BuildPackageStatement("CREATE PACKAGE ", name, headerSource));
        if (!string.IsNullOrWhiteSpace(bodySource))
        {
            sb.Append("\n\n");
            sb.Append(BuildPackageStatement("RECREATE PACKAGE BODY ", name, bodySource!));
        }
        return sb.ToString();
    }

    /// <summary><c>DROP PACKAGE name</c> — Firebird drops the header and its body together.</summary>
    public static string BuildDropPackage(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Package name is required.", nameof(name));
        return "DROP PACKAGE " + Quote(name.Trim());
    }

    /// <summary>
    /// Reassembles a <c>CREATE [OR ALTER] VIEW</c> from the View Detail Easy-mode
    /// parts — the inverse of <see cref="Sql.ViewSignatureParser"/>. The verb is
    /// preserved (<paramref name="orAlter"/>) and the body emitted verbatim, so
    /// Source → Easy → Source keeps the original shape. An empty
    /// <paramref name="columns"/> list omits the <c>(...)</c> clause (so a view
    /// authored without an explicit column list round-trips unchanged); a non-empty
    /// list re-emits it. Names are quoted only when needed (<see cref="QuoteLight"/>),
    /// matching the fetched-source form.
    /// </summary>
    public static string BuildCreateOrAlterView(
        string name,
        IReadOnlyList<string> columns,
        string body,
        bool orAlter)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("View name is required.", nameof(name));

        var cols = new List<string>();
        if (columns is not null)
        {
            foreach (var c in columns)
                if (!string.IsNullOrWhiteSpace(c)) cols.Add(c.Trim());
        }

        var sb = new StringBuilder();
        sb.Append(orAlter ? "CREATE OR ALTER VIEW " : "CREATE VIEW ").Append(QuoteLight(name.Trim()));

        if (cols.Count > 0)
        {
            sb.AppendLine(" (");
            for (int k = 0; k < cols.Count; k++)
            {
                sb.Append("  ").Append(QuoteLight(cols[k]));
                if (k < cols.Count - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine(")");
        }
        else
        {
            sb.AppendLine();
        }

        sb.AppendLine("AS");
        sb.Append(body?.Trim() ?? string.Empty);
        return sb.ToString();
    }

    /// <summary>
    /// Reassembles a full <c>CREATE OR ALTER PROCEDURE</c> from the editable parts
    /// (Procedure Detail Easy mode). Deterministic — the inverse of
    /// <see cref="Sql.ProcedureSignatureParser"/>. Parameter type text and the body
    /// are emitted verbatim; names are quoted. Output params never carry a default.
    /// </summary>
    public static string BuildCreateOrAlterProcedure(
        string name,
        IReadOnlyList<Sql.ProcedureParameter> inputs,
        IReadOnlyList<Sql.ProcedureParameter> outputs,
        string body)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure name is required.", nameof(name));

        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER PROCEDURE ").Append(QuoteLight(name.Trim())).AppendLine();

        if (inputs is { Count: > 0 })
        {
            sb.AppendLine("(");
            AppendProcedureParamLines(sb, inputs, includeDefault: true);
            sb.AppendLine(")");
        }

        if (outputs is { Count: > 0 })
        {
            sb.AppendLine("RETURNS");
            sb.AppendLine("(");
            AppendProcedureParamLines(sb, outputs, includeDefault: false);
            sb.AppendLine(")");
        }

        sb.AppendLine("AS");
        sb.Append(string.IsNullOrWhiteSpace(body) ? "BEGIN\nEND" : body.Trim());
        return sb.ToString();
    }

    /// <summary>
    /// Reassembles a full <c>CREATE OR ALTER FUNCTION</c> from the editable parts
    /// (Function Detail Easy mode). Deterministic — the inverse of
    /// <see cref="Sql.FunctionSignatureParser"/>. A function returns a single value, so
    /// the result is one <c>RETURNS &lt;type&gt;</c> line (not a param block); the type
    /// text and the body are emitted verbatim. Argument lines reuse the procedure-param
    /// emitter. <paramref name="deterministic"/> appends the <c>DETERMINISTIC</c> keyword.
    /// </summary>
    public static string BuildCreateOrAlterFunction(
        string name,
        IReadOnlyList<Sql.ProcedureParameter> arguments,
        string returnType,
        bool deterministic,
        string body)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Function name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(returnType))
            throw new ArgumentException("Function return type is required.", nameof(returnType));

        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER FUNCTION ").Append(QuoteLight(name.Trim())).AppendLine();

        if (arguments is { Count: > 0 })
        {
            sb.AppendLine("(");
            AppendProcedureParamLines(sb, arguments, includeDefault: true);
            sb.AppendLine(")");
        }

        sb.Append("RETURNS ").Append(returnType.Trim());
        if (deterministic) sb.Append(" DETERMINISTIC");
        sb.AppendLine();

        sb.AppendLine("AS");
        sb.Append(string.IsNullOrWhiteSpace(body) ? "BEGIN\nEND" : body.Trim());
        return sb.ToString();
    }

    /// <summary>
    /// Regenerates a procedure body (the text after <c>AS</c>) from the structured
    /// <see cref="Sql.ProcedureBodyModel"/>: the DECLARE section (variables, then
    /// cursors, then subprograms) followed by the executable <c>BEGIN…END</c> block.
    /// Deterministic inverse of <see cref="Sql.ProcedureBodySplitter.Split"/> —
    /// variables emit a canonical <c>DECLARE VARIABLE</c> form; cursor and subprogram
    /// declarations are emitted verbatim (already full <c>DECLARE …;</c> statements).
    /// </summary>
    public static string BuildProcedureBody(Sql.ProcedureBodyModel model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        var sb = new StringBuilder();

        foreach (var v in model.Variables)
        {
            if (string.IsNullOrWhiteSpace(v.Name)) continue;
            sb.Append("DECLARE VARIABLE ").Append(QuoteLight(v.Name.Trim()))
              .Append(' ').Append((v.TypeText ?? string.Empty).Trim());
            if (v.NotNull) sb.Append(" NOT NULL");
            if (!string.IsNullOrWhiteSpace(v.Default))
                sb.Append(" = ").Append(v.Default!.Trim());
            sb.Append(';').Append('\n');
        }

        foreach (var c in model.Cursors)
        {
            var decl = (c.Declaration ?? string.Empty).Trim();
            if (decl.Length == 0) continue;
            if (!decl.EndsWith(";", StringComparison.Ordinal)) decl += ";";
            sb.Append(decl).Append('\n');
        }

        foreach (var sp in model.Subprograms)
        {
            var decl = (sp.Declaration ?? string.Empty).Trim();
            if (decl.Length == 0) continue;
            sb.Append(decl).Append('\n');
        }

        // No blank separator line between the DECLARE section and BEGIN — the
        // declarations flow directly into BEGIN (IBExpert style).
        var execBody = (model.ExecutableBody ?? string.Empty).Trim();
        sb.Append(execBody.Length == 0 ? "BEGIN\nEND" : execBody);
        return sb.ToString();
    }

    // ─── Trigger generation ────────────────────────────────────────────────

    /// <summary>
    /// Reassembles a full <c>CREATE OR ALTER TRIGGER</c> from the Trigger Detail
    /// Easy-mode parts (header metadata + body) — the deterministic inverse of
    /// <see cref="Sql.TriggerSignatureParser"/>. The events are emitted in a fixed
    /// INSERT/UPDATE/DELETE order joined by <c>OR</c>; <c>ACTIVE</c>/<c>INACTIVE</c>
    /// and <c>POSITION</c> are always written (clearer than relying on defaults).
    /// The body is emitted verbatim, so Easy → Source keeps it byte-for-byte. Names
    /// are quoted only when needed (<see cref="QuoteLight"/>), matching the fetched
    /// source form.
    /// </summary>
    public static string BuildCreateOrAlterTrigger(
        string name, string table, bool isBefore,
        bool insert, bool update, bool delete,
        int position, bool active, string body)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Trigger name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(table))
            throw new ArgumentException("Trigger table is required.", nameof(table));
        if (!(insert || update || delete))
            throw new ArgumentException("At least one trigger event (INSERT/UPDATE/DELETE) is required.", nameof(insert));

        var events = new List<string>();
        if (insert) events.Add("INSERT");
        if (update) events.Add("UPDATE");
        if (delete) events.Add("DELETE");

        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER TRIGGER ").Append(QuoteLight(name.Trim()))
          .Append(" FOR ").Append(QuoteLight(table.Trim())).AppendLine();
        sb.Append(active ? "ACTIVE" : "INACTIVE").Append(' ')
          .Append(isBefore ? "BEFORE" : "AFTER").Append(' ')
          .Append(string.Join(" OR ", events))
          .Append(" POSITION ").Append(position.ToString(CultureInfo.InvariantCulture)).AppendLine();
        sb.AppendLine("AS");
        sb.Append(string.IsNullOrWhiteSpace(body) ? "BEGIN\nEND" : body.Trim());
        return sb.ToString();
    }

    /// <summary>
    /// Builds the auto-derived trigger name <c>{TABLE}_{timing}{events}{position}</c>
    /// — timing B(efore)/A(fter) + event letters I/U/D in that fixed order + the
    /// position glued on (no separator before it), e.g. <c>ORDERS_BIUD50</c> for a
    /// BEFORE INSERT+UPDATE+DELETE trigger at position 50, <c>STANMAG_BU99</c> for a
    /// BEFORE UPDATE trigger at 99. Pure + testable. The VM calls this only while the
    /// user hasn't overridden the name (and only for a new trigger).
    /// </summary>
    public static string BuildTriggerName(string table, bool isBefore, bool insert, bool update, bool delete, int position)
    {
        var code = new StringBuilder();
        code.Append(isBefore ? 'B' : 'A');
        if (insert) code.Append('I');
        if (update) code.Append('U');
        if (delete) code.Append('D');
        return string.Format(CultureInfo.InvariantCulture, "{0}_{1}{2}", (table ?? string.Empty).Trim(), code.ToString(), position);
    }

    /// <summary>Like <see cref="BuildCommentProcedure"/> but for triggers —
    /// Firebird's <c>COMMENT ON TRIGGER</c> form.</summary>
    public static string BuildCommentTrigger(string triggerName, string? comment)
        => BuildRelationComment("TRIGGER", triggerName, comment);

    private static void AppendProcedureParamLines(StringBuilder sb, IReadOnlyList<Sql.ProcedureParameter> ps, bool includeDefault)
    {
        for (int k = 0; k < ps.Count; k++)
        {
            var p = ps[k];
            sb.Append("    ").Append(QuoteLight((p.Name ?? string.Empty).Trim()))
              .Append(' ').Append((p.TypeText ?? string.Empty).Trim());
            if (p.NotNull) sb.Append(" NOT NULL");
            if (includeDefault && !string.IsNullOrWhiteSpace(p.DefaultValue))
                sb.Append(" = ").Append(p.DefaultValue!.Trim());
            if (k < ps.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
    }

    // Quote only when needed (lowercase / leading non-letter / special chars) so a
    // reassembled procedure reads like the catalog DDL (unquoted SHOUTY_CASE), not
    // "ALL" "QUOTED". Matches FirebirdDdlReader.Quote's lighter convention — distinct
    // from the always-quote Quote used elsewhere in this generator.
    // Quote only when needed (lowercase / special / leading non-letter); SHOUTY_CASE
    // names stay bare, matching the catalog. internal so SecurityDdlGenerator reuses it.
    internal static string QuoteLight(string name)
    {
        if (string.IsNullOrEmpty(name)) return "\"\"";
        bool needs = !char.IsLetter(name[0]) || char.IsLower(name[0]);
        if (!needs)
        {
            foreach (var c in name)
            {
                if (!(char.IsUpper(c) || char.IsDigit(c) || c == '_' || c == '$'))
                {
                    needs = true;
                    break;
                }
            }
        }
        return needs ? "\"" + name.Replace("\"", "\"\"") + "\"" : name;
    }

    private static string BuildRelationComment(string objectType, string name, string? comment)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Object name is required.", nameof(name));

        var n = Quote(name.Trim());
        if (string.IsNullOrWhiteSpace(comment))
        {
            return string.Format(CultureInfo.InvariantCulture, "COMMENT ON {0} {1} IS NULL", objectType, n);
        }
        return string.Format(CultureInfo.InvariantCulture, "COMMENT ON {0} {1} IS '{2}'", objectType, n, EscapeSqlLiteral(comment));
    }

    private static string EscapeSqlLiteral(string s) => s.Replace("'", "''");

    // ─── Foreign-key generation ────────────────────────────────────────────
    //
    // Emits one ALTER TABLE … ADD CONSTRAINT … FOREIGN KEY … REFERENCES …
    // statement. NoAction maps to "omit the clause" — Firebird's default is
    // NO ACTION, and that matches the reader's convention of suppressing
    // RESTRICT-equivalent rules when displaying FKs. Cascade and SetNull each
    // get an explicit clause.

    /// <summary>
    /// Builds the full <c>ALTER TABLE … ADD CONSTRAINT …</c> statement for a
    /// new foreign key. Throws on any validation gap that would produce
    /// invalid SQL (missing names, empty field lists, count mismatch).
    /// </summary>
    public static string BuildAddForeignKey(string tableName, ForeignKeySpec spec)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ConstraintName))
            throw new ArgumentException("Constraint name is required.", nameof(spec));
        if (spec.LocalFields is null || spec.LocalFields.Count == 0)
            throw new ArgumentException("At least one local field is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ReferencedTable))
            throw new ArgumentException("Referenced table is required.", nameof(spec));
        if (spec.ReferencedFields is null || spec.ReferencedFields.Count == 0)
            throw new ArgumentException("At least one referenced field is required.", nameof(spec));
        if (spec.LocalFields.Count != spec.ReferencedFields.Count)
            throw new ArgumentException("Local and referenced field counts must match.", nameof(spec));

        var sb = new StringBuilder();
        sb.Append("ALTER TABLE ").Append(Quote(tableName.Trim()))
          .Append(" ADD CONSTRAINT ").Append(Quote(spec.ConstraintName.Trim()))
          .Append(" FOREIGN KEY (");
        AppendQuotedList(sb, spec.LocalFields);
        sb.Append(") REFERENCES ").Append(Quote(spec.ReferencedTable.Trim()))
          .Append(" (");
        AppendQuotedList(sb, spec.ReferencedFields);
        sb.Append(')');

        // ON UPDATE / ON DELETE clauses — order matches FB syntax. NoAction
        // means "default" → omit. CASCADE / SET NULL render literally.
        var onUpdate = RenderAction(spec.OnUpdate);
        if (onUpdate is not null) sb.Append(" ON UPDATE ").Append(onUpdate);
        var onDelete = RenderAction(spec.OnDelete);
        if (onDelete is not null) sb.Append(" ON DELETE ").Append(onDelete);

        return sb.ToString();
    }

    private static string? RenderAction(ForeignKeyAction action) => action switch
    {
        ForeignKeyAction.Cascade => "CASCADE",
        ForeignKeyAction.SetNull => "SET NULL",
        // NoAction: omit. Future expansion: SetDefault → "SET DEFAULT",
        // Restrict → "NO ACTION" (Firebird treats them similarly but the
        // explicit text helps round-tripping when other engines are involved).
        _ => null,
    };

    private static void AppendQuotedList(StringBuilder sb, IReadOnlyList<string> names)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Quote(names[i].Trim()));
        }
    }

    // ─── Constraint generation (Constraint Management Sprint V1) ──────────
    //
    // Add + Drop only. Firebird has no in-place ALTER CONSTRAINT, so a future
    // "edit constraint" is just Drop + Add over these same builders. Every
    // constraint is added with ALTER TABLE … ADD CONSTRAINT … and dropped with
    // ALTER TABLE … DROP CONSTRAINT …. Identifiers are quoted (internal quotes
    // doubled) so reserved-word / lowercase names round-trip. Field lists go
    // through AppendQuotedList. FK has its own builder (BuildAddForeignKey).

    /// <summary>
    /// <c>ALTER TABLE "T" ADD CONSTRAINT "PK" PRIMARY KEY ("A", "B")</c>, with an
    /// optional <c>USING [ASC|DESC] INDEX "ix"</c> clause to name + order the
    /// backing index (Firebird supports this on PK / UNIQUE / FK constraints).
    /// </summary>
    public static string BuildAddPrimaryKey(string tableName, string constraintName, IReadOnlyList<string> fields,
        string? indexName = null, bool descending = false)
    {
        ValidateConstraintBasics(tableName, constraintName, fields);
        var sb = new StringBuilder();
        sb.Append("ALTER TABLE ").Append(Quote(tableName.Trim()))
          .Append(" ADD CONSTRAINT ").Append(Quote(constraintName.Trim()))
          .Append(" PRIMARY KEY (");
        AppendQuotedList(sb, fields);
        sb.Append(')');
        sb.Append(BuildUsingIndexClause(constraintName, indexName, descending));
        return sb.ToString();
    }

    /// <summary>
    /// <c>ALTER TABLE "T" ADD CONSTRAINT "UQ" UNIQUE ("A", "B")</c>, with the same
    /// optional <c>USING [ASC|DESC] INDEX "ix"</c> clause as
    /// <see cref="BuildAddPrimaryKey"/>.
    /// </summary>
    public static string BuildAddUnique(string tableName, string constraintName, IReadOnlyList<string> fields,
        string? indexName = null, bool descending = false)
    {
        ValidateConstraintBasics(tableName, constraintName, fields);
        var sb = new StringBuilder();
        sb.Append("ALTER TABLE ").Append(Quote(tableName.Trim()))
          .Append(" ADD CONSTRAINT ").Append(Quote(constraintName.Trim()))
          .Append(" UNIQUE (");
        AppendQuotedList(sb, fields);
        sb.Append(')');
        sb.Append(BuildUsingIndexClause(constraintName, indexName, descending));
        return sb.ToString();
    }

    // Firebird's USING clause names + orders the constraint's backing index.
    // The index_name is mandatory in the clause, so to request DESC without an
    // explicit name we default it to the constraint name (FB's own default).
    // No name + ASC → omit the clause entirely (FB auto-creates an ASC index
    // named after the constraint).
    private static string BuildUsingIndexClause(string constraintName, string? indexName, bool descending)
    {
        var hasName = !string.IsNullOrWhiteSpace(indexName);
        if (!hasName && !descending) return string.Empty;
        var ix = hasName ? indexName!.Trim() : constraintName.Trim();
        return $" USING {(descending ? "DESC" : "ASC")} INDEX {Quote(ix)}";
    }

    /// <summary>
    /// <c>ALTER TABLE "T" ADD CONSTRAINT "CK" CHECK (expr)</c>. The
    /// <paramref name="checkExpression"/> may be either a bare condition
    /// (<c>ID &gt; 0</c>) or a full clause (<c>CHECK (ID &gt; 0)</c>) — both
    /// produce a valid <c>CHECK (...)</c> clause. Whitespace is trimmed; the
    /// expression is otherwise embedded verbatim (the user owns its SQL).
    /// </summary>
    public static string BuildAddCheck(string tableName, string constraintName, string checkExpression)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(constraintName))
            throw new ArgumentException("Constraint name is required.", nameof(constraintName));
        if (string.IsNullOrWhiteSpace(checkExpression))
            throw new ArgumentException("Check expression is required.", nameof(checkExpression));

        return string.Format(
            CultureInfo.InvariantCulture,
            "ALTER TABLE {0} ADD CONSTRAINT {1} {2}",
            Quote(tableName.Trim()),
            Quote(constraintName.Trim()),
            NormalizeCheckClause(checkExpression));
    }

    /// <summary>
    /// <c>ALTER TABLE "T" DROP CONSTRAINT "X"</c>. Works for any constraint
    /// kind (PK / FK / CHECK / UNIQUE). Caller confirms the destructive intent;
    /// EmberTern never auto-drops dependents — a Firebird dependency rejection
    /// surfaces to the user.
    /// </summary>
    public static string BuildDropConstraint(string tableName, string constraintName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(constraintName))
            throw new ArgumentException("Constraint name is required.", nameof(constraintName));

        return string.Format(
            CultureInfo.InvariantCulture,
            "ALTER TABLE {0} DROP CONSTRAINT {1}",
            Quote(tableName.Trim()),
            Quote(constraintName.Trim()));
    }

    // ─── Index generation (Index Management V1) ──────────────────────────
    //
    // Add + Drop + recompute-statistics. Firebird has no ALTER INDEX that changes
    // columns / uniqueness / direction — a future "edit index" is Drop + Create over
    // these builders. Indexes backing a PK / FK / UNIQUE constraint are managed
    // through the constraint (the VM blocks dropping them here). ACTIVE/INACTIVE is
    // still out of scope.

    /// <summary>
    /// <c>CREATE [UNIQUE] [DESCENDING] INDEX "ix" ON "T" ("A", "B")</c>, or
    /// <c>… ON "T" COMPUTED BY (expr)</c> for an expression index. Firebird's
    /// default direction is ASCENDING, so the keyword is emitted only for DESC.
    /// </summary>
    public static string BuildCreateIndex(
        string tableName,
        string indexName,
        IReadOnlyList<string> fields,
        bool unique,
        bool descending,
        string? computedExpression = null)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(indexName))
            throw new ArgumentException("Index name is required.", nameof(indexName));

        var hasExpr = !string.IsNullOrWhiteSpace(computedExpression);
        if (!hasExpr && (fields is null || fields.Count == 0))
            throw new ArgumentException("At least one field (or a COMPUTED BY expression) is required.", nameof(fields));

        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (unique) sb.Append("UNIQUE ");
        if (descending) sb.Append("DESCENDING ");
        sb.Append("INDEX ").Append(Quote(indexName.Trim()))
          .Append(" ON ").Append(Quote(tableName.Trim())).Append(' ');
        if (hasExpr)
        {
            sb.Append("COMPUTED BY (").Append(computedExpression!.Trim()).Append(')');
        }
        else
        {
            sb.Append('(');
            AppendQuotedList(sb, fields);
            sb.Append(')');
        }
        return sb.ToString();
    }

    /// <summary>
    /// <c>DROP INDEX "ix"</c>. Caller confirms the destructive intent and is
    /// responsible for NOT calling this on a constraint-backing index (Firebird
    /// rejects that anyway — the VM blocks it up-front with a clearer message).
    /// </summary>
    public static string BuildDropIndex(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
            throw new ArgumentException("Index name is required.", nameof(indexName));
        return $"DROP INDEX {Quote(indexName.Trim())}";
    }

    /// <summary>
    /// <c>SET STATISTICS INDEX "ix"</c> — recomputes the index's selectivity.
    /// This is Firebird's statement for refreshing a SINGLE index's statistics
    /// (valid on FB 1.5 / 2.x / 3 / 4 / 5). Firebird has no <c>ANALYZE INDEX</c>
    /// (that is Oracle syntax); to recompute every index of a table, the caller
    /// issues one <c>SET STATISTICS INDEX</c> per index.
    /// </summary>
    public static string BuildSetIndexStatistics(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
            throw new ArgumentException("Index name is required.", nameof(indexName));
        return $"SET STATISTICS INDEX {Quote(indexName.Trim())}";
    }

    /// <summary><c>ALTER INDEX "ix" ACTIVE</c> — re-enables a deactivated index.
    /// Verified on FB 5.0.3. Firebird rejects this for PRIMARY/UNIQUE/FOREIGN KEY
    /// backing indexes ("Cannot deactivate index used by a … constraint") — the VM
    /// gates the action on <c>IsConstraintBacked</c> up-front.</summary>
    public static string BuildAlterIndexActive(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
            throw new ArgumentException("Index name is required.", nameof(indexName));
        return $"ALTER INDEX {Quote(indexName.Trim())} ACTIVE";
    }

    /// <summary><c>ALTER INDEX "ix" INACTIVE</c> — disables an index (queries stop
    /// using it; it stops being maintained on writes). Same constraint-backed
    /// limitation as <see cref="BuildAlterIndexActive"/>.</summary>
    public static string BuildAlterIndexInactive(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
            throw new ArgumentException("Index name is required.", nameof(indexName));
        return $"ALTER INDEX {Quote(indexName.Trim())} INACTIVE";
    }

    /// <summary><c>ALTER TRIGGER "t" ACTIVE</c> — re-enables a deactivated trigger.
    /// Firebird triggers have a real inactive state (RDB$TRIGGER_INACTIVE), unlike
    /// procedures/functions/packages.</summary>
    public static string BuildAlterTriggerActive(string triggerName)
    {
        if (string.IsNullOrWhiteSpace(triggerName))
            throw new ArgumentException("Trigger name is required.", nameof(triggerName));
        return $"ALTER TRIGGER {Quote(triggerName.Trim())} ACTIVE";
    }

    /// <summary><c>ALTER TRIGGER "t" INACTIVE</c> — disables a trigger (it stops firing).</summary>
    public static string BuildAlterTriggerInactive(string triggerName)
    {
        if (string.IsNullOrWhiteSpace(triggerName))
            throw new ArgumentException("Trigger name is required.", nameof(triggerName));
        return $"ALTER TRIGGER {Quote(triggerName.Trim())} INACTIVE";
    }

    /// <summary>Firebird's <c>COMMENT ON INDEX "ix" IS …</c> (verified on FB 5.0.3 —
    /// writes RDB$INDICES.RDB$DESCRIPTION). Pass null/whitespace to clear
    /// (<c>IS NULL</c>).</summary>
    public static string BuildCommentIndex(string indexName, string? comment)
        => BuildRelationComment("INDEX", indexName, comment);

    /// <summary>
    /// Read-only reconstructed DDL for the Index Detail DDL tab:
    /// <c>CREATE [UNIQUE] [DESCENDING] INDEX "ix" ON "T" ("A", "B")</c> or
    /// <c>… ON "T" COMPUTED BY (expr)</c>, plus an <c>/* INACTIVE */</c> note and a
    /// <c>COMMENT ON INDEX</c> line when present. Built from the loaded
    /// <see cref="IndexDetailInfo"/> (no DB round-trip) — same approach as Domain
    /// Detail's live DDL.
    /// </summary>
    public static string BuildIndexDdl(IndexDetailInfo info)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        if (string.IsNullOrWhiteSpace(info.Name))
            throw new ArgumentException("Index name is required.", nameof(info));

        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (info.IsUnique) sb.Append("UNIQUE ");
        if (info.IsDescending) sb.Append("DESCENDING ");
        sb.Append("INDEX ").Append(Quote(info.Name.Trim()))
          .Append(" ON ").Append(Quote((info.Table ?? string.Empty).Trim())).Append(' ');

        var expr = info.Expression?.Trim();
        if (!string.IsNullOrEmpty(expr))
        {
            // RDB$EXPRESSION_SOURCE is already parenthesized (e.g. "(UPPER(NAME))").
            // COMPUTED BY needs exactly one set of parens — emit the source as-is
            // when it's already wrapped, else wrap it.
            sb.Append("COMPUTED BY ");
            if (expr.StartsWith("(", StringComparison.Ordinal) && expr.EndsWith(")", StringComparison.Ordinal))
                sb.Append(expr);
            else
                sb.Append('(').Append(expr).Append(')');
        }
        else
        {
            sb.Append('(');
            var fields = (info.Fields ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            var cleaned = new List<string>(fields.Length);
            foreach (var f in fields) cleaned.Add(f.Trim());
            AppendQuotedList(sb, cleaned);
            sb.Append(')');
        }
        sb.Append(';');

        if (!info.IsActive) sb.Append("\n/* INACTIVE */");

        if (!string.IsNullOrWhiteSpace(info.Description))
            sb.Append('\n').Append(BuildCommentIndex(info.Name, info.Description)).Append(';');

        return sb.ToString();
    }

    // ─── Sequence / generator generation (Generator Detail) ──────────────
    //
    // A Firebird generator is a SEQUENCE. CREATE / ALTER use the SQL-standard
    // SEQUENCE syntax; the current value is reset with ALTER SEQUENCE … RESTART
    // WITH (FB3+); the description goes through COMMENT ON SEQUENCE. Identifiers
    // are always quoted (internal quotes doubled). No direct UPDATE on RDB$
    // system tables — everything is DDL the engine validates.

    /// <summary>
    /// <c>CREATE SEQUENCE "NAME" [START WITH s] [INCREMENT BY i]</c>. The
    /// <c>START WITH</c> clause is emitted for a non-zero start, or always when
    /// <paramref name="forceStartWith"/> is true; <c>INCREMENT BY</c> only for a
    /// non-default increment (FB4+). The Generator Detail New flow passes
    /// <paramref name="forceStartWith"/>=true so <c>RDB$INITIAL_VALUE</c> is set to
    /// the user's value even for 0 (a plain <c>CREATE SEQUENCE</c> leaves it at the
    /// FB4+ default of 1, which would then mismatch the runtime counter).
    /// </summary>
    public static string BuildCreateSequence(string name, long startWith, long increment, bool forceStartWith = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sequence name is required.", nameof(name));

        var sb = new StringBuilder();
        sb.Append("CREATE SEQUENCE ").Append(Quote(name.Trim()));
        if (startWith != 0 || forceStartWith)
            sb.Append(" START WITH ").Append(startWith.ToString(CultureInfo.InvariantCulture));
        if (increment != 1)
            sb.Append(" INCREMENT BY ").Append(increment.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary><c>DROP SEQUENCE "NAME"</c>. Caller confirms the destructive
    /// intent; EmberTern never auto-drops dependents — a Firebird dependency
    /// rejection surfaces to the user.</summary>
    public static string BuildDropSequence(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sequence name is required.", nameof(name));
        return $"DROP SEQUENCE {Quote(name.Trim())}";
    }

    /// <summary>
    /// <c>SET GENERATOR "NAME" TO v</c> — sets the generator's raw runtime
    /// counter so that <c>GEN_ID(name, 0)</c> returns exactly <paramref name="value"/>
    /// (the next <c>NEXT VALUE FOR</c> then yields <c>value + increment</c>).
    /// This is the **version-independent** way to set the Current Value: verified
    /// to behave identically on FB3 and FB5. Use this — NOT
    /// <see cref="BuildAlterSequenceRestart"/> — for "set the current value to v",
    /// because <c>RESTART WITH</c> changed semantics in FB4 (FB3: counter ← v; FB5:
    /// counter ← v − increment, so the next value is v). See the Generator
    /// semantics audit in CLAUDE.md.
    /// </summary>
    public static string BuildSetGenerator(string name, long value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sequence name is required.", nameof(name));
        return string.Format(CultureInfo.InvariantCulture,
            "SET GENERATOR {0} TO {1}", Quote(name.Trim()), value);
    }

    /// <summary><c>ALTER SEQUENCE "NAME" RESTART WITH v</c> — the SQL-standard
    /// reset. <b>Version-dependent</b>: on FB3 the counter becomes <c>v</c>
    /// (<c>GEN_ID(,0)=v</c>); on FB4+ the counter becomes <c>v − increment</c> so
    /// the NEXT value is <c>v</c>. Because of that split, EmberTern uses
    /// <see cref="BuildSetGenerator"/> (not this) to set the Current Value to an
    /// exact number. Kept for completeness + regression coverage of the shape.</summary>
    public static string BuildAlterSequenceRestart(string name, long value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sequence name is required.", nameof(name));
        return string.Format(CultureInfo.InvariantCulture,
            "ALTER SEQUENCE {0} RESTART WITH {1}", Quote(name.Trim()), value);
    }

    /// <summary><c>ALTER SEQUENCE "NAME" START WITH v</c> — sets the sequence's
    /// INITIAL value used by a future bare RESTART (FB4+).</summary>
    public static string BuildAlterSequenceStartWith(string name, long value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sequence name is required.", nameof(name));
        return string.Format(CultureInfo.InvariantCulture,
            "ALTER SEQUENCE {0} START WITH {1}", Quote(name.Trim()), value);
    }

    /// <summary><c>ALTER SEQUENCE "NAME" INCREMENT BY i</c> — changes the step
    /// (FB4+).</summary>
    public static string BuildAlterSequenceIncrement(string name, long increment)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sequence name is required.", nameof(name));
        return string.Format(CultureInfo.InvariantCulture,
            "ALTER SEQUENCE {0} INCREMENT BY {1}", Quote(name.Trim()), increment);
    }

    /// <summary>Like <see cref="BuildCommentTable"/> but for sequences/generators —
    /// Firebird's <c>COMMENT ON SEQUENCE</c> form (the SQL-standard synonym for
    /// GENERATOR). Pass null/whitespace to clear the comment (<c>IS NULL</c>).</summary>
    public static string BuildCommentSequence(string name, string? comment)
        => BuildRelationComment("SEQUENCE", name, comment);

    // ─── Domains ────────────────────────────────────────────────────────────
    //
    // Firebird ALTER DOMAIN (re-verified against the lab DB on FB 5.0.3, 2026-06-29):
    //   SUPPORTED     — SET/DROP DEFAULT; ADD CHECK / DROP CONSTRAINT (the single
    //                   check); SET/DROP NOT NULL (FB3+); TYPE <t>; TYPE <t> CHARACTER
    //                   SET <cs> (char types — the charset CAN be changed this way);
    //                   TO <name> (rename). Firebird rejects data-unsafe TYPE changes
    //                   (narrowing a length, a type used by an index/constraint)
    //                   server-side, so EmberTern lets those surface as errors.
    //   NOT SUPPORTED — COLLATE in ANY ALTER form (only at CREATE) → -104; a bare
    //                   SET CHARACTER SET (only the TYPE … CHARACTER SET form works).
    // EmberTern therefore edits type / length / precision / scale / sub-type / charset
    // (char) / NOT NULL / DEFAULT / CHECK / name / description on an existing domain;
    // ONLY the collation is read-only after create (no ALTER syntax exists for it).
    // NOTE: this corrects the earlier (overly-restrictive) gotcha #148, which assumed
    // charset could never be ALTERed — the TYPE … CHARACTER SET form does change it.

    /// <summary>Composes the SQL type for a domain from its structured parts —
    /// <c>VARCHAR(50)</c>, <c>NUMERIC(15,2)</c>, <c>BLOB SUB_TYPE 1</c>, or a bare
    /// type name. Mirrors <see cref="FormatTypeOrDomain"/>'s formatting but keyed
    /// off a <see cref="DomainInfo"/> (and handles any BLOB sub-type, not just 0/1).</summary>
    internal static string ComposeDomainType(DomainInfo d)
    {
        var t = (d.DataType ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(t)) t = "INTEGER";
        return t switch
        {
            "CHAR" or "VARCHAR" or "CSTRING"
                => d.Length is { } l ? $"{t}({l.ToString(CultureInfo.InvariantCulture)})" : t,
            "NUMERIC" or "DECIMAL"
                => d.Precision is { } p
                    ? (d.Scale is { } s
                        ? $"{t}({p.ToString(CultureInfo.InvariantCulture)},{s.ToString(CultureInfo.InvariantCulture)})"
                        : $"{t}({p.ToString(CultureInfo.InvariantCulture)})")
                    : t,
            "BLOB"
                => d.SubType is { } st ? $"BLOB SUB_TYPE {st.ToString(CultureInfo.InvariantCulture)}" : "BLOB",
            _ => t,
        };
    }

    private static bool IsCharType(string? dataType)
    {
        var t = (dataType ?? string.Empty).Trim().ToUpperInvariant();
        return t is "CHAR" or "VARCHAR" or "CSTRING";
    }

    /// <summary>Composes the domain's SQL type plus its <c>CHARACTER SET</c> clause
    /// (char types only, skipping <c>NONE</c>) — the form used both after <c>AS</c> in
    /// CREATE and after <c>TYPE</c> in <c>ALTER DOMAIN … TYPE … CHARACTER SET …</c>.
    /// Never appends COLLATE (Firebird rejects it in ALTER).</summary>
    public static string ComposeDomainTypeWithCharset(DomainInfo d)
    {
        var type = ComposeDomainType(d);
        if (IsCharType(d.DataType)
            && !string.IsNullOrWhiteSpace(d.CharacterSet)
            && !string.Equals(d.CharacterSet!.Trim(), "NONE", StringComparison.OrdinalIgnoreCase))
        {
            type += " CHARACTER SET " + d.CharacterSet.Trim();
        }
        return type;
    }

    /// <summary>
    /// Renders a complete, readable <c>CREATE DOMAIN</c> from a <see cref="DomainInfo"/>.
    /// Clause order is the FB-verified one: <c>AS &lt;type&gt; [CHARACTER SET]</c> on
    /// the head line, then <c>DEFAULT</c> / <c>NOT NULL</c> / <c>CHECK</c> / <c>COLLATE</c>
    /// each on its own indented line. CHARACTER SET / COLLATE are emitted only for char
    /// types. This is the single source of the domain DDL — used both for the DDL tab
    /// (display) and for the New-domain Save (execution); internal newlines are
    /// whitespace to the executor so the same string serves both.
    /// </summary>
    public static string BuildCreateDomain(DomainInfo domain)
    {
        if (domain is null) throw new ArgumentNullException(nameof(domain));
        if (string.IsNullOrWhiteSpace(domain.Name))
            throw new ArgumentException("Domain name is required.", nameof(domain));

        var sb = new StringBuilder();
        sb.Append("CREATE DOMAIN ").Append(Quote(domain.Name.Trim())).Append(" AS ").Append(ComposeDomainType(domain));

        var isChar = IsCharType(domain.DataType);
        if (isChar && !string.IsNullOrWhiteSpace(domain.CharacterSet)
            && !string.Equals(domain.CharacterSet!.Trim(), "NONE", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" CHARACTER SET ").Append(domain.CharacterSet.Trim());
        }

        if (!string.IsNullOrWhiteSpace(domain.DefaultValue))
            sb.Append("\n    DEFAULT ").Append(domain.DefaultValue.Trim());
        if (domain.NotNull)
            sb.Append("\n    NOT NULL");
        if (!string.IsNullOrWhiteSpace(domain.CheckConstraint))
            sb.Append("\n    ").Append(NormalizeCheckClause(domain.CheckConstraint.Trim()));
        if (isChar && !string.IsNullOrWhiteSpace(domain.Collation)
            && !string.Equals(domain.Collation!.Trim(), "NONE", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("\n    COLLATE ").Append(domain.Collation.Trim());
        }

        return sb.ToString();
    }

    /// <summary><c>ALTER DOMAIN "N" SET DEFAULT &lt;value&gt;</c>. SET DEFAULT both
    /// adds and replaces a default, so the caller uses it for either.</summary>
    public static string BuildAlterDomainSetDefault(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Default value is required.", nameof(value));
        return $"ALTER DOMAIN {Quote(name.Trim())} SET DEFAULT {value.Trim()}";
    }

    /// <summary><c>ALTER DOMAIN "N" DROP DEFAULT</c>. Errors if the domain has no
    /// default, so the caller only emits it when a default was present.</summary>
    public static string BuildAlterDomainDropDefault(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name is required.", nameof(name));
        return $"ALTER DOMAIN {Quote(name.Trim())} DROP DEFAULT";
    }

    /// <summary><c>ALTER DOMAIN "N" ADD CHECK (…)</c>. Accepts a bare condition or a
    /// full <c>CHECK (…)</c> clause (normalized). A domain has at most one check, so
    /// changing it is DROP CONSTRAINT then ADD CHECK.</summary>
    public static string BuildAlterDomainAddCheck(string name, string check)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(check))
            throw new ArgumentException("Check condition is required.", nameof(check));
        return $"ALTER DOMAIN {Quote(name.Trim())} ADD {NormalizeCheckClause(check.Trim())}";
    }

    /// <summary><c>ALTER DOMAIN "N" DROP CONSTRAINT</c> — drops the domain's single
    /// (unnamed) CHECK constraint. Errors if there is none, so the caller only emits
    /// it when a check was present.</summary>
    public static string BuildAlterDomainDropConstraint(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name is required.", nameof(name));
        return $"ALTER DOMAIN {Quote(name.Trim())} DROP CONSTRAINT";
    }

    /// <summary><c>ALTER DOMAIN "N" TYPE &lt;type&gt;[ CHARACTER SET cs]</c>. Verified on
    /// FB 5.0.3: TYPE changes the domain's data type / length / precision / scale and —
    /// for char types — the <c>CHARACTER SET</c>. Firebird rejects data-unsafe changes
    /// (narrowing a length, a type used by an index/constraint) server-side. COLLATE is
    /// never emitted — Firebird rejects it in ALTER (only valid at CREATE).</summary>
    public static string BuildAlterDomainType(string name, DomainInfo target)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name is required.", nameof(name));
        if (target is null) throw new ArgumentNullException(nameof(target));
        return $"ALTER DOMAIN {Quote(name.Trim())} TYPE {ComposeDomainTypeWithCharset(target)}";
    }

    /// <summary><c>ALTER DOMAIN "N" SET NOT NULL</c> (FB3+). Firebird rejects this if a
    /// column using the domain already holds NULLs.</summary>
    public static string BuildAlterDomainSetNotNull(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name is required.", nameof(name));
        return $"ALTER DOMAIN {Quote(name.Trim())} SET NOT NULL";
    }

    /// <summary><c>ALTER DOMAIN "N" DROP NOT NULL</c> (FB3+).</summary>
    public static string BuildAlterDomainDropNotNull(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name is required.", nameof(name));
        return $"ALTER DOMAIN {Quote(name.Trim())} DROP NOT NULL";
    }

    /// <summary><c>ALTER DOMAIN "OLD" TO "NEW"</c> — renames the domain. The caller must
    /// emit this LAST (after any other ALTERs, which reference the old name) and then
    /// reopen the editor under the new name.</summary>
    public static string BuildAlterDomainRename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName))
            throw new ArgumentException("Domain name is required.", nameof(oldName));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("New domain name is required.", nameof(newName));
        return $"ALTER DOMAIN {Quote(oldName.Trim())} TO {Quote(newName.Trim())}";
    }

    /// <summary><c>COMMENT ON DOMAIN "N" IS …</c>. Pass null/whitespace to clear
    /// (<c>IS NULL</c>).</summary>
    public static string BuildCommentDomain(string name, string? comment)
        => BuildRelationComment("DOMAIN", name, comment);

    /// <summary><c>DROP DOMAIN "N"</c>. Caller confirms the destructive intent;
    /// EmberTern never auto-drops dependents — a Firebird dependency rejection
    /// surfaces to the user.</summary>
    public static string BuildDropDomain(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name is required.", nameof(name));
        return $"DROP DOMAIN {Quote(name.Trim())}";
    }

    // ─── Exceptions ───────────────────────────────────────────────────────────
    //
    // A Firebird custom EXCEPTION is just a name + a message (RDB$MESSAGE) — no
    // PSQL body, no parameters. Syntax verified on FB 5.0.3 (lab DB, embedded):
    //   CREATE EXCEPTION "N" 'message'   — create
    //   ALTER  EXCEPTION "N" 'message'   — change the message text
    //   DROP   EXCEPTION "N"             — remove
    //   COMMENT ON EXCEPTION "N" IS …    — description (RDB$DESCRIPTION)
    // The message literal has its single quotes doubled per SQL string rules.

    /// <summary><c>CREATE EXCEPTION "N" 'message'</c>. The message is required by
    /// Firebird (an empty string is allowed — it emits <c>''</c>).</summary>
    public static string BuildCreateException(string name, string? message)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Exception name is required.", nameof(name));
        return $"CREATE EXCEPTION {Quote(name.Trim())} {QuoteLiteral(message)}";
    }

    /// <summary><c>ALTER EXCEPTION "N" 'message'</c> — changes the raised message
    /// text of an existing exception.</summary>
    public static string BuildAlterException(string name, string? message)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Exception name is required.", nameof(name));
        return $"ALTER EXCEPTION {Quote(name.Trim())} {QuoteLiteral(message)}";
    }

    /// <summary><c>DROP EXCEPTION "N"</c>. Caller confirms the destructive intent;
    /// EmberTern never auto-drops dependents — a Firebird dependency rejection
    /// surfaces to the user.</summary>
    public static string BuildDropException(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Exception name is required.", nameof(name));
        return $"DROP EXCEPTION {Quote(name.Trim())}";
    }

    /// <summary><c>COMMENT ON EXCEPTION "N" IS …</c>. Pass null/whitespace to clear
    /// (<c>IS NULL</c>).</summary>
    public static string BuildCommentException(string name, string? comment)
        => BuildRelationComment("EXCEPTION", name, comment);

    // A SQL string literal: 'text' with single quotes doubled. Null → ''.
    private static string QuoteLiteral(string? text)
        => "'" + (text ?? string.Empty).Replace("'", "''") + "'";

    private static void ValidateConstraintBasics(string tableName, string constraintName, IReadOnlyList<string> fields)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(constraintName))
            throw new ArgumentException("Constraint name is required.", nameof(constraintName));
        if (fields is null || fields.Count == 0)
            throw new ArgumentException("At least one field is required.", nameof(fields));
    }

    // Accept either a full "CHECK (...)" clause or a bare condition; return a
    // "CHECK (...)" clause. A leading CHECK keyword (word-boundary, so "CHECKED"
    // isn't mistaken for it) means the user already wrote the full clause.
    private static string NormalizeCheckClause(string raw)
    {
        var expr = raw.Trim();
        if (expr.Length > 5
            && expr.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase)
            && (char.IsWhiteSpace(expr[5]) || expr[5] == '('))
        {
            return expr;
        }
        return "CHECK (" + expr + ")";
    }

    // ─── Shared ALTER pipeline (inline edit + dialog edit) ────────────────
    //
    // The inline Pola grid edit (FieldRowViewModel → EnqueueRowEdits) and the
    // future "Edit field" dialog both need to compile the same diff: original
    // FieldInfo vs. user's desired shape. BuildAlterStatements is the single
    // source of truth for that diff — each property compared once, each ALTER
    // emitted in the order Firebird tolerates safely (rename first so
    // subsequent ALTERs reference the new name).

    /// <summary>
    /// Diffs <paramref name="original"/> against <paramref name="target"/> and
    /// returns the minimum-set of <see cref="PendingDdlChange"/> entries that
    /// would morph the column to match. Returns an empty list when no relevant
    /// property differs ("no-op" semantics per session spec — caller emits no
    /// DDL when user clicked OK without changing anything).
    /// </summary>
    /// <param name="tableName">Owning table — quoted into every generated statement.</param>
    /// <param name="original">Current state of the column (loaded from <c>RDB$</c>).</param>
    /// <param name="target">User-desired end state.</param>
    /// <param name="canRename">When false, rename and type-change are skipped
    /// (Firebird rejects both when triggers / views / check constraints still
    /// reference the column). Caller surfaces the "blocked" feedback.</param>
    public static IReadOnlyList<PendingDdlChange> BuildAlterStatements(
        string tableName,
        FieldInfo original,
        AlterFieldTarget target,
        bool canRename)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (original is null) throw new ArgumentNullException(nameof(original));
        if (target is null) throw new ArgumentNullException(nameof(target));

        var changes = new List<PendingDdlChange>();

        // 1. Rename — emit FIRST so subsequent ALTERs in this batch reference
        //    the new name. Tracked through `effectiveName` for later steps.
        var effectiveName = original.Name;
        if (!string.IsNullOrWhiteSpace(target.Name)
            && !string.Equals(target.Name, original.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (canRename)
            {
                changes.Add(new PendingDdlChange
                {
                    Kind = PendingDdlChangeKind.Other,
                    Description = string.Format(CultureInfo.CurrentCulture,
                        "Rename {0} → {1}", original.Name, target.Name),
                    Sql = BuildRenameField(tableName, original.Name, target.Name),
                });
                effectiveName = target.Name;
            }
            // canRename=false → caller (inline VM or dialog) handles the
            // "rename blocked" feedback; we silently skip the rename here.
        }

        // 2. Type change — gated by canRename for the same dependency reason
        //    (FB rejects ALTER COLUMN TYPE when objects reference the column).
        //    Null TypeClause means "leave unchanged".
        if (target.TypeClause is { Length: > 0 }
            && !string.Equals(target.TypeClause, original.Type, StringComparison.OrdinalIgnoreCase))
        {
            if (canRename)
            {
                changes.Add(new PendingDdlChange
                {
                    Kind = PendingDdlChangeKind.Other,
                    Description = string.Format(CultureInfo.CurrentCulture,
                        "ALTER COLUMN {0} TYPE {1}", effectiveName, target.TypeClause),
                    Sql = BuildAlterType(tableName, effectiveName, target.TypeClause),
                });
            }
        }

        // 3. NotNull toggle.
        if (target.NotNull != original.NotNull)
        {
            changes.Add(new PendingDdlChange
            {
                Kind = PendingDdlChangeKind.Other,
                Description = string.Format(CultureInfo.CurrentCulture,
                    target.NotNull ? "Set NOT NULL on {0}" : "Drop NOT NULL on {0}",
                    effectiveName),
                Sql = BuildSetNotNull(tableName, effectiveName, target.NotNull),
            });
        }

        // 4. Default — null and empty treated equivalently (both mean "no default").
        var origDefault = original.DefaultValue ?? string.Empty;
        var newDefault = target.DefaultValue ?? string.Empty;
        if (!string.Equals(origDefault, newDefault, StringComparison.Ordinal))
        {
            changes.Add(new PendingDdlChange
            {
                Kind = PendingDdlChangeKind.Other,
                Description = string.Format(CultureInfo.CurrentCulture,
                    string.IsNullOrWhiteSpace(newDefault) ? "Drop default on {0}" : "Set default on {0}",
                    effectiveName),
                Sql = BuildSetDefault(tableName, effectiveName, newDefault),
            });
        }

        // 5. Description (COMMENT ON COLUMN). Null and empty are equivalent —
        //    BuildCommentColumn emits IS NULL on empty/whitespace.
        var origDesc = original.Description ?? string.Empty;
        var newDesc = target.Description ?? string.Empty;
        if (!string.Equals(origDesc, newDesc, StringComparison.Ordinal))
        {
            changes.Add(new PendingDdlChange
            {
                Kind = PendingDdlChangeKind.Other,
                Description = string.Format(CultureInfo.CurrentCulture,
                    "Comment on {0}", effectiveName),
                Sql = BuildCommentColumn(tableName, effectiveName, newDesc),
            });
        }

        return changes;
    }
}
