using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace EmberTern.LicenseManager.Email;

/// <summary>Which of a message's two bodies is wanted.</summary>
public enum MessageBodyKind
{
    /// <summary>The rich body. ⚠ Never the only one — some corporate clients strip HTML (§14.2).</summary>
    Html = 0,

    /// <summary>The plain-text alternative.</summary>
    Text = 1,
}

/// <summary>
/// The message bodies, as resources, resolved by language.
///
/// <para>⭐⭐ <b>THE POINT OF THIS CLASS IS THAT THERE IS NO <c>if</c>.</b> A language is a code and a body
/// kind is an enum; together they NAME a resource. Adding a third language is two files plus one row in
/// <see cref="MessageLanguages"/> — ⛔ never a branch here, and never a branch at a call site. That is the
/// whole of what "prepared for localization without building a localization system" means for L6.</para>
///
/// <para>⭐ <b>The templates carry the wording as of L6.2.</b> L6.1a shipped the mechanism with four empty
/// files; this stage wrote them. ⛔ They carry no customer data: every fact about a licence arrives as a
/// substitution (<see cref="MessagePlaceholders"/>), because a template that named a customer would be a
/// template per customer.</para>
///
/// <para>⭐⭐ <b>THE SUBJECT LIVES IN THE PLAIN-TEXT TEMPLATE'S FIRST LINE</b>, as <c>Subject: …</c>, and
/// <see cref="LoadBody"/> strips it. The subject is a header of the message rather than a part of a body,
/// and it has to be translated with the body — so it belongs in the same resource pair rather than in a
/// fifth file per language, or in a string in code that an <c>if</c> would then have to pick.
/// ⭐ The HTML template repeats it in <c>title</c>, which is where an HTML document's subject already
/// belongs; a test composes both and fails if the two ever drift.</para>
///
/// <para>⚠ Embedded rather than loose files on disk: a template is part of the build, not a thing an
/// operator edits. ⛔ A file the operator could edit would be a fifth thing to back up, and a message the
/// register could not reproduce.</para>
/// </summary>
public static class LicenseEmailTemplates
{
    /// <summary>
    /// The prefix that marks the plain-text template's subject line.
    ///
    /// <para>⚠ Matched once, at the very start of the file. ⛔ Not a general header parser — this is one
    /// declared line in a file we own, and treating it as RFC 5322 would be inventing a format.</para>
    /// </summary>
    public const string SubjectPrefix = "Subject:";

    /// <summary>
    /// The line separator every loaded template is normalised to.
    ///
    /// <para>⭐ CRLF, for the same reason <c>LicenseArmor.LineSeparator</c> is: this text becomes a mail
    /// body, so it must not depend on the platform — or, here, on the <c>text=auto</c> checkout — that
    /// produced the file. ⚠ Without it a Linux checkout and a Windows checkout would compose two different
    /// messages from one template.</para>
    /// </summary>
    public const string LineSeparator = "\r\n";

    /// <summary>The resource folder, as a namespace path.</summary>
    private const string Folder = "EmberTern.LicenseManager.Email.Templates";

    /// <summary>The stem every template file shares.</summary>
    private const string Stem = "LicenceEmail";

    private static readonly Assembly Owner = typeof(LicenseEmailTemplates).Assembly;

