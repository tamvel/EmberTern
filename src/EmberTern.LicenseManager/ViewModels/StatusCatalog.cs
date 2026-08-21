using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// Every sentence the message strip can say, as KEYS.
///
/// <para>&#9733;&#9733; <b>A catalog of <see cref="MessageKey"/>, not of words.</b> Its siblings
/// (<c>ManagerSettingsCatalog</c>) hand back resolved <c>string</c>s, because a label is rendered where it
/// is read. A status message is not: it is RAISED at one moment and READ at another, possibly in a
/// different language, so what the producer hands over must be the identity of the sentence and its
/// arguments &#8212; never the sentence. <see cref="StatusMessage.Text"/> does the resolving, at the moment
/// of the read.</para>
///
/// <para>&#9733; <b>ONE catalog rather than one per view model, and that is a measured choice.</b> Four of
/// these sentences are said by two different view models &#8212; "The e-mail settings could not be read",
/// "The file could not be written", "The two passphrases do not match" and the missing-confirmation notice.
/// Splitting per area would have given each of those two homes and two translations, which is the same
/// duplication that decision D&#8209;5's prefix mechanism exists to prevent. &#9888; The regions below carry
/// the thematic split instead; if this file ever approaches the "450-property class" D&#8209;5 warns about,
/// split it by prefix, not by copying a shared sentence.</para>
///
/// <para>&#9888;&#9888; <b>Every member is a PROPERTY returning a key.</b> A <c>const MessageKey</c> is
/// impossible, but a <c>static readonly</c> one would compile &#8212; and would be harmless only by luck,
/// since a key does not move with the language. &#9733; It is still forbidden, for consistency with the
/// word catalogs and so no reader has to work out which kind of member they are looking at.</para>
///
/// <para>&#9888; <b>Not every sentence here is reached from a <c>StatusMessage.*</c> call.</b> Some are
/// carried by an exception thrown deep in the application and resolved where it is caught &#8212; see
/// <c>LocalizedException</c>. That is the same catalog on purpose: the operator cannot tell which of the
/// two paths produced the line they are reading, so neither should the translator.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class StatusCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Status.";

    /// <summary>Mints one of this catalog's own keys.</summary>
    /// <remarks>
    /// &#9888; The argument is always <c>nameof(TheMember)</c>, never a typed-out string: the member name
    /// IS the key, so there is one owner and nothing to keep in step. Same convention as
    /// <c>ManagerSettingsCatalog.Word</c>.
    /// </remarks>
    private static MessageKey Key(string member) => new(KeyPrefix + member);

    /// <summary>Signing with key {0}.</summary>
    public static MessageKey SigningWithKey => Key(nameof(SigningWithKey));

    /// <summary>Select a licence in the list first.</summary>
    public static MessageKey SelectLicenceInListFirst => Key(nameof(SelectLicenceInListFirst));

    /// <summary>Licence {0} names customer {1}, which is not in the register.</summary>
    public static MessageKey LicenceNamesUnknownCustomer => Key(nameof(LicenceNamesUnknownCustomer));

    /// <summary>New customer. The name is required — it is what gets signed.</summary>
    public static MessageKey NewCustomerHint => Key(nameof(NewCustomerHint));

    /// <summary>A customer name is required. It is signed into every licence and shown in their EmberTern.</summary>
    public static MessageKey CustomerNameRequired => Key(nameof(CustomerNameRequired));

    /// <summary>Saved {0}.</summary>
    public static MessageKey CustomerSaved => Key(nameof(CustomerSaved));

    /// <summary>Select or save a customer first.</summary>
    public static MessageKey SelectOrSaveCustomerFirst => Key(nameof(SelectOrSaveCustomerFirst));

    /// <summary>New licence. Save the terms, then issue.</summary>
    public static MessageKey NewLicenceHint => Key(nameof(NewLicenceHint));

    /// <summary>Select a customer first.</summary>
    public static MessageKey SelectCustomerFirst => Key(nameof(SelectCustomerFirst));

    /// <summary>Saved licence {0}….</summary>
    public static MessageKey LicenceSaved => Key(nameof(LicenceSaved));

    /// <summary>Select a saved licence to issue.</summary>
    public static MessageKey SelectSavedLicenceToIssue => Key(nameof(SelectSavedLicenceToIssue));

    /// <summary>Choose why this licence is being issued again before signing a new artifact.</summary>
    public static MessageKey ChooseIssueReasonFirst => Key(nameof(ChooseIssueReasonFirst));

    /// <summary>The licence could not be issued: {0}</summary>
    public static MessageKey LicenceNotIssued => Key(nameof(LicenceNotIssued));

    /// <summary>Issued and recorded for {0}. Not saved to disk — it can be exported from the register at any ...</summary>
    public static MessageKey IssuedAndRecorded => Key(nameof(IssuedAndRecorded));

    /// <summary>Issued for {0} and saved to {1}.</summary>
    public static MessageKey IssuedAndSaved => Key(nameof(IssuedAndSaved));

    /// <summary>Issued and recorded, but the file could not be written: {0}</summary>
    public static MessageKey IssuedButFileNotWritten => Key(nameof(IssuedButFileNotWritten));

    /// <summary>Select a licence.</summary>
    public static MessageKey SelectLicence => Key(nameof(SelectLicence));

    /// <summary>This licence has never been issued.</summary>
    public static MessageKey LicenceNeverIssued => Key(nameof(LicenceNeverIssued));

    /// <summary>Exported the stored artifact to {0}.</summary>
    public static MessageKey ArtifactExported => Key(nameof(ArtifactExported));

    /// <summary>Select a licence to send.</summary>
    public static MessageKey SelectLicenceToSend => Key(nameof(SelectLicenceToSend));

    /// <summary>Sending e-mail is only available on Windows, because the SMTP password is protected with Wind...</summary>
    public static MessageKey EmailIsWindowsOnly => Key(nameof(EmailIsWindowsOnly));

    /// <summary>This licence has never been issued, so there is no artifact to send. Issue it first.</summary>
    public static MessageKey LicenceNeverIssuedNothingToSend => Key(nameof(LicenceNeverIssuedNothingToSend));

    /// <summary>The e-mail settings could not be read: {0}</summary>
    public static MessageKey EmailSettingsNotRead => Key(nameof(EmailSettingsNotRead));

    /// <summary>E-mail is not configured yet. Open Settings ▸ E-mail and enter at least a sender address.</summary>
    public static MessageKey EmailNotConfigured => Key(nameof(EmailNotConfigured));

    /// <summary>Select an issue from the history first.</summary>
    public static MessageKey SelectIssueFromHistoryFirst => Key(nameof(SelectIssueFromHistoryFirst));

    /// <summary>Exported issue {0} of {1} to {2}.</summary>
    public static MessageKey IssueExported => Key(nameof(IssueExported));

    /// <summary>The file could not be written: {0}</summary>
    public static MessageKey FileNotWritten => Key(nameof(FileNotWritten));

    /// <summary>Use a long passphrase for the backup — six generated words, kept in a password manager. It ca...</summary>
    public static MessageKey BackupPassphraseHint => Key(nameof(BackupPassphraseHint));

    /// <summary>The two passphrases do not match.</summary>
    public static MessageKey PassphrasesDoNotMatch => Key(nameof(PassphrasesDoNotMatch));

    /// <summary>Encrypted backup written to {0} — {1} customer(s), {2} licence(s), {3} artifact(s) and {4} au...</summary>
    public static MessageKey BackupWritten => Key(nameof(BackupWritten));

    /// <summary>The backup could not be written: {0}</summary>
    public static MessageKey BackupNotWritten => Key(nameof(BackupNotWritten));

    /// <summary>Plain JSONL export written to {0} — {1} line(s). ⛔ NOT encrypted: it carries every issued lic...</summary>
    public static MessageKey JsonlExportWritten => Key(nameof(JsonlExportWritten));

    /// <summary>The export could not be written: {0}</summary>
    public static MessageKey ExportNotWritten => Key(nameof(ExportNotWritten));

    /// <summary>The register could not be closed, so it was not replaced. Nothing has been changed.</summary>
    public static MessageKey RegisterNotClosed => Key(nameof(RegisterNotClosed));

    /// <summary>The active register was replaced — {0} customer(s), {1} licence(s), {2} artifact(s) and {3} a...</summary>
    public static MessageKey RegisterReplaced => Key(nameof(RegisterReplaced));

    /// <summary>Restored into {0} — {1} customer(s), {2} licence(s), {3} artifact(s) and {4} audit entries, a...</summary>
    public static MessageKey RestoredElsewhere => Key(nameof(RestoredElsewhere));

    /// <summary>That backup could not be read: {0}</summary>
    public static MessageKey BackupNotRead => Key(nameof(BackupNotRead));

    /// <summary>That backup was taken on {0} UTC (register schema {1}). Enter its passphrase to restore it.</summary>
    public static MessageKey BackupInspected => Key(nameof(BackupInspected));

    /// <summary>The data folder could not be opened: {0}</summary>
    public static MessageKey DataFolderNotOpened => Key(nameof(DataFolderNotOpened));

    /// <summary>The e-mail settings could not be saved: {0}</summary>
    public static MessageKey EmailSettingsNotSaved => Key(nameof(EmailSettingsNotSaved));

    /// <summary>Reloaded the saved settings.</summary>
    public static MessageKey SettingsReloaded => Key(nameof(SettingsReloaded));

    /// <summary>This action needs a confirmation and none could be shown, so nothing was changed.</summary>
    public static MessageKey ConfirmationUnavailableNothingChanged => Key(nameof(ConfirmationUnavailableNothingChanged));

    /// <summary>The e-mail settings could not be deleted: {0}</summary>
    public static MessageKey EmailSettingsNotDeleted => Key(nameof(EmailSettingsNotDeleted));

    /// <summary>E-mail is no longer configured.</summary>
    public static MessageKey EmailNoLongerConfigured => Key(nameof(EmailNoLongerConfigured));

    /// <summary>'{0}' does not look like an e-mail address. Enter the address the test message should arrive at.</summary>
    public static MessageKey NotAnEmailAddress => Key(nameof(NotAnEmailAddress));

    /// <summary>There is no SMTP host to test. Enter a server above — file delivery needs no test, because it...</summary>
    public static MessageKey NoSmtpHostToTest => Key(nameof(NoSmtpHostToTest));

    /// <summary>This action needs a confirmation and none could be shown, so nothing was sent.</summary>
    public static MessageKey ConfirmationUnavailableNothingSent => Key(nameof(ConfirmationUnavailableNothingSent));

    /// <summary>Sending a test message to {0}…</summary>
    public static MessageKey SendingTestMessage => Key(nameof(SendingTestMessage));

    /// <summary>Test email sent successfully to {0} through {1}. The SMTP configuration works.</summary>
    public static MessageKey TestEmailSent => Key(nameof(TestEmailSent));

    /// <summary>The test message could not be sent: {0} The SMTP configuration on this page did not work — ch...</summary>
    public static MessageKey TestMessageNotSent => Key(nameof(TestMessageNotSent));

    /// <summary>Enter the keystore passphrase.</summary>
    public static MessageKey EnterKeystorePassphrase => Key(nameof(EnterKeystorePassphrase));

    /// <summary>That passphrase does not open the keystore. Check it and try again.</summary>
    public static MessageKey PassphraseDoesNotOpenKeystore => Key(nameof(PassphraseDoesNotOpenKeystore));

    /// <summary>The file at {0} is not an EmberTern keystore.</summary>
    public static MessageKey NotAKeystore => Key(nameof(NotAKeystore));

    /// <summary>The keystore was written by a newer License Manager. Update this application.</summary>
    public static MessageKey KeystoreFromNewerBuild => Key(nameof(KeystoreFromNewerBuild));

    /// <summary>The keystore is damaged. Restore it from an offline backup and verify the restore.</summary>
    public static MessageKey KeystoreDamaged => Key(nameof(KeystoreDamaged));

    /// <summary>The keystore could not be opened ({0}).</summary>
    public static MessageKey KeystoreNotOpened => Key(nameof(KeystoreNotOpened));

    /// <summary>The keystore could not be read: {0}</summary>
    public static MessageKey KeystoreNotRead => Key(nameof(KeystoreNotRead));

    /// <summary>A key id is required — it travels in every licence.</summary>
    public static MessageKey KeyIdRequired => Key(nameof(KeyIdRequired));

    /// <summary>Use a long passphrase — six generated words, kept in a password manager and on paper. It cann...</summary>
    public static MessageKey NewKeyPassphraseHint => Key(nameof(NewKeyPassphraseHint));

    /// <summary>The keystore could not be written: {0}</summary>
    public static MessageKey KeystoreNotWritten => Key(nameof(KeystoreNotWritten));

    /// <summary>Sending to {0}…</summary>
    public static MessageKey SendingTo => Key(nameof(SendingTo));

    /// <summary>Sent to {0} through {1}. Recorded in the audit log.</summary>
    public static MessageKey SentThrough => Key(nameof(SentThrough));

    /// <summary>The message was not sent: {0} The attempt is recorded in the audit log. You can save the mess...</summary>
    public static MessageKey MessageNotSent => Key(nameof(MessageNotSent));

    /// <summary>No place to save was offered, so nothing was written.</summary>
    public static MessageKey NoSaveLocationOffered => Key(nameof(NoSaveLocationOffered));

    /// <summary>Saved to {0}. Open it in your mail client and send it — the licence is attached and the messa...</summary>
    public static MessageKey MessageSavedToFile => Key(nameof(MessageSavedToFile));

    /// <summary>The selection or the target date changed since this preview was built, so nothing was issued....</summary>
    public static MessageKey PreviewOutOfDate => Key(nameof(PreviewOutOfDate));

    /// <summary>Nothing was issued and the register is unchanged. {0}</summary>
    public static MessageKey NothingIssuedRegisterUnchanged => Key(nameof(NothingIssuedRegisterUnchanged));

    /// <summary>EmberTern would accept it: valid until {0}, licensed to {1}.</summary>
    public static MessageKey VerdictValid => Key(nameof(VerdictValid));

    /// <summary>EmberTern would accept it, but it is past its expiry and inside the grace period.</summary>
    public static MessageKey VerdictGrace => Key(nameof(VerdictGrace));

    /// <summary>EmberTern would report it as expired.</summary>
    public static MessageKey VerdictExpired => Key(nameof(VerdictExpired));

    /// <summary>EmberTern would report it as not yet valid.</summary>
    public static MessageKey VerdictNotYetValid => Key(nameof(VerdictNotYetValid));

    /// <summary>EmberTern would refuse it ({0}).</summary>
    public static MessageKey VerdictRefused => Key(nameof(VerdictRefused));

    /// <summary>Copied {0} to the clipboard.</summary>
    public static MessageKey CopiedToClipboard => Key(nameof(CopiedToClipboard));

    /// <summary>Tick at least one licence to extend.</summary>
    public static MessageKey TickAtLeastOneLicence => Key(nameof(TickAtLeastOneLicence));

    /// <summary>1 selected licence cannot be extended to this date, so the whole operation is held. Nothing i...</summary>
    /// <remarks>
    /// ⭐ A counted FAMILY since L8.5 (C‑1): English has two arms and Polish needs three, so the pair
    /// <c>…One</c> / <c>…Many</c> could not survive translation. ⚠ There is no flat entry for this key —
    /// its variants live under <c>.one</c> / <c>.other</c> in English and <c>.one</c> / <c>.few</c> /
    /// <c>.many</c> in Polish.
    /// </remarks>
    public static MessageKey Blocked => Key(nameof(Blocked));

    /// <summary>{0} selected licences cannot be extended to this date, so the whole operation is held. Nothin...</summary>


    /// <summary>Licence {0} refers to customer {1}, which the register does not hold. Nothing was issued.</summary>
    public static MessageKey LicenceRefersToUnknownCustomer => Key(nameof(LicenceRefersToUnknownCustomer));

    /// <summary>1 licence extended to {0}. {1} artifact(s) recorded as batch {2}. Nothing was written to disk...</summary>
    /// <remarks>⭐ A counted family since L8.5 — see <see cref="Blocked"/>.</remarks>
    public static MessageKey BatchCompleted => Key(nameof(BatchCompleted));

    /// <summary>{0} licences extended to {1}. {2} artifact(s) recorded as batch {3}. Nothing was written to d...</summary>


    /// <summary>1 licence extended to {0}. {1} artifact(s) recorded as batch {2}. {3} of them received a firs...</summary>
    /// <remarks>⭐ A counted family since L8.5 — see <see cref="Blocked"/>.</remarks>
    public static MessageKey BatchCompletedWithFirstIssues => Key(nameof(BatchCompletedWithFirstIssues));

    /// <summary>{0} licences extended to {1}. {2} artifact(s) recorded as batch {3}. {4} of them received a f...</summary>


    /// <summary>{0}</summary>
    public static MessageKey Verbatim => Key(nameof(Verbatim));

    /// <summary>A keystore already exists. Creating a second signing key would leave every licence already is...</summary>
    public static MessageKey KeystoreAlreadyExists => Key(nameof(KeystoreAlreadyExists));

    /// <summary>These settings carry no SMTP host, so nothing can be sent directly. Save the message as an .e...</summary>
    public static MessageKey SettingsCarryNoSmtpHost => Key(nameof(SettingsCarryNoSmtpHost));

    /// <summary>The message language '{0}' is not one this version can write.</summary>
    public static MessageKey SmtpUnknownMessageLanguage => Key(nameof(SmtpUnknownMessageLanguage));

    /// <summary>A sender address is required — it is what the customer replies to.</summary>
    public static MessageKey SmtpSenderRequired => Key(nameof(SmtpSenderRequired));

    /// <summary>The sender address does not look like an e-mail address: {0}</summary>
    public static MessageKey SmtpSenderNotAnAddress => Key(nameof(SmtpSenderNotAnAddress));

    /// <summary>The port must be between 1 and 65535, not {0}.</summary>
    public static MessageKey SmtpPortOutOfRange => Key(nameof(SmtpPortOutOfRange));

    /// <summary>A username cannot be used without STARTTLS — the password would travel unencrypted. Either en...</summary>
    public static MessageKey SmtpUsernameNeedsStartTls => Key(nameof(SmtpUsernameNeedsStartTls));

    /// <summary>A password without a username cannot be used. Enter the account that signs in.</summary>
    public static MessageKey SmtpPasswordNeedsUsername => Key(nameof(SmtpPasswordNeedsUsername));

    /// <summary>Credentials were entered but no server. Enter the SMTP host, or clear the credentials and del...</summary>
    public static MessageKey SmtpCredentialsWithoutServer => Key(nameof(SmtpCredentialsWithoutServer));

    /// <summary>That passphrase does not open the backup — or the file was modified after it was written. Che...</summary>
    public static MessageKey BackupWrongPassphrase => Key(nameof(BackupWrongPassphrase));

    /// <summary>That file is not an EmberTern register backup. ⚠ The keystore is a different file with a diff...</summary>
    public static MessageKey BackupNotABackup => Key(nameof(BackupNotABackup));

    /// <summary>That backup was written by a newer License Manager. Update this application to read it.</summary>
    public static MessageKey BackupFromNewerBuild => Key(nameof(BackupFromNewerBuild));

    /// <summary>That backup uses an encryption scheme this build does not implement.</summary>
    public static MessageKey BackupUnsupportedScheme => Key(nameof(BackupUnsupportedScheme));

    /// <summary>That backup file is damaged and cannot be read. Try another copy.</summary>
    public static MessageKey BackupCorrupt => Key(nameof(BackupCorrupt));

    /// <summary>That backup could not be opened ({0}).</summary>
    public static MessageKey BackupNotOpened => Key(nameof(BackupNotOpened));

    /// <summary>The restore could not be completed: {0}</summary>
    public static MessageKey RestoreNotCompleted => Key(nameof(RestoreNotCompleted));

    /// <summary>{0} {1}</summary>
    public static MessageKey RestoreRefusedWithProblems => Key(nameof(RestoreRefusedWithProblems));

    /// <summary>{0} ⚠ The License Manager has closed its register and must be restarted. Your register on dis...</summary>
    public static MessageKey RegisterClosedAndMustRestart => Key(nameof(RegisterClosedAndMustRestart));

    /// <summary>The backup restored into a register that disagrees with itself, so nothing was written.</summary>
    public static MessageKey RestoreBackupInconsistent => Key(nameof(RestoreBackupInconsistent));

    /// <summary>The register is still open, so it was not replaced. Close the License Manager's register firs...</summary>
    public static MessageKey RestoreRegisterStillOpen => Key(nameof(RestoreRegisterStillOpen));

    /// <summary>The register file cannot be opened for replacement, so nothing was changed.</summary>
    public static MessageKey RestoreRegisterNotOpenable => Key(nameof(RestoreRegisterNotOpenable));

    /// <summary>The existing register could not be kept safe, so it was not replaced. Nothing has been changed.</summary>
    public static MessageKey RestorePreservedCopyFailed => Key(nameof(RestorePreservedCopyFailed));

    /// <summary>The restored register failed its final check, so the previous one was put back.</summary>
    public static MessageKey RestoreFinalCheckFailed => Key(nameof(RestoreFinalCheckFailed));

    /// <summary>The restored register does not match what was verified, so the previous one was put back.</summary>
    public static MessageKey RestoreDoesNotMatchVerified => Key(nameof(RestoreDoesNotMatchVerified));

    /// <summary>Choose a folder to restore into.</summary>
    public static MessageKey RestoreChooseFolder => Key(nameof(RestoreChooseFolder));

    /// <summary>That is not a usable folder path.</summary>
    public static MessageKey RestoreNotAUsableFolderPath => Key(nameof(RestoreNotAUsableFolderPath));

    /// <summary>That path is a file. A restore needs a folder of its own.</summary>
    public static MessageKey RestorePathIsAFile => Key(nameof(RestorePathIsAFile));

    /// <summary>A restore never writes into the active register's folder. Choose a new, empty folder; you can...</summary>
    public static MessageKey RestoreNeverIntoActiveFolder => Key(nameof(RestoreNeverIntoActiveFolder));

    /// <summary>That folder is not empty. A restore always creates its register in a folder of its own, so no...</summary>
    public static MessageKey RestoreFolderNotEmpty => Key(nameof(RestoreFolderNotEmpty));

    /// <summary>The backup decrypted, but what came out is not a register this build can open.</summary>
    public static MessageKey RestoreBackupIsNotARegister => Key(nameof(RestoreBackupIsNotARegister));

    /// <summary>Licence {0} has artifacts but no current one is marked.</summary>
    public static MessageKey IntegrityNoCurrentArtifact => Key(nameof(IntegrityNoCurrentArtifact));

    /// <summary>Licence {0} marks a current artifact that does not belong to it.</summary>
    public static MessageKey IntegrityCurrentNotOwned => Key(nameof(IntegrityCurrentNotOwned));

    /// <summary>Licence {0} marks an artifact that is not its newest.</summary>
    public static MessageKey IntegrityCurrentNotNewest => Key(nameof(IntegrityCurrentNotNewest));

    /// <summary>Licence {0} belongs to a customer that is not in the register.</summary>
    public static MessageKey IntegrityCustomerMissing => Key(nameof(IntegrityCustomerMissing));

    /// <summary>The e-mail settings file could not be read: {0}</summary>
    public static MessageKey SmtpFileNotRead => Key(nameof(SmtpFileNotRead));

    /// <summary>The e-mail settings file is empty.</summary>
    public static MessageKey SmtpFileEmpty => Key(nameof(SmtpFileEmpty));

    /// <summary>These e-mail settings were written by a newer License Manager (version {0}). Update this appl...</summary>
    public static MessageKey SmtpFileFromNewerBuild => Key(nameof(SmtpFileFromNewerBuild));

    /// <summary>The stored password could not be decrypted. It is protected for this Windows account on this ...</summary>
    public static MessageKey SmtpPasswordNotDecrypted => Key(nameof(SmtpPasswordNotDecrypted));

    /// <summary>Saved. Messages can be delivered as .eml files.</summary>
    public static MessageKey SavedFileDelivery => Key(nameof(SavedFileDelivery));

    /// <summary>Saved. Messages will be sent through {0}.</summary>
    public static MessageKey SavedThroughHost => Key(nameof(SavedThroughHost));

    /// <summary>The password is encrypted with Windows DPAPI for this Windows account on this computer. It ca...</summary>
    public static MessageKey DpapiProtectionNote => Key(nameof(DpapiProtectionNote));

    /// <summary>The e-mail settings could not be read.</summary>
    public static MessageKey EmailSettingsNotReadShort => Key(nameof(EmailSettingsNotReadShort));

    /// <summary>Choose why this licence is being issued.</summary>
    public static MessageKey ReasonRequired => Key(nameof(ReasonRequired));

    /// <summary>This licence has never been issued, so the first artifact can only be the initial one.</summary>
    public static MessageKey ReasonMustBeInitial => Key(nameof(ReasonMustBeInitial));

    /// <summary>This licence has already been issued, so a further artifact cannot be the initial one. Choose...</summary>
    public static MessageKey ReasonNotInitialAgain => Key(nameof(ReasonNotInitialAgain));

    /// <summary>The expiry has not moved since the last issue, so this is not a renewal. Change the expiry an...</summary>
    public static MessageKey ReasonExpiryNotMoved => Key(nameof(ReasonExpiryNotMoved));

    /// <summary>Nothing but the expiry differs from the last issue, so there is no terms change to record. Pr...</summary>
    public static MessageKey ReasonNoTermsChange => Key(nameof(ReasonNoTermsChange));

    /// <summary>'{0}' is not one of the recorded issuing reasons.</summary>
    public static MessageKey ReasonNotRecorded => Key(nameof(ReasonNotRecorded));

    /// <summary>A licence must carry at least one seat.</summary>
    public static MessageKey TermsSeatsRequired => Key(nameof(TermsSeatsRequired));

    /// <summary>A start date is required. Pick one from the calendar, or type it into the field.</summary>
    public static MessageKey TermsStartDateRequired => Key(nameof(TermsStartDateRequired));

    /// <summary>An expiry date is required. Pick one from the calendar, or type it into the field.</summary>
    public static MessageKey TermsExpiryDateRequired => Key(nameof(TermsExpiryDateRequired));

    /// <summary>{0} has no e-mail address. Add one on the Customer page, or export the licence to a file and ...</summary>
    public static MessageKey ComposeNoCustomerEmail => Key(nameof(ComposeNoCustomerEmail));

    /// <summary>The customer's e-mail address does not look like one: {0}</summary>
    public static MessageKey ComposeCustomerEmailInvalid => Key(nameof(ComposeCustomerEmailInvalid));

    /// <summary>There is no usable sender address in the e-mail settings, and a message has to come from some...</summary>
    public static MessageKey ComposeNoSenderAddress => Key(nameof(ComposeNoSenderAddress));

    /// <summary>The stored artifact for licence {0} could not be read, so no message can describe it. Inspect...</summary>
    public static MessageKey ComposeArtifactUnreadable => Key(nameof(ComposeArtifactUnreadable));

    /// <summary>The e-mail settings could not be read, so nothing can be sent.</summary>
    public static MessageKey EmailSettingsNotReadNothingSent => Key(nameof(EmailSettingsNotReadNothingSent));

    /// <summary>The stored SMTP password could not be read on this Windows account, so signing in will probab...</summary>
    public static MessageKey SmtpPasswordUnreadableSendWillFail => Key(nameof(SmtpPasswordUnreadableSendWillFail));

    /// <summary>The target date is not after this licence's start date ({0}). Choose a later target date, or ...</summary>
    public static MessageKey BlockerTargetBeforeStart => Key(nameof(BlockerTargetBeforeStart));

    /// <summary>Already valid until {0}, so the target date would not extend it. Choose a later target date, ...</summary>
    public static MessageKey BlockerAlreadyValidUntil => Key(nameof(BlockerAlreadyValidUntil));

    /// <summary>The expiry must be after the start date.</summary>
    public static MessageKey TermsExpiryMustFollowStart => Key(nameof(TermsExpiryMustFollowStart));

    /// <summary>Saved licence {0}….</summary>
    public static MessageKey LicenceSavedShort => Key(nameof(LicenceSavedShort));

    // ── The register's own integrity refusals (L8.4) ─────────────────────────────────────────────
    //
    // ⚠⚠ These are OURS, and L8.2 left them as English behind `e.Message`. `RegisterIntegrityException`
    //    reaches the strip at two call sites — `StorageViewModel` through `StatusMessage.FromError`, and
    //    `BatchRenewalViewModel` as an argument — so the sentences are the operator's to read in their
    //    own language. ⛔ The exception keeps its English text for diagnostics; the display path is here.

    /// <summary>Licence {0} belongs to customer {1} and cannot be moved to {2}. …</summary>
    public static MessageKey LicenceBelongsToAnotherCustomer => Key(nameof(LicenceBelongsToAnotherCustomer));

    /// <summary>The artifact for licence {0} carries iat {1}, which does not come after {2}. …</summary>
    public static MessageKey ArtifactIatNotAfterCurrent => Key(nameof(ArtifactIatNotAfterCurrent));

    /// <summary>Licence {0} appears twice in one batch. …</summary>
    public static MessageKey LicenceAppearsTwiceInBatch => Key(nameof(LicenceAppearsTwiceInBatch));

    /// <summary>A batch unit pairs the terms of licence {0} with an artifact for {1}.</summary>
    public static MessageKey BatchUnitPairsMismatchedTerms => Key(nameof(BatchUnitPairsMismatchedTerms));

    /// <summary>The register has integrity problems, so it was not backed up: {0}</summary>
    /// <remarks>⭐ {0} is a <c>LocalizedSentences</c> — a variable number of our own live sentences.</remarks>
    public static MessageKey RegisterHasIntegrityProblems => Key(nameof(RegisterHasIntegrityProblems));

    /// <summary>The snapshot holds {0} row(s) where the register holds {1}. Nothing was written.</summary>
    public static MessageKey SnapshotRowCountMismatch => Key(nameof(SnapshotRowCountMismatch));

    /// <summary>The snapshot does not reproduce the register row for row. Nothing was written.</summary>
    public static MessageKey SnapshotDoesNotReproduceRegister => Key(nameof(SnapshotDoesNotReproduceRegister));

    /// <summary>The register is inconsistent.</summary>
    /// <remarks>⚠ The parameterless-constructor fallback, required by the exception design guidelines.</remarks>
    public static MessageKey RegisterIsInconsistent => Key(nameof(RegisterIsInconsistent));
}
