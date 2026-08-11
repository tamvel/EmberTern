using System;
using EmberTern.Core.Localization;

namespace EmberTern.Office;

/// <summary>
/// A source file that cannot be read as the format its name claims, carrying <b>both</b> a localizable
/// description and an English one.
///
/// <para>⭐ <b>The same dual shape as <c>ConnectionFailedException</c>, and here the argument for it is
/// stronger rather than weaker.</b> That exception has three surfaces that name its type; this one has
/// <b>none</b>. <c>DataImportTabViewModel</c> catches by POSITION on purpose — it reaches the world through
/// delegates and therefore cannot enumerate what the world throws (gotchas #264 / #265), and an allow-list
/// there once closed the application. So most paths that meet this exception will read
/// <see cref="Exception.Message"/> without ever knowing what it is, and that must keep working. Putting a KEY
/// in <c>Message</c> would put <c>Import.Source.NotReadableXlsx</c> in front of the user; leaving English
/// there means an unmigrated path degrades to <b>exactly today's behaviour</b>, never to something worse.</para>
///
/// <para>⚠ The duplication is guarded rather than tolerated: <c>Message</c> must render the same sentence
/// <see cref="Localized"/> resolves to in English, and a test pins it. That guard is doing more work here than
/// in C3 — the two producers' English text has no other pin, so it is also the <i>only</i> machine check that
/// a resource edit has not quietly changed what the exception says.</para>
///
/// <para>⛔ It deliberately does <b>not</b> derive from <c>InvalidDataException</c>, which is what the
/// providers threw before. A subclass would let every existing <c>catch</c> and
/// <c>Assert.ThrowsAsync&lt;InvalidDataException&gt;</c> keep compiling and keep passing — i.e. it would
/// suppress the very signal that enumerates the call sites, which is how the type changes in etaps C2 and C5
/// found consumers their author's inventory had missed.</para>
/// </summary>
public sealed class ImportSourceException : Exception
{
    /// <param name="localized">What to show the user — a key plus data, resolved by the App.</param>
    /// <param name="message">
    /// The same sentence in English, for logs and for any catch-all that reads <see cref="Exception.Message"/>.
    /// </param>
    /// <param name="inner">
    /// What the reader library actually said. ⛔ Kept here and never surfaced: for the <c>.xlsx</c> case it is
    /// <i>File contains corrupted data</i> about a file that is not corrupted at all.
    /// </param>
    public ImportSourceException(LocalizableMessage localized, string message, Exception? inner = null)
        : base(message, inner)
    {
        Localized = localized ?? throw new ArgumentNullException(nameof(localized));
    }

    /// <summary>The user-facing description, unresolved. ⭐ Resolve with <c>Loc.Format</c> at the moment of
    /// display — never earlier, or the text freezes in the language that was current when the read failed.
    /// </summary>
    public LocalizableMessage Localized { get; }
}
