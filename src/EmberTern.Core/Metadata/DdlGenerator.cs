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
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));

        var t = Quote(tableName.Trim());
        if (string.IsNullOrWhiteSpace(comment))
        {
            return string.Format(CultureInfo.InvariantCulture, "COMMENT ON TABLE {0} IS NULL", t);
        }
        return string.Format(CultureInfo.InvariantCulture, "COMMENT ON TABLE {0} IS '{1}'", t, EscapeSqlLiteral(comment));
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
    // Add + Drop only. Firebird has no ALTER INDEX that changes columns /
    // uniqueness / direction — a future "edit index" is Drop + Create over
    // these builders. Indexes backing a PK / FK / UNIQUE constraint are managed
    // through the constraint (the VM blocks dropping them here). ACTIVE/INACTIVE
    // and SET STATISTICS are true ALTERs but out of V1 scope.

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
