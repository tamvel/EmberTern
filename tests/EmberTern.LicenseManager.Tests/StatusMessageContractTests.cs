using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>What L8.2 actually claims: a message is a KEY and its ARGUMENTS, and the words are chosen when it
/// is read rather than when it is raised.</b>
///
/// <para>Before this stage <c>StatusMessage</c> stored a finished sentence, so a message already on the
/// strip kept speaking whatever language it was raised in — a defect nothing on screen admits to, and the
/// exact shape the product still carries in Data Import (#353). These tests measure the new behaviour
/// rather than reading the code for it.</para>
///
/// <para>⚠ No Avalonia here on purpose: the contract is about the message object and the resolver, and a
/// window would only make the measurement slower and flakier. The BINDING half is
/// <see cref="LocalizationLivenessTests"/>'s job.</para>
/// </summary>
public sealed class StatusMessageContractTests
{
    // ⚠ A real pseudo-locale, matching LocalizationLivenessTests — no pseudo-language ever ships.
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    /// <summary>A catalog that answers differently per culture, so "did it re-read" is measurable.</summary>
    private sealed class TwoLanguageCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture) =>
            Equals(culture, Pseudo) ? "PL:" + name + " [{0}]" : "EN:" + name + " [{0}]";
    }

    // ── The central claim ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>THE measurement: a STANDING message follows a language change.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ The message object is created once and never touched again — exactly the situation the old design
    /// got wrong. If <see cref="StatusMessage.Text"/> were captured at construction (or cached in a field
    /// afterwards), the second assertion reads the first language and the strip lies to the operator.
    /// </remarks>
    [Fact]
    public void AStandingMessage_FollowsALanguageChange()
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);

            var message = StatusMessage.Error(StatusCatalog.FileNotWritten, "C:\\out.etlic");
            Assert.StartsWith("EN:", message.Text, StringComparison.Ordinal);

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);

            // ⭐ Same object. Nothing rebuilt it, nothing re-raised it.
            Assert.StartsWith("PL:", message.Text, StringComparison.Ordinal);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>⭐ The arguments survive the change — the sentence moves, the values do not.</summary>
    [Fact]
    public void TheArguments_SurviveALanguageChange()
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);

            var message = StatusMessage.Error(StatusCatalog.FileNotWritten, "C:\\out.etlic");
            Assert.Contains("C:\\out.etlic", message.Text, StringComparison.Ordinal);

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            Assert.Contains("C:\\out.etlic", message.Text, StringComparison.Ordinal);

            Assert.Equal(["C:\\out.etlic"], message.Arguments);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>⛔ An argument array mutated after the fact cannot change a message already on screen.</summary>
    [Fact]
    public void TheArguments_AreCopiedAtConstruction()
    {
        var arguments = new object?[] { "first" };
        var message = StatusMessage.Error(StatusCatalog.FileNotWritten, arguments);

        arguments[0] = "second";

        Assert.Equal(["first"], message.Arguments);
    }

    // ── The host that carries the strip ──────────────────────────────────────────────────────────────

    /// <summary>A concrete host, because the base is abstract and the behaviour under test is the base's.</summary>
    private sealed class TestHost : MessageHostViewModel;

    /// <summary>
    /// ⭐⭐ The strip ANNOUNCES the new language — resolving live is not enough on its own.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ Two halves, and only together do they work: <see cref="StatusMessage.Text"/> re-reads, but a
    /// binding re-reads only when something tells it to, and nothing about switching languages touches this
    /// view model's own properties. Without the notification the strip keeps the old language on screen
    /// while every other word changes — with no binding error and no exception.
    /// </remarks>
    [Fact]
    public void TheStrip_AnnouncesThatItsTextChanged()
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);

            var host = new TestHost { Message = StatusMessage.Error(StatusCatalog.FileNotWritten, "x") };

            var announced = new List<string?>();
            host.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);

            Assert.Contains(nameof(MessageHostViewModel.MessageText), announced);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐⭐ A host that has gone out of use is COLLECTABLE — the subscription does not root it.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <c>Loc.LanguageChanged</c> is a <c>static</c> event, so a plain <c>+=</c> would make every
    /// message host immortal. Four of the five live as long as the window and would have hidden it;
    /// <c>SendLicenceViewModel</c> is rebuilt on EVERY send, so the operator would accumulate one dead view
    /// model per licence sent. ⭐ Decision P4: the subscription is weak so no short-lived host has to
    /// remember to detach.</para>
    /// <para>⚠ The handler must stay a <c>static</c> lambda for this to hold — a closure over the host
    /// would keep it reachable through the delegate and this test would go red, which is the point.</para>
    /// </remarks>
    [Fact]
    public void AShortLivedHost_IsNotKeptAliveByItsSubscription()
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        var weak = CreateCollectableHost();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(
            weak.TryGetTarget(out _),
            "The message host is still reachable after going out of scope — Loc.LanguageChanged is a "
            + "static event, so a strong subscription makes every host immortal.");
    }

    // ⚠ In its own non-inlined method so the local cannot stay alive in the caller's frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TestHost> CreateCollectableHost() => new(new TestHost());

    // ── Exceptions ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>A foreign exception's message becomes an ARGUMENT, never a key.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The failure this forbids is the one that would quietly undo the stage: if a server's or the
    /// operating system's sentence were used AS a key, it would resolve to itself (<c>Loc.Text</c> answers
    /// a missing key with the key) and render perfectly, while the catalog sat unused. ⛔ Their words are
    /// not ours to translate; our sentence is the key.
    /// </remarks>
    [Fact]
    public void AForeignExceptionMessage_TravelsAsAnArgumentAndNotAsAKey()
    {
        var error = new IOException("The process cannot access the file.");

        var message = StatusMessage.FromError(error, MessageSeverity.Error);

        Assert.Equal(StatusCatalog.Verbatim, message.Key);
        Assert.Equal([error.Message], message.Arguments);

        // ⭐ The key is ours and is a real catalog entry; the foreign text is only data.
        Assert.NotEqual(error.Message, message.Key.Value);
        Assert.NotNull(Loc.Find(message.Key.Value));
    }

    /// <summary>
    /// ⭐ Where the sentence is OURS, the exception carries its key and the words are resolved.
    /// </summary>
    /// <remarks>
    /// ⚠ Measured by switching languages: an <see cref="ILocalizedError"/> whose text were taken from
    /// <c>ex.Message</c> would render the same English in both, which is precisely the Phase-5 defect one
    /// layer further in.
    /// </remarks>
    [Fact]
    public void OurOwnExceptionSentence_ResolvesRatherThanBeingPrinted()
    {
        var error = new LocalizedOperationException(
            StatusCatalog.KeystoreAlreadyExists, "A keystore already exists.");

        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);

            var message = StatusMessage.FromError(error, MessageSeverity.Error);
            Assert.Equal(StatusCatalog.KeystoreAlreadyExists, message.Key);
            Assert.StartsWith("EN:", message.Text, StringComparison.Ordinal);

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            Assert.StartsWith("PL:", message.Text, StringComparison.Ordinal);

            // ⛔ And never the exception's own English, which exists for a debugger only.
            Assert.DoesNotContain("A keystore already exists.", message.Text, StringComparison.Ordinal);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⛔ No view model puts an exception's own message on the strip by hand.
    /// </summary>
    /// <remarks>
    /// <para>⭐ Scanned over the SOURCE, because the shape being forbidden compiles perfectly. The legal
    /// routes are exactly two: hand the text in as an ARGUMENT to one of our keys
    /// (<c>Error(SomeKey, e.Message)</c>), or go through <see cref="StatusMessage.FromError"/>. Passing
    /// <c>e.Message</c> as the KEY is what this catches.</para>
    /// <para>⚠ Comments are stripped first (#396) — a guard about code must not be answered by prose
    /// describing the rule.</para>
    /// </remarks>
    [Fact]
    public void NoViewModel_PutsAnExceptionMessageWhereAKeyBelongs()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var code = CodeOf(file);

            foreach (var severity in new[] { "Info", "Success", "Warning", "Error" })
            {
                // The first argument is the KEY. Anything ending in `.Message` there is the defect.
                var needle = "StatusMessage." + severity + "(";
                var at = 0;

                while ((at = code.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
                {
                    at += needle.Length;
                    var end = code.IndexOfAny([',', ')'], at);
                    if (end < 0)
                    {
                        break;
                    }

                    var first = code[at..end].Trim();
                    if (first.EndsWith(".Message", StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetFileName(file)}: {needle}{first}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "An exception's own sentence must never sit where a catalog KEY belongs — it would resolve to "
            + "itself and render correctly while nothing was localized:\n  " + string.Join("\n  ", offenders));
    }

    // ── ConfirmRequest ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⛔ No parameter of a message-carrying type has a defaulted WORD.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <c>ConfirmRequest</c> used to declare <c>string CancelLabel = "Cancel"</c>. A default parameter
    /// value is copied into every CALLER at compile time — exactly like a <c>const</c> — so no lookup could
    /// ever have reached it, and all three call sites relied on it. ⭐ Guarded on the DECLARATION, because a
    /// correctly resolved label and a baked-in one are indistinguishable at run time (#284).
    /// </remarks>
    [Fact]
    public void NoMember_HasADefaultedWord()
    {
        var offenders = new List<string>();
        var swept = 0;

        var assembly = typeof(StatusMessage).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            var members = type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Cast<MethodBase>()
                .Concat(type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));

            foreach (var member in members)
            {
                foreach (var parameter in member.GetParameters())
                {
                    if (parameter.ParameterType != typeof(string) || !parameter.HasDefaultValue)
                    {
                        continue;
                    }

                    swept++;

                    if (parameter.DefaultValue is string text && text.Length > 0)
                    {
                        offenders.Add($"{type.Name}.{member.Name}({parameter.Name} = \"{text}\")");
                    }
                }
            }
        }

        Assert.True(
            swept > 0,
            "No defaulted string parameter was examined at all — the sweep is measuring nothing, which is "
            + "how a guard passes for the wrong reason.");

        Assert.True(
            offenders.Count == 0,
            "A defaulted string parameter is pasted into every CALLER at compile time, exactly like a "
            + "const, so no lookup and no translation can ever reach it:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>⭐ The cancel label is a real, resolvable catalog entry rather than a baked-in word.</summary>
    [Fact]
    public void TheCancelLabel_ComesFromTheCatalog()
    {
        var request = new ConfirmRequest(
            ConfirmCatalog.ForgetSmtpTitle,
            ConfirmCatalog.ForgetSmtpMessage,
            ConfirmCatalog.ForgetSmtpAction);

        Assert.Equal(ConfirmCatalog.Cancel, request.CancelLabel);
        Assert.NotNull(Loc.Find(request.CancelLabel.Value));
        Assert.Equal("Cancel", Loc.Text(request.CancelLabel.Value));
    }

    // ── The sentences that are assembled at run time ─────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The eight sentences that could NOT be checked mechanically, checked here instead.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠ L8.2's acceptance criterion is that not one user-visible word changed, and it was verified by
    /// harvesting every string literal from the pre-migration revision and matching each catalog value
    /// against it: <b>145 of 153 matched verbatim</b>. The remainder cannot match any single literal,
    /// because they were ASSEMBLED at run time from a singular/plural opener, an optional clause and a
    /// shared tail. ⛔ That is exactly the shape rule 12 forbids handing to a translator, which is why each
    /// count now carries its whole sentence.</para>
    /// <para>⭐ So the eight are pinned HERE, against the text the old code produced — written out in full,
    /// deliberately, because a reconstruction that shared code with the thing it checks would prove
    /// nothing.</para>
    /// </remarks>
    [Theory]
    [InlineData(
        "Status.BlockedOne",
        "1 selected licence cannot be extended to this date, so the whole operation is held. Nothing is "
        + "issued in part \u2014 remove them from the selection, or choose a different target date.")]
    [InlineData(
        "Status.BlockedMany",
        "{0} selected licences cannot be extended to this date, so the whole operation is held. Nothing is "
        + "issued in part \u2014 remove them from the selection, or choose a different target date.")]
    [InlineData(
        "Status.BatchCompletedOne",
        "1 licence extended to {0}. {1} artifact(s) recorded as batch {2}. Nothing was written to disk "
        + "\u2014 export the files from the register when you are ready to send them.")]
    [InlineData(
        "Status.BatchCompletedMany",
        "{0} licences extended to {1}. {2} artifact(s) recorded as batch {3}. Nothing was written to disk "
        + "\u2014 export the files from the register when you are ready to send them.")]
    [InlineData(
        "Status.BatchCompletedOneWithFirstIssues",
        "1 licence extended to {0}. {1} artifact(s) recorded as batch {2}. {3} of them received a first "
        + "artifact. Nothing was written to disk \u2014 export the files from the register when you are "
        + "ready to send them.")]
    [InlineData(
        "Status.BatchCompletedManyWithFirstIssues",
        "{0} licences extended to {1}. {2} artifact(s) recorded as batch {3}. {4} of them received a first "
        + "artifact. Nothing was written to disk \u2014 export the files from the register when you are "
        + "ready to send them.")]
    [InlineData("Status.Verbatim", "{0}")]
    [InlineData(
        "Status.RegisterClosedAndMustRestart",
        "{0} \u26a0 The License Manager has closed its register and must be restarted. Your register on "
        + "disk was not left in a half-finished state.")]
    public void AnAssembledSentence_StillReadsExactlyAsItDidBefore(string key, string expected) =>
        Assert.Equal(expected, Loc.Text(key));

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static string CodeOf(string file) =>
        string.Join(
            Environment.NewLine,
            File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "src", "EmberTern.LicenseManager"), "*.cs",
                SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "EmberTern.LicenseManager.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
