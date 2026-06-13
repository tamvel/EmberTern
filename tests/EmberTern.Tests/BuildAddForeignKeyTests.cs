using System;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pure-DDL pin for the FK generator. Covers shape (single/multi-field),
/// each action variant, NoAction-omits-clause convention, validation
/// (missing names / empty fields / count mismatch), and identifier quoting.
/// </summary>
public class BuildAddForeignKeyTests
{
    private static ForeignKeySpec SingleField() => new()
    {
        ConstraintName = "FK_ZAMOWIENIA_KONTRAHENCI",
        LocalFields = new[] { "ID_KONTRAHENT" },
        ReferencedTable = "KONTRAHENCI",
        ReferencedFields = new[] { "ID" },
    };

    [Fact]
    public void SingleField_EmitsAlterTableAddConstraint()
    {
        var sql = DdlGenerator.BuildAddForeignKey("ZAMOWIENIA", SingleField());
        Assert.Contains("ALTER TABLE \"ZAMOWIENIA\"", sql);
        Assert.Contains("ADD CONSTRAINT \"FK_ZAMOWIENIA_KONTRAHENCI\"", sql);
        Assert.Contains("FOREIGN KEY (\"ID_KONTRAHENT\")", sql);
        Assert.Contains("REFERENCES \"KONTRAHENCI\" (\"ID\")", sql);
    }

    [Fact]
    public void NoAction_OmitsBothClauses()
    {
        var sql = DdlGenerator.BuildAddForeignKey("T", SingleField());
        // Default = NoAction; both clauses are omitted entirely so the
        // generated DDL doesn't carry redundant ON UPDATE / ON DELETE
        // (matches the display-side convention in ForeignKeyRule).
        Assert.DoesNotContain("ON UPDATE", sql);
        Assert.DoesNotContain("ON DELETE", sql);
    }

    [Fact]
    public void OnDeleteCascade_EmitsClause()
    {
        var spec = SingleField();
        var withDelete = new ForeignKeySpec
        {
            ConstraintName = spec.ConstraintName,
            LocalFields = spec.LocalFields,
            ReferencedTable = spec.ReferencedTable,
            ReferencedFields = spec.ReferencedFields,
            OnDelete = ForeignKeyAction.Cascade,
        };
        var sql = DdlGenerator.BuildAddForeignKey("ZAMOWIENIA", withDelete);
        Assert.Contains("ON DELETE CASCADE", sql);
        Assert.DoesNotContain("ON UPDATE", sql);
    }

    [Fact]
    public void OnUpdateSetNull_EmitsClause()
    {
        var spec = SingleField();
        var withUpdate = new ForeignKeySpec
        {
            ConstraintName = spec.ConstraintName,
            LocalFields = spec.LocalFields,
            ReferencedTable = spec.ReferencedTable,
            ReferencedFields = spec.ReferencedFields,
            OnUpdate = ForeignKeyAction.SetNull,
        };
        var sql = DdlGenerator.BuildAddForeignKey("ZAMOWIENIA", withUpdate);
        Assert.Contains("ON UPDATE SET NULL", sql);
    }

    [Fact]
    public void BothActions_EmitsBothClausesInOrder()
    {
        var spec = SingleField();
        var both = new ForeignKeySpec
        {
            ConstraintName = spec.ConstraintName,
            LocalFields = spec.LocalFields,
            ReferencedTable = spec.ReferencedTable,
            ReferencedFields = spec.ReferencedFields,
            OnUpdate = ForeignKeyAction.Cascade,
            OnDelete = ForeignKeyAction.SetNull,
        };
        var sql = DdlGenerator.BuildAddForeignKey("ZAMOWIENIA", both);
        var onUpdateIdx = sql.IndexOf("ON UPDATE", StringComparison.Ordinal);
        var onDeleteIdx = sql.IndexOf("ON DELETE", StringComparison.Ordinal);
        Assert.True(onUpdateIdx > 0 && onDeleteIdx > 0);
        Assert.True(onUpdateIdx < onDeleteIdx, "ON UPDATE must appear before ON DELETE");
    }

    [Fact]
    public void MultiField_EmitsCommaSeparatedLists()
    {
        var spec = new ForeignKeySpec
        {
            ConstraintName = "FK_COMPOSITE",
            LocalFields = new[] { "A", "B", "C" },
            ReferencedTable = "OTHER",
            ReferencedFields = new[] { "X", "Y", "Z" },
        };
        var sql = DdlGenerator.BuildAddForeignKey("T", spec);
        Assert.Contains("FOREIGN KEY (\"A\", \"B\", \"C\")", sql);
        Assert.Contains("REFERENCES \"OTHER\" (\"X\", \"Y\", \"Z\")", sql);
    }

    [Fact]
    public void IdentifierQuoting_HandlesLowercase()
    {
        var spec = new ForeignKeySpec
        {
            ConstraintName = "fk_lowercase",
            LocalFields = new[] { "field_a" },
            ReferencedTable = "other_table",
            ReferencedFields = new[] { "ID" },
        };
        var sql = DdlGenerator.BuildAddForeignKey("source_table", spec);
        // Every identifier passes through Quote() so lowercase names
        // round-trip safely with Firebird's case-sensitivity rules.
        Assert.Contains("\"source_table\"", sql);
        Assert.Contains("\"fk_lowercase\"", sql);
        Assert.Contains("\"field_a\"", sql);
        Assert.Contains("\"other_table\"", sql);
    }

    [Fact]
    public void NullSpec_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DdlGenerator.BuildAddForeignKey("T", null!));
    }

    [Fact]
    public void MissingConstraintName_Throws()
    {
        var spec = new ForeignKeySpec
        {
            ConstraintName = "",
            LocalFields = new[] { "A" },
            ReferencedTable = "Y",
            ReferencedFields = new[] { "B" },
        };
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAddForeignKey("T", spec));
    }

    [Fact]
    public void MissingReferencedTable_Throws()
    {
        var spec = new ForeignKeySpec
        {
            ConstraintName = "FK",
            LocalFields = new[] { "A" },
            ReferencedTable = "",
            ReferencedFields = new[] { "B" },
        };
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAddForeignKey("T", spec));
    }

    [Fact]
    public void EmptyLocalFields_Throws()
    {
        var spec = new ForeignKeySpec
        {
            ConstraintName = "FK",
            LocalFields = Array.Empty<string>(),
            ReferencedTable = "Y",
            ReferencedFields = new[] { "B" },
        };
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAddForeignKey("T", spec));
    }

    [Fact]
    public void EmptyReferencedFields_Throws()
    {
        var spec = new ForeignKeySpec
        {
            ConstraintName = "FK",
            LocalFields = new[] { "A" },
            ReferencedTable = "Y",
            ReferencedFields = Array.Empty<string>(),
        };
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAddForeignKey("T", spec));
    }

    [Fact]
    public void CountMismatch_Throws()
    {
        var spec = new ForeignKeySpec
        {
            ConstraintName = "FK",
            LocalFields = new[] { "A", "B" },
            ReferencedTable = "Y",
            ReferencedFields = new[] { "X" },
        };
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAddForeignKey("T", spec));
    }

    [Fact]
    public void EmptyTableName_Throws()
    {
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAddForeignKey("", SingleField()));
    }
}
