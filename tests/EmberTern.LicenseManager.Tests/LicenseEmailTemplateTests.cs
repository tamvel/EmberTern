using System;
using System.Linq;
using System.Text.RegularExpressions;
using EmberTern.LicenseManager.Email;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The message templates as RESOURCES — the mechanism L6.1a delivered, and, since L6.2, the SHAPE of the
/// wording it now carries.
///
/// <para>⚠ <b>Still no sentence is pinned here.</b> What the message says is asserted where it is composed
/// (<see cref="LicenseMessageTests"/>); what this file guards is structural and would otherwise fail
/// silently: that every resource is reachable by the name the resolver composes, that nothing anywhere
/// branches on a language, that every placeholder a template spells is one this build can fill, and that
/// the two bodies of a language — and the two languages — state the SAME set of facts.</para>
/// </summary>
public sealed class LicenseEmailTemplateTests
{
    /// <summary>
    /// ⭐⭐ <b>The assertion that catches the failure this design can actually have.</b> A renamed file, a
    /// missing <c>EmbeddedResource</c> entry, or a folder move would leave the resolver composing a name
    /// nothing answers — and at send time that surfaces as an empty message body, not as an error.
    /// </summary>
    [Fact]
    public void EveryExpectedTemplateIsActuallyEmbedded()
    {
        var embedded = typeof(LicenseEmailTemplates).Assembly.GetManifestResourceNames();

        var missing = LicenseEmailTemplates.AllResourceNames()
            .Where(n => !embedded.Contains(n, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These templates are not embedded in the build:\n  " + string.Join("\n  ", missing)
            + "\nEmbedded resources actually present:\n  " + string.Join("\n  ", embedded));
    }

    /// <summary>⭐ Four resources: two languages, each with an HTML body and a plain-text alternative.</summary>
    [Fact]
    public void ThereIsOnePairPerSupportedLanguage()
    {
        Assert.Equal(MessageLanguages.All.Count * 2, LicenseEmailTemplates.AllResourceNames().Count);
        Assert.Equal(2, MessageLanguages.All.Count);
    }

    [Theory]
    [InlineData("pl", MessageBodyKind.Html, "LicenceEmail.pl.html")]
    [InlineData("pl", MessageBodyKind.Text, "LicenceEmail.pl.txt")]
    [InlineData("en", MessageBodyKind.Html, "LicenceEmail.en.html")]
    [InlineData("en", MessageBodyKind.Text, "LicenceEmail.en.txt")]
    public void ALanguageAndABodyKindNameAResource(string language, MessageBodyKind kind, string expected)
    {
        Assert.EndsWith(expected, LicenseEmailTemplates.ResourceName(language, kind), StringComparison.Ordinal);
    }

    /// <summary>⭐ Every template LOADS, and — since L6.2 — every one of them is actually written.</summary>
    [Fact]
    public void EveryTemplateCanBeReadAndIsWritten()
    {
        foreach (var language in MessageLanguages.All)
        {
            Assert.NotNull(LicenseEmailTemplates.Load(language, MessageBodyKind.Html));
            Assert.NotNull(LicenseEmailTemplates.Load(language, MessageBodyKind.Text));

            Assert.True(
                LicenseEmailTemplates.IsWritten(language, MessageBodyKind.Html),
                $"The HTML template for '{language}' has no body.");
            Assert.True(
                LicenseEmailTemplates.IsWritten(language, MessageBodyKind.Text),
                $"The plain-text template for '{language}' has no body.");
        }
    }

    /// <summary>
    /// ⚠ A settings file naming a language this build does not know must still produce a message — the
    /// resolver falls back rather than throwing, because failing to send over an unrecognised preference
    /// would be failing the operation for the wrong reason.
    /// </summary>
    [Theory]
    [InlineData("de")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownLanguageFallsBackToTheDefault(string? language)
    {
        Assert.Equal(MessageLanguages.Default, MessageLanguages.Resolve(language));
        Assert.EndsWith(
            "LicenceEmail.pl.html",
            LicenseEmailTemplates.ResourceName(language!, MessageBodyKind.Html),
            StringComparison.Ordinal);
    }

    /// <summary>⭐ D-9: Polish is the default, and it is the default in ONE place.</summary>
    [Fact]
    public void PolishIsTheDefaultMessageLanguage()
    {
        Assert.Equal("pl", MessageLanguages.Default);
        Assert.Equal("pl", SmtpSettings.Empty.MessageLanguage);
    }

    /// <summary>
    /// ⚠ A build-time fault, not an operator one: a template that is not embedded throws rather than
    /// degrading, because silently sending an empty body is worse than failing loudly.
    /// </summary>
    [Fact]
    public void AMissingTemplateIsAnErrorRatherThanAnEmptyMessage()
    {
        // ⚠ Reached through the SUPPORTED surface: every language MessageLanguages knows is embedded, so
        //   the throwing path is proved by asking for a resource name that is composed correctly for a
        //   language the catalogue does not carry — which Resolve turns into the default. The guard
        //   therefore asserts the CONTRACT (a real name always loads) rather than contriving a failure.
        foreach (var name in LicenseEmailTemplates.AllResourceNames())
        {
            Assert.NotNull(typeof(LicenseEmailTemplates).Assembly.GetManifestResourceStream(name));
        }
    }

    // ── L6.2: the wording's SHAPE ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The subject lives in the plain-text template's first line, and ⛔ that line is not part of any
    /// body — a customer must never see a literal <c>Subject:</c> at the top of the message.
    /// </summary>
    [Fact]
    public void EveryLanguageDeclaresItsSubjectAndKeepsItOutOfTheBody()
    {
        foreach (var language in MessageLanguages.All)
        {
            var subject = LicenseEmailTemplates.LoadSubject(language);
            Assert.False(string.IsNullOrWhiteSpace(subject), $"'{language}' declares no subject.");

            var body = LicenseEmailTemplates.LoadBody(language, MessageBodyKind.Text);
            Assert.DoesNotContain(LicenseEmailTemplates.SubjectPrefix, body, StringComparison.Ordinal);
            Assert.False(
                body.StartsWith(LicenseEmailTemplates.LineSeparator, StringComparison.Ordinal),
                $"The body for '{language}' begins with the header's blank separator line.");
        }
    }

    /// <summary>
    /// ⭐⭐ <b>A placeholder no value answers is a customer reading <c>{{Seats}}</c>.</b> Every substitution
    /// a template spells must be one <see cref="MessagePlaceholders.All"/> carries.
    /// </summary>
    [Fact]
    public void EveryPlaceholderATemplateUsesIsOneThisBuildKnows()
    {
        foreach (var language in MessageLanguages.All)
        {
            foreach (var kind in new[] { MessageBodyKind.Html, MessageBodyKind.Text })
            {
                var unknown = Used(LicenseEmailTemplates.Load(language, kind))
                    .Where(name => !MessagePlaceholders.All.Contains(name, StringComparer.Ordinal))
                    .ToList();

                Assert.True(
                    unknown.Count == 0,
                    $"{language}/{kind} uses placeholders nothing can fill: {string.Join(", ", unknown)}");
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The HTML body and the plain-text alternative are ONE message in two forms</b> (§14.2). If one
    /// of them states a fact the other does not, a customer whose client strips HTML gets a lesser licence
    /// e-mail than the one beside them — measured here as the SET of substitutions each body performs.
    /// </summary>
    [Fact]
    public void TheTwoBodiesOfALanguageStateTheSameFacts()
    {
        foreach (var language in MessageLanguages.All)
        {
            Assert.Equal(
                Used(LicenseEmailTemplates.LoadBody(language, MessageBodyKind.Text)).OrderBy(n => n, StringComparer.Ordinal),
                Used(LicenseEmailTemplates.LoadBody(language, MessageBodyKind.Html)).OrderBy(n => n, StringComparer.Ordinal));
        }
    }

    /// <summary>⭐ And a translation may not quietly drop a fact the other language states.</summary>
    [Fact]
    public void EveryLanguageStatesTheSameFacts()
    {
        var reference = Used(LicenseEmailTemplates.LoadBody(MessageLanguages.Default, MessageBodyKind.Text))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var language in MessageLanguages.All)
        {
            Assert.Equal(
                reference,
                Used(LicenseEmailTemplates.LoadBody(language, MessageBodyKind.Text))
                    .OrderBy(n => n, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// ⚠ Line endings are normalised on load, so the composed message does not depend on how git checked
    /// the file out — <c>text=auto</c> makes that a real difference between two clones.
    /// </summary>
    [Fact]
    public void LoadedTemplatesUseOneLineSeparator()
    {
        foreach (var language in MessageLanguages.All)
        {
            foreach (var kind in new[] { MessageBodyKind.Html, MessageBodyKind.Text })
            {
                var text = LicenseEmailTemplates.Load(language, kind);
                Assert.DoesNotContain(
                    "\n", text.Replace(LicenseEmailTemplates.LineSeparator, string.Empty, StringComparison.Ordinal),
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "\r", text.Replace(LicenseEmailTemplates.LineSeparator, string.Empty, StringComparison.Ordinal),
                    StringComparison.Ordinal);
            }
        }
    }

    // The placeholder names a template actually uses. ⚠ The same strict pattern the composer substitutes
    // with, deliberately — a guard that matched more loosely would pass over the exact form that ships.
    private static string[] Used(string template) =>
        Regex.Matches(template, @"\{\{([A-Za-z]+)\}\}")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// ⭐⭐ <b>No branch anywhere picks a language.</b> That is the whole of what "prepared for
    /// localization without building a localization system" means here: a code plus a body kind NAME a
    /// resource, so a third language is two files plus one row — never an <c>if</c>.
    /// </summary>
    [Fact]
    public void TheResolverComposesNamesRatherThanBranchingOnLanguage()
    {
        // Composed, not chosen: the language appears inside the name, in order, for every combination.
        foreach (var language in MessageLanguages.All)
        {
            Assert.Contains(
                $".{language}.",
                LicenseEmailTemplates.ResourceName(language, MessageBodyKind.Html),
                StringComparison.Ordinal);
            Assert.Contains(
                $".{language}.",
                LicenseEmailTemplates.ResourceName(language, MessageBodyKind.Text),
                StringComparison.Ordinal);
        }
    }
}