    /// <summary>
    /// The resource name a language and body kind resolve to, e.g. <c>…Templates.LicenceEmail.pl.html</c>.
    ///
    /// <para>⭐ Public because it is the thing worth ASSERTING: a guard that checks the composed name
    /// against the resources the assembly actually carries catches a renamed or unembedded file at test
    /// time rather than at send time.</para>
    /// </summary>
    public static string ResourceName(string language, MessageBodyKind kind) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Folder}.{Stem}.{MessageLanguages.Resolve(language)}.{Extension(kind)}");

    /// <summary>Every resource name this build expects to carry — one per language, per body kind.</summary>
    /// <remarks>⭐ Derived from <see cref="MessageLanguages.All"/>, never listed again by hand.</remarks>
    public static IReadOnlyList<string> AllResourceNames()
    {
        var names = new List<string>(MessageLanguages.All.Count * 2);
        foreach (var language in MessageLanguages.All)
        {
            names.Add(ResourceName(language, MessageBodyKind.Html));
            names.Add(ResourceName(language, MessageBodyKind.Text));
        }

        return names;
    }

    /// <summary>
    /// Reads a template, verbatim apart from line-ending normalisation — subject line included.
    ///
    /// <para>⚠ An unsupported language resolves to <see cref="MessageLanguages.Default"/> rather than
    /// throwing — a message must stay composable even if a settings file names a language this build does
    /// not know.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The resource is not embedded at all. ⭐ That is a BUILD fault, not an operator one, so it throws
    /// rather than degrading: a missing template means every message would go out with an empty body, and
    /// silently sending nothing is worse than failing loudly.
    /// </exception>
    public static string Load(string language, MessageBodyKind kind)
    {
        var name = ResourceName(language, kind);

        using var stream = Owner.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"The message template '{name}' is not embedded in this build.");

        using var reader = new StreamReader(stream);
        return Normalise(reader.ReadToEnd());
    }

    /// <summary>
    /// The subject line, still carrying its placeholders.
    ///
    /// <para>⭐ Read from the PLAIN-TEXT template, which is its one source — see the type remarks.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The template does not begin with <see cref="SubjectPrefix"/>. ⭐ A build fault again, and refused
    /// rather than defaulted: a message with an invented subject is a message nobody wrote.
    /// </exception>
    public static string LoadSubject(string language)
    {
        var text = Load(language, MessageBodyKind.Text);
        var end = text.IndexOf(LineSeparator, StringComparison.Ordinal);
        var first = end < 0 ? text : text[..end];

        if (!first.StartsWith(SubjectPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The plain-text template for '{MessageLanguages.Resolve(language)}' does not begin with " +
                $"'{SubjectPrefix}'. That line is where the message's subject lives.");
        }

        return first[SubjectPrefix.Length..].Trim();
    }

    /// <summary>
    /// The body alone — the plain-text template without its subject line, or the HTML template as it is.
    ///
    /// <para>⚠ Only the plain-text form has a subject line to strip; the HTML form carries the subject in
    /// its <c>title</c>, where it is part of the document rather than a header pasted in front of it.</para>
    /// </summary>
    public static string LoadBody(string language, MessageBodyKind kind)
    {
        var text = Load(language, kind);
        if (kind != MessageBodyKind.Text)
        {
            return text;
        }

        // ⭐ Verified through the ONE reader that knows the rule, so a missing header fails here too rather
        //    than silently handing back a body with a stray "Subject:" line at the top of it.
        _ = LoadSubject(language);

        var end = text.IndexOf(LineSeparator, StringComparison.Ordinal);
        if (end < 0)
        {
            return string.Empty;
        }

        var body = text[(end + LineSeparator.Length)..];

        // The blank line after the header belongs to the header's shape, not to the message — exactly one
        // is dropped, so a deliberate blank first line of a body would still survive.
        return body.StartsWith(LineSeparator, StringComparison.Ordinal)
            ? body[LineSeparator.Length..]
            : body;
    }

    /// <summary>
    /// Whether a template has any content yet.
    ///
    /// <para>⭐ The honest way to ask "is the wording written for this language?" — and the reason L6.1a
    /// could ship empty resources without that looking like a bug to the next reader.</para>
    /// </summary>
    public static bool IsWritten(string language, MessageBodyKind kind) =>
        !string.IsNullOrWhiteSpace(LoadBody(language, kind));

    private static string Extension(MessageBodyKind kind) =>
        kind == MessageBodyKind.Html ? "html" : "txt";

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", LineSeparator, StringComparison.Ordinal);
}
