using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pure-DDL tests for the shared <see cref="DdlGenerator.BuildAlterStatements"/>
/// pipeline used by both inline edit (Pola grid) and dialog edit (AddFieldDialog
/// in edit mode). Pins:
///   - no-diff = empty list (no-op when user clicks OK without changing anything)
///   - rename first (subsequent ALTERs reference the new name)
///   - canRename=false skips rename + type-change, doesn't throw
///   - null TypeClause means "leave type unchanged"
///   - NotNull, Default, Description compare independently
/// </summary>
public class BuildAlterStatementsTests
{
    // BaseTypeName is computed from Type — don't set it explicitly.
    private static FieldInfo BaseField() => new()
    {
        Name = "OPIS",
        Type = "VARCHAR(50)",
        Size = 50,
        NotNull = false,
        DefaultValue = null,
        Description = "",
    };

    // Variant for tests that need a non-default NotNull/DefaultValue. Init-only
    // props mean we can't mutate after construction; use a fresh instance.
    private static FieldInfo FieldWith(bool notNull = false, string? defaultValue = null) => new()
    {
        Name = "OPIS",
        Type = "VARCHAR(50)",
        Size = 50,
        NotNull = notNull,
        DefaultValue = defaultValue,
        Description = "",
    };

    [Fact]
    public void EmptyDiff_ReturnsEmptyList()
    {
        var target = new AlterFieldTarget
        {
            Name = "OPIS",
            TypeClause = null,
            NotNull = false,
            DefaultValue = null,
            Description = "",
        };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: true);
        Assert.Empty(changes);
    }

    [Fact]
    public void RenameOnly_EmitsAlterTo()
    {
        var target = new AlterFieldTarget { Name = "DESC", TypeClause = null, Description = "" };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: true);
        var c = Assert.Single(changes);
        Assert.Contains("ALTER \"OPIS\" TO \"DESC\"", c.Sql);
    }

    [Fact]
    public void Rename_BlockedWhenCanRenameFalse()
    {
        var target = new AlterFieldTarget { Name = "DESC", TypeClause = null, Description = "" };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: false);
        Assert.Empty(changes);
    }

    [Fact]
    public void TypeChange_EmitsAlterType()
    {
        var target = new AlterFieldTarget
        {
            Name = "OPIS",
            TypeClause = "VARCHAR(100)",
            Description = "",
        };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: true);
        var c = Assert.Single(changes);
        Assert.Contains("ALTER \"OPIS\" TYPE VARCHAR(100)", c.Sql);
    }

    [Fact]
    public void TypeChange_BlockedWhenCanRenameFalse()
    {
        // Type-change shares the rename gate — FB rejects ALTER COLUMN TYPE
        // when other objects (views/triggers/checks) reference the column.
        var target = new AlterFieldTarget
        {
            Name = "OPIS",
            TypeClause = "VARCHAR(100)",
            Description = "",
        };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: false);
        Assert.Empty(changes);
    }

    [Fact]
    public void NullTypeClause_LeavesTypeUnchanged()
    {
        // Inline edit passes null TypeClause when user didn't touch the type —
        // explicitly different from passing the original.Type string back
        // (which would still be "no-op" because of the equality check, but
        // costs a string compare). null means "skip the type check entirely".
        var target = new AlterFieldTarget { Name = "OPIS", TypeClause = null, Description = "" };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: true);
        Assert.Empty(changes);
    }

    [Fact]
    public void NotNullChange_EmitsSet()
    {
        var target = new AlterFieldTarget { Name = "OPIS", TypeClause = null, NotNull = true, Description = "" };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: true);
        var c = Assert.Single(changes);
        Assert.Contains("ALTER \"OPIS\" SET NOT NULL", c.Sql);
    }

    [Fact]
    public void NotNullChange_EmitsDrop()
    {
        var original = FieldWith(notNull: true);
        var target = new AlterFieldTarget { Name = "OPIS", TypeClause = null, NotNull = false, Description = "" };
        var changes = DdlGenerator.BuildAlterStatements("T", original, target, canRename: true);
        var c = Assert.Single(changes);
        Assert.Contains("ALTER \"OPIS\" DROP NOT NULL", c.Sql);
    }

    [Fact]
    public void DefaultChange_EmitsSetOrDrop()
    {
        // Add a default
        var target1 = new AlterFieldTarget { Name = "OPIS", TypeClause = null, DefaultValue = "'unknown'", Description = "" };
        var c1 = Assert.Single(DdlGenerator.BuildAlterStatements("T", BaseField(), target1, canRename: true));
        Assert.Contains("ALTER \"OPIS\" SET DEFAULT 'unknown'", c1.Sql);

        // Remove an existing default
        var withDefault = FieldWith(defaultValue: "'old'");
        var target2 = new AlterFieldTarget { Name = "OPIS", TypeClause = null, DefaultValue = null, Description = "" };
        var c2 = Assert.Single(DdlGenerator.BuildAlterStatements("T", withDefault, target2, canRename: true));
        Assert.Contains("ALTER \"OPIS\" DROP DEFAULT", c2.Sql);
    }

    [Fact]
    public void DescriptionChange_EmitsCommentOnColumn()
    {
        var target = new AlterFieldTarget { Name = "OPIS", TypeClause = null, Description = "new description" };
        var c = Assert.Single(DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: true));
        Assert.Contains("COMMENT ON COLUMN \"T\".\"OPIS\" IS 'new description'", c.Sql);
    }

    [Fact]
    public void RenameAndOtherChanges_RenameFirst_SubsequentReferencesNewName()
    {
        // Renaming + setting NOT NULL + changing default. After rename, the
        // SET NOT NULL and SET DEFAULT must target the NEW name (the column
        // by the old name no longer exists post-rename).
        var target = new AlterFieldTarget
        {
            Name = "DESC",
            TypeClause = null,
            NotNull = true,
            DefaultValue = "''",
            Description = "",
        };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: true);
        Assert.Equal(3, changes.Count);
        // Order: rename, NotNull, Default
        Assert.Contains("TO \"DESC\"", changes[0].Sql);
        Assert.Contains("ALTER \"DESC\" SET NOT NULL", changes[1].Sql);
        Assert.Contains("ALTER \"DESC\" SET DEFAULT ''", changes[2].Sql);
    }

    [Fact]
    public void NullAndEmptyDefault_TreatedEquivalent()
    {
        // null DefaultValue and "" DefaultValue must NOT produce a "drop default"
        // statement — both mean "no default". Same for Description.
        var target = new AlterFieldTarget { Name = "OPIS", TypeClause = null, DefaultValue = "", Description = "" };
        var changes = DdlGenerator.BuildAlterStatements("T", BaseField(), target, canRename: true);
        Assert.Empty(changes);
    }

    [Fact]
    public void TableNameQuotingPropagatesThrough()
    {
        // Ensure the table name passes through all builders quoted (defense
        // against future BuildXxx tweaks that might forget to quote).
        var target = new AlterFieldTarget { Name = "OPIS", TypeClause = "INTEGER", Description = "" };
        var c = Assert.Single(DdlGenerator.BuildAlterStatements("MY_T", BaseField(), target, canRename: true));
        Assert.Contains("\"MY_T\"", c.Sql);
    }
}
