using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Settings;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>"Not one visible word changed", proved by RENDERING rather than by review.</b>
///
/// <para>Most of L8.4 moved a literal into a resource verbatim, and the existing suite already reads many
/// of those off realised controls. This file covers the ones the migration RE-SHAPED, which is the only
/// place a mistake could hide: a sentence that used to be assembled from a fragment plus a shared tail, or
/// a pair of English arms that became a plural family. ⛔ Every expectation below is written out IN FULL,
/// because an expectation reconstructed by the same code it is checking proves nothing (§55.8).</para>
///
/// <para>⚠ The English these assert is the pre-migration text, character for character. ⛔ A red line here
/// is not a wording question — it means the migration changed what the operator reads, which L8.4 may not
/// do. Wording is L8.5's decision.</para>
/// </summary>
public sealed class MigratedTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // ── The plural families: English already had both arms, so both must still read the same ─────────

    [Theory]
    [InlineData(1, "1 seat")]
    [InlineData(5, "5 seats")]
    public void SeatsReadsExactlyAsItDidBefore(int seats, string expected) =>
        Assert.Equal(expected, RowCatalog.Seats(seats));

    [Theory]
    [InlineData(1, "1 licence.")]
    [InlineData(7, "7 licences.")]
    public void TheResultSummaryReadsExactlyAsItDidBefore(int count, string expected) =>
        Assert.Equal(expected, LicencesCatalog.Count(count));

    // ── The assembled sentences: a fragment plus a tail became whole sentences ────────────────────────

    [Theory]
    [InlineData(1, "1 licence selected.")]
    [InlineData(3, "3 licences selected.")]
    public void TheCheckedSummaryReadsExactlyAsItDidBefore(int count, string expected) =>
        Assert.Equal(expected, LicencesCatalog.Checked(count));

    [Theory]
    [InlineData(1, 1, "1 licence selected — 1 of them not shown by the current filters.")]
    [InlineData(5, 2, "5 licences selected — 2 of them not shown by the current filters.")]
    public void TheCheckedSummaryWithHiddenRowsReadsExactlyAsItDidBefore(
        int count, int hidden, string expected) =>
        Assert.Equal(expected, LicencesCatalog.CheckedWithHidden(count, hidden));

    [Fact]
    public void TheSelectionDetailReadsExactlyAsItDidBefore()
    {
        Assert.Equal("ACME — never issued.", LicencesCatalog.DetailNeverIssued("ACME"));

        Assert.Equal(
            "ACME — issued once, on 2026-01-02.",
            LicencesCatalog.DetailIssuedOnce("ACME", "2026-01-02"));

        Assert.Equal(
            "ACME — issued 3 times, last on 2026-01-02.",
            LicencesCatalog.DetailIssuedTimes("ACME", 3, "2026-01-02"));
    }

    [Theory]
    [InlineData(
        1,
        "1 issue on record. The current file is the one marked below; earlier ones were superseded, "
        + "never overwritten or deleted.")]
    [InlineData(
        4,
        "4 issues on record, all kept. The current file is the one marked below; earlier ones were "
        + "superseded, never overwritten or deleted.")]
    public void TheHistorySummaryReadsExactlyAsItDidBefore(int count, string expected) =>
        Assert.Equal(expected, HistoryCatalog.Summary(count));

    /// <summary>
    /// ⭐ The batch preview, including the JOIN that replaced a fragment carrying its own leading space.
    /// </summary>
    [Fact]
    public void TheBatchPreviewReadsExactlyAsItDidBefore()
    {
        Assert.Equal(
            "1 licence would be extended to 2027-01-01.",
            BatchCatalog.WouldBeExtended(1, "2027-01-01"));

        Assert.Equal(
            "6 licences would be extended to 2027-01-01.",
            BatchCatalog.WouldBeExtended(6, "2027-01-01"));

        // ⚠ The space belongs to the JOIN now, so the joined result is what has to match — the old code
        //   carried it inside the fragment.
        Assert.Equal(
            "6 licences would be extended to 2027-01-01. 1 of them has never been issued and would "
            + "receive its first artifact.",
            BatchCatalog.WouldBeExtended(6, "2027-01-01") + " " + BatchCatalog.FirstIssues(1));

        Assert.Equal(
            "6 licences would be extended to 2027-01-01. 3 of them have never been issued and would "
            + "receive their first artifact.",
            BatchCatalog.WouldBeExtended(6, "2027-01-01") + " " + BatchCatalog.FirstIssues(3));
    }

    // ── The multi-fragment sentences: several C# string literals became one resource value ────────────

    [Fact]
    public void TheStorageSentencesReadExactlyAsTheyDidBefore()
    {
        Assert.Equal(
            "1 customer(s) · 2 licence(s) · 3 issued artifact(s), the whole history · "
            + "4 current-artifact pointer(s) · 5 audit entries.",
            StorageCatalog.BackupContents("1", "2", "3", "4", "5"));

        Assert.Equal(
            "The current register will be preserved before restore. It is moved to "
            + "licenses.db.replaced-<date-time> in the same folder and is never deleted, so a failed "
            + "restore always leaves you the register you started with. ⚠ The License Manager closes when "
            + "this succeeds — start it again to work on the restored register.",
            StorageCatalog.ReplaceRule("licenses.db"));

        Assert.Equal(
            "The active register will not be changed. The backup is restored into a NEW, empty folder of "
            + "your choosing; nothing is written into C:\\data, and no history entry is added. For "
            + "recovering or inspecting a backup while you carry on working.",
            StorageCatalog.RestoreElsewhereRule("C:\\data"));
    }

    [Fact]
    public void TheSendSentencesReadExactlyAsTheyDidBefore()
    {
        Assert.Equal(
            "licence.etlic · 512 bytes · application/octet-stream",
            SendCatalog.Attachment("licence.etlic", "512", "application/octet-stream"));

        Assert.Equal(
            "Written in 'pl'. The language applies to every customer and is changed under "
            + "Settings ▸ E-mail.",
            SendCatalog.LanguageNote("pl"));

        Assert.Equal("Sending through mail.example.com:587.", SendCatalog.DeliveryDirect("mail.example.com", "587"));

        Assert.Equal(
            "No SMTP server is configured, so this message can only be saved as an .eml file and sent "
            + "from your own mail client. Add a server under Settings ▸ E-mail to send directly.",
            SendCatalog.DeliveryNoServer);

        Assert.Equal(
            "This is exactly what will be sent. An HTML version of the same message is included for mail "
            + "clients that show it; clients that strip HTML show the text above.",
            SendCatalog.PreviewNote);
    }

    [Fact]
    public void TheDeliverySummaryReadsExactlyAsItDidBefore()
    {
        Assert.Equal(
            "Not configured yet. A sender address is the minimum — without one, no message can be "
            + "composed at all.",
            ManagerSettingsCatalog.DeliveryNotConfigured);

        Assert.Equal(
            "File delivery only: a message can be saved as an .eml file and sent from your own mail "
            + "client. Add a server below to send directly.",
            ManagerSettingsCatalog.DeliveryFileOnly);

        Assert.Equal(
            "Direct sending and file delivery are both available.",
            ManagerSettingsCatalog.DeliveryBoth);
    }

    /// <summary>
    /// ⭐ The register's own refusals, which L8.2 left behind <c>e.Message</c> and L8.4 keyed.
    /// </summary>
    /// <remarks>
    /// ⚠ These were the operator's words all along — they reach the strip through
    /// <c>StatusMessage.FromError</c> and as an argument in the batch. The English must not have moved.
    /// </remarks>
    [Fact]
    public void TheRegisterRefusalsReadExactlyAsTheyDidBefore()
    {
        Assert.Equal(
            "Licence L-1 belongs to customer c-0001 and cannot be moved to c-0002. Artifacts already "
            + "issued for it carry the original customer's name, so the register would stop agreeing with "
            + "what was delivered.",
            Loc.Format(StatusCatalog.LicenceBelongsToAnotherCustomer.Value, "L-1", "c-0001", "c-0002"));

        Assert.Equal(
            "Licence L-1 appears twice in one batch. Two artifacts issued in the same operation would "
            + "carry the same iat, and the second could never replace the first in the field.",
            Loc.Format(StatusCatalog.LicenceAppearsTwiceInBatch.Value, "L-1"));

        Assert.Equal(
            "The snapshot holds 3 row(s) where the register holds 4. Nothing was written.",
            Loc.Format(StatusCatalog.SnapshotRowCountMismatch.Value, 3, 4));

        Assert.Equal(
            "The snapshot does not reproduce the register row for row. Nothing was written.",
            Loc.Text(StatusCatalog.SnapshotDoesNotReproduceRegister.Value));

        Assert.Equal("The register is inconsistent.", Loc.Text(StatusCatalog.RegisterIsInconsistent.Value));
    }

    /// <summary>
    /// ⭐⭐ A register-integrity refusal reaches the strip BY KEY, not as a raw English <c>e.Message</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>StatusMessage.FromError</c> sends anything that is not an <see cref="ILocalizedError"/> through
    /// <c>Status.Verbatim</c> — i.e. straight to the screen in English. This is the assertion that says
    /// <c>RegisterIntegrityException</c> is on the right side of that branch.
    /// </remarks>
    [Fact]
    public void ARegisterRefusalReachesTheStripByKey()
    {
        var refusal = new RegisterIntegrityException(
            StatusCatalog.SnapshotDoesNotReproduceRegister,
            "The snapshot does not reproduce the register row for row. Nothing was written.");

        var message = StatusMessage.FromError(refusal, MessageSeverity.Error);

        Assert.Equal(StatusCatalog.SnapshotDoesNotReproduceRegister, message.Key);
        Assert.NotEqual(StatusCatalog.Verbatim, message.Key);
    }

    [Fact]
    public void TheTokenSizeReadsExactlyAsItDidBefore() =>
        Assert.Equal("512 bytes as delivered", HistoryCatalog.TokenSizeAsDelivered("512"));

    [Fact]
    public void TheFileDialogWordsReadExactlyAsTheyDidBefore()
    {
        Assert.Equal("Save the licence", FileTypeCatalog.SaveLicenceTitle);
        Assert.Equal("EmberTern licence", FileTypeCatalog.Licence);
        Assert.Equal("Save the message", FileTypeCatalog.SaveMessageTitle);
        Assert.Equal("E-mail message", FileTypeCatalog.EmailMessage);
        Assert.Equal("Save", FileTypeCatalog.SaveTitle);
        Assert.Equal("Choose a register backup", FileTypeCatalog.ChooseBackupTitle);
        Assert.Equal("Choose a NEW, empty folder to restore into", FileTypeCatalog.ChooseRestoreFolderTitle);
        Assert.Equal("EmberTern register backup", FileTypeCatalog.RegisterBackup);
        Assert.Equal("JSON Lines", FileTypeCatalog.JsonLines);
    }

    // ── Every picker in the application binds a CAPTION, not a label ──────────────────────────────────

    /// <summary>
    /// ⭐⭐ Stated POSITIVELY: an <c>ItemTemplate</c> binds <c>Caption.Value</c>.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <c>APickerLabelLivenessTests</c> measured that a template bound straight to <c>Label</c>
    /// renders correctly and then freezes. This is the sweep that stops the next picker from being written
    /// that way — the failure has no binding error, no exception and no visible symptom in English.</para>
    ///
    /// <para>⛔ The rule is "every template binds a caption", never "every template except the two whose
    /// words happen not to move": a negative rule leaks, and in this repository it has leaked twice
    /// (`product-polish.md` M2b rule 11).</para>
    /// </remarks>
    [Fact]
    public void NoItemTemplate_BindsALabelDirectly()
    {
        var offenders = new List<string>();
        var judged = 0;

        foreach (var view in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot, "src", "EmberTern.LicenseManager", "Views"), "*.axaml"))
        {
            // ⚠ XML comments are stripped first: a rule STATED in prose is not the rule being broken, and
            //   this sweep read MainWindow's own explanatory comment as a violation on its first run — the
            //   same shape as #396 and as L8.3's SizeToContent false positive.
            var markup = Regex.Replace(File.ReadAllText(view), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            foreach (Match match in Regex.Matches(markup, @"\{Binding\s+(Label|Caption\.Value)\}"))
            {
                judged++;

                if (!match.Groups[1].Value.Equals("Caption.Value", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(view)}: {match.Value}");
                }
            }
        }

        Assert.True(
            judged > 0,
            "The sweep judged nothing at all — either the templates moved or the pattern stopped matching.");

        Assert.True(
            offenders.Count == 0,
            "A picker binds an option's Label directly. An option record raises no PropertyChanged, so the "
            + "control renders correctly on load and then freezes in that language. Bind Caption.Value:\n  "
            + string.Join("\n  ", offenders));
    }

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
