using System;
using System.Collections.Generic;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I8: the one owner of "what does a column definition become".
/// <para>
/// The file exists to pin ONE property, in several forms: the type text the preview validates against, the
/// type text the <c>CREATE TABLE</c> carries, and the type text the catalog will report afterwards are the
/// <b>same string</b>. Two pieces of code producing it would eventually disagree, and the disagreement would
/// arrive as rows rejected by a table this module itself designed.
/// </para>
/// </summary>
public class ImportNewTableTests
{
    private static ImportColumnDefinition Col(
        string name, string type, int? size = null, int? scale = null, int? subType = null, bool notNull = false)
        => new()
        {
            Name = name, BasicType = type, Size = size, Scale = scale, BlobSubType = subType, NotNull = notNull,
        };

    // ── The type text ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ <c>ImportColumnDefinition</c> carries ONE <c>Size</c>; <c>FieldDefinition</c> splits it into
    /// <c>Size</c> (text) and <c>Precision</c> (numeric). Feeding the right slot is this class's whole job, and
    /// getting it wrong would emit <c>NUMERIC</c> with no precision at all.
    /// </summary>
    [Theory]
    [InlineData("VARCHAR", 20, null, "VARCHAR(20)")]
    [InlineData("CHAR", 3, null, "CHAR(3)")]
    [InlineData("NUMERIC", 15, 2, "NUMERIC(15,2)")]
    [InlineData("NUMERIC", 18, 0, "NUMERIC(18,0)")]
    [InlineData("INTEGER", null, null, "INTEGER")]
    [InlineData("BIGINT", null, null, "BIGINT")]
    [InlineData("DATE", null, null, "DATE")]
    [InlineData("TIMESTAMP", null, null, "TIMESTAMP")]
    [InlineData("BOOLEAN", null, null, "BOOLEAN")]
    public void TypeText_PutsTheArgumentInTheSlotTheTypeActuallyUses(
        string basicType, int? size, int? scale, string expected)
    {
        Assert.Equal(expected, ImportNewTable.TypeText(Col("C", basicType, size, scale)));
    }

    [Fact]
    public void TypeText_RendersATextBlob()
    {
        Assert.Equal("BLOB SUB_TYPE 1", ImportNewTable.TypeText(Col("C", "BLOB", subType: 1)));
    }

    /// <summary>A size on a type that takes none must not leak into the DDL — <c>INTEGER(20)</c> is not
    /// Firebird.</summary>
    [Fact]
    public void ASizeOnATypeThatTakesNone_IsDropped()
    {
        Assert.Equal("INTEGER", ImportNewTable.TypeText(Col("C", "INTEGER", size: 20, scale: 4)));
        Assert.Equal("DATE", ImportNewTable.TypeText(Col("C", "DATE", size: 8)));
    }

    /// <summary>⭐⭐ The invariant the whole etap rests on: whatever type text this class emits,
    /// <see cref="ImportTargetType"/> must read it back as a type the import can write. The projection and the
    /// catalog re-read after the CREATE therefore describe the same column.</summary>
    [Theory]
    [InlineData("VARCHAR", 20, null, SqlValueKind.Text)]
    [InlineData("NUMERIC", 15, 2, SqlValueKind.Decimal)]
    [InlineData("INTEGER", null, null, SqlValueKind.Integer)]
    [InlineData("BIGINT", null, null, SqlValueKind.Integer)]
    [InlineData("DATE", null, null, SqlValueKind.Date)]
    [InlineData("TIMESTAMP", null, null, SqlValueKind.Timestamp)]
    [InlineData("TIME", null, null, SqlValueKind.Time)]
    [InlineData("BOOLEAN", null, null, SqlValueKind.Boolean)]
    public void EveryTypeItEmits_IsATypeTheImportCanWrite(
        string basicType, int? size, int? scale, SqlValueKind expected)
    {
        var resolved = ImportTargetType.Resolve(ImportNewTable.TypeText(Col("C", basicType, size, scale)));

        Assert.True(resolved.IsSupported);
        Assert.Equal(expected, resolved.Kind);
        Assert.Equal(size, resolved.Size);
    }

