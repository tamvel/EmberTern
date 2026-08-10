using EmberTern.Core.Localization;

namespace EmberTern.Office;

/// <summary>
/// What the two workbook readers say when a file is not the format its name claims — decision <b>D‑3</b>'s
/// producer for <see cref="EmberTern.Office"/>, and the first one outside Core/Firebird.
///
/// <para>⭐ <b>Why these two sentences are OURS and the libraries' are not.</b> Both readers were measured
/// against real files and both answer uselessly: <c>DocumentFormat.OpenXml</c> says <i>File contains corrupted
/// data</i> for a perfectly intact workbook that merely predates the format, and <c>ExcelDataReader</c> says
/// <i>Invalid file signature</i>. The first is not merely unhelpful, it is <b>false</b> — it tells the user
/// their file is damaged when it is fine. So unlike <c>FirebirdConnectionMessages</c>, where the server's own
/// words travel as an argument because they are authoritative, here the library's words are <b>deliberately
/// not shown at all</b>; they stay on the exception's <c>InnerException</c> for a developer to find.</para>
///
/// <para>⚠ <b>Each key covers a WHOLE sentence, including the advice</b> — and the advice is the part that
/// moves. It changed once already: until etap I10 the old format could not be read at all, so the only way
/// forward was Save As; <see cref="XlsImportProvider"/> reads it now, so "rename it" comes first. A translator
/// will edit these values, which is exactly why splitting the refusal from its remedy into two keys would be
/// wrong — an inflecting language may not join them in that order.</para>
///
/// <para>⚠ <c>{0}</c> is DATA: <c>IImportSource.DisplayName</c>, i.e. the file's name. Nothing here formats
/// it; the App does, in the reader's culture.</para>
/// </summary>
public static class ImportSourceMessages
{
    /// <summary>A file named <c>.xlsx</c> that the OOXML reader cannot open. <c>{0}</c> is the file name.</summary>
    public static readonly MessageKey NotReadableXlsx = new("Import.Source.NotReadableXlsx");

    /// <summary>A file named <c>.xls</c> that the BIFF reader cannot open. <c>{0}</c> is the file name.</summary>
    public static readonly MessageKey NotReadableXls = new("Import.Source.NotReadableXls");
}
