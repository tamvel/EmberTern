using System.IO;
using System.Text;
using System.Threading.Tasks;
using EmberTern.App.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Guards the export encoding contract: .sql files are UTF-8 WITHOUT a BOM (a BOM breaks
/// isql / IBExpert on the first statement), and Polish/Unicode characters round-trip
/// losslessly. Regression pin against an accidental return to Encoding.UTF8 (which emits a BOM).
/// </summary>
public class SqlFileWriterTests
{
    private const string PolishScript =
        "CREATE OR ALTER PROCEDURE \"ZAMÓWIENIA\" AS BEGIN END;\n\n" +
        "COMMENT ON PROCEDURE \"ZAMÓWIENIA\" IS 'Obsługa zamówień — żółć, ćma, gęś';";

    [Fact]
    public async Task WriteAsync_WritesUtf8WithoutBom()
    {
        var path = Path.Combine(Path.GetTempPath(), "embertern-sql-" + System.Guid.NewGuid().ToString("N") + ".sql");
        try
        {
            await SqlFileWriter.WriteAsync(path, PolishScript);
            var bytes = await File.ReadAllBytesAsync(path);

            // No UTF-8 BOM (EF BB BF) at the start.
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "Exported .sql must not start with a UTF-8 BOM.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_RoundTripsPolishCharacters()
    {
        var path = Path.Combine(Path.GetTempPath(), "embertern-sql-" + System.Guid.NewGuid().ToString("N") + ".sql");
        try
        {
            await SqlFileWriter.WriteAsync(path, PolishScript);

            // Decoded as UTF-8, the content must be byte-for-byte the original.
            var readBack = await File.ReadAllTextAsync(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Equal(PolishScript, readBack);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Encoding_IsNoBom_NotEncodingUtf8()
    {
        // The whole point: our encoding emits no preamble, whereas Encoding.UTF8 does.
        Assert.Empty(SqlFileWriter.Utf8NoBom.GetPreamble());
        Assert.NotEmpty(Encoding.UTF8.GetPreamble()); // documents the trap we avoid
    }
}