    // ── The DDL ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The statement comes from the shared <c>DdlGenerator</c> — the same generator the New Table tab
    /// uses (§4.6: nothing new). Names are quoted, so a name with a space or a reserved word round-trips.</summary>
    [Fact]
    public void CreateSql_ComesFromTheSharedGenerator()
    {
        var sql = ImportNewTable.BuildCreateSql("IMP_NEW", new[]
        {
            Col("INDEKS", "VARCHAR", 20),
            Col("ILOSC", "INTEGER", notNull: true),
            Col("CENA", "NUMERIC", 15, 2),
        });

        Assert.Contains("CREATE TABLE \"IMP_NEW\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"INDEKS\" VARCHAR(20)", sql, StringComparison.Ordinal);
        Assert.Contains("\"ILOSC\" INTEGER NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"CENA\" NUMERIC(15,2)", sql, StringComparison.Ordinal);
    }

    /// <summary>An import target is a place to put a file. Inventing a primary key, an identity or a default
    /// the source says nothing about would be the guessing §0 forbids — everything beyond the columns is the
    /// user's to add afterwards.</summary>
    [Fact]
    public void CreateSql_InventsNoConstraints()
    {
        var sql = ImportNewTable.BuildCreateSql("IMP_NEW", new[] { Col("A", "VARCHAR", 10) });

        Assert.DoesNotContain("PRIMARY KEY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IDENTITY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DropSql_QuotesTheNameTheSameWayTheCreateDid()
    {
        Assert.Equal("DROP TABLE \"IMP_NEW\"", ImportNewTable.BuildDropSql("IMP_NEW"));
    }

    [Fact]
    public void DropSql_RefusesAnEmptyName()
    {
        Assert.Throws<ArgumentException>(() => ImportNewTable.BuildDropSql("  "));
    }

    // ── The projection ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ What makes "Validate" work against a table that does not exist yet — and therefore what makes
    /// it worth running at the one moment the decision is still reversible (§0.5).</summary>
    [Fact]
    public void Project_DescribesTheTableAsItWillBe()
    {
        var target = ImportNewTable.Project("IMP_NEW", new[]
        {
            Col("INDEKS", "VARCHAR", 20),
            Col("ILOSC", "INTEGER", notNull: true),
        });

        Assert.Equal("IMP_NEW", target.TableName);
        Assert.Equal(2, target.Columns.Count);
        Assert.Equal("VARCHAR(20)", target.Columns[0].Type);
        Assert.True(target.Columns[1].NotNull);

        // A table that does not exist has no triggers — the BEFORE INSERT warning (R6) is about a table that
        // already had a life, and raising it here would be noise about nothing.
        Assert.Empty(target.BeforeInsertTriggers);
    }

    /// <summary>Every projected column must be mappable and writable: an import target none of whose columns
    /// can be filled would be a table built for nothing.</summary>
    [Fact]
    public void EveryProjectedColumn_IsWritable()
    {
        var target = ImportNewTable.Project("IMP_NEW", new[]
        {
            Col("A", "VARCHAR", 20), Col("B", "INTEGER"), Col("C", "BLOB", subType: 1),
        });

        foreach (var column in target.Columns)
        {
            Assert.False(ImportTarget.IsNeverWritable(column));
            Assert.False(ImportTarget.RequiresOverridingSystemValue(column));
            Assert.True(ImportTargetType.Resolve(column).IsSupported);
        }
    }

    /// <summary>A row with no name is not a column — it is a grid row the user has not filled in yet, and it
    /// must not reach the DDL as <c>"" VARCHAR(20)</c>.</summary>
    [Fact]
    public void ANamelessRow_IsNotAColumn()
    {
        var columns = new[] { Col("A", "VARCHAR", 10), Col("  ", "INTEGER") };

        Assert.Single(ImportNewTable.Project("T", columns).Columns);
        Assert.Single(ImportNewTable.BuildSpec(columns).Fields);
    }

    /// <summary>
    /// ⭐ The end-to-end shape of the etap: infer → project → convert. A value the inferencer accepted must be
    /// accepted by the converter against the PROJECTED column, because that projection is what the preview and
    /// the dry run measure it against.
    /// </summary>
    [Fact]
    public void InferredColumns_ProjectIntoATargetTheConverterAgreesWith()
    {
        IReadOnlyList<ImportColumnDefinition> columns = new[]
        {
            Col("KOD", "VARCHAR", 6),
            Col("ILOSC", "INTEGER"),
            Col("CENA", "NUMERIC", 7, 2),
        };

        var target = ImportNewTable.Project("IMP_NEW", columns);
        var culture = new ImportCultureOptions();

        Assert.True(ImportValueConverter.Convert("ABC123", target.Columns[0], culture).IsSuccess);
        Assert.True(ImportValueConverter.Convert("42", target.Columns[1], culture).IsSuccess);
        Assert.True(ImportValueConverter.Convert("1234,56", target.Columns[2], culture).IsSuccess);
    }
}
