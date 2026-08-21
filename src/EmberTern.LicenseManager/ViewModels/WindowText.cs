using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// What the Storage window says in C# — the sentences its view models compose rather than declare in XAML.
///
/// <para>⭐ It shares the <c>Storage.</c> prefix with the window's XAML keys on purpose: they are the same
/// window's vocabulary, and the split between "declared in markup" and "composed in a view model" is an
/// implementation detail a translator should never have to know about.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class StorageCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Storage.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>One sentence naming everything a backup will carry.</summary>
    /// <remarks>
    /// ⚠ The counts are handed in ALREADY FORMATTED, and the English keeps its <c>(s)</c> forms exactly as
    /// they were. ⏭ Turning those five into plural families changes the English, so it belongs to L8.5.
    /// </remarks>
    public static string BackupContents(
        string customers, string licences, string artifacts, string pointers, string auditEntries) =>
        Loc.Format(
            KeyPrefix + nameof(BackupContents),
            customers, licences, artifacts, pointers, auditEntries);

    /// <summary>What replacing the active register does. ⚠ It describes the rule; it does not enforce it.</summary>
    public static string ReplaceRule(string registerFileName) =>
        Loc.Format(KeyPrefix + nameof(ReplaceRule), registerFileName);

    /// <summary>What restoring elsewhere does. ⛔ The active register is not touched, not even a history entry.</summary>
    public static string RestoreElsewhereRule(string dataFolder) =>
        Loc.Format(KeyPrefix + nameof(RestoreElsewhereRule), dataFolder);

    /// <summary>The mode that replaces the working register, having preserved it first.</summary>
    public static string ModeReplaceActive => Word(nameof(ModeReplaceActive));

    /// <summary>⭐ The SAFE mode, and the default — the one that cannot touch the working register.</summary>
    public static string ModeRestoreElsewhere => Word(nameof(ModeRestoreElsewhere));
}

/// <summary>
/// What the Send licence window says about the message it is about to send.
/// </summary>
/// <remarks>
/// ⛔ Nothing here is part of the MESSAGE. The message's own words are the customer's language (D‑9) and
/// live in the e-mail templates; these are the operator's language and describe what is being sent.
/// </remarks>
[StringCatalog(KeyPrefix)]
internal static class SendCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Send.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>The attachment as the customer will see it — name, size and type.</summary>
    /// <remarks>⚠ The byte count arrives already formatted invariantly; only the unit is a word.</remarks>
    public static string Attachment(string fileName, string bytes, string mediaType) =>
        Loc.Format(KeyPrefix + nameof(Attachment), fileName, bytes, mediaType);

    /// <summary>Which language the message is written in, and where that is decided.</summary>
    public static string LanguageNote(string language) =>
        Loc.Format(KeyPrefix + nameof(LanguageNote), language);

    /// <summary>How it will travel, when a server is configured.</summary>
    public static string DeliveryDirect(string host, string port) =>
        Loc.Format(KeyPrefix + nameof(DeliveryDirect), host, port);

    /// <summary>Why it cannot travel directly, and what to do instead.</summary>
    public static string DeliveryNoServer => Word(nameof(DeliveryNoServer));

    /// <summary>⚠ Said in the window: the preview is the text body, and an HTML version travels with it.</summary>
    public static string PreviewNote => Word(nameof(PreviewNote));
}

/// <summary>
/// What the batch-renewal panel says about the plan it has measured.
///
/// <para>⭐⭐ The preview is TWO sentences, not one sentence with a clause bolted on. Before L8.4 the
/// first-issue notice was a fragment carrying its own leading space, appended after the period — legible in
/// English and unassignable to a translator. Each is now complete, and the JOIN is what puts the space
/// back, so the rendered line is unchanged (§55.5: every element complete, only the COUNT varies).</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class BatchCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Batch.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>Nothing is ticked and no target date is chosen yet.</summary>
    public static string TickAndChooseDate => Word(nameof(TickAndChooseDate));

    /// <summary>A date is chosen but nothing is ticked.</summary>
    public static string TickLicences => Word(nameof(TickLicences));

    /// <summary>What the plan would do. ⚠ The target day arrives already formatted (ISO, invariant).</summary>
    public static string WouldBeExtended(int qualifying, string targetDay) =>
        Loc.FormatCount(KeyPrefix + nameof(WouldBeExtended), qualifying, targetDay);

    /// <summary>⭐ The second sentence: how many of them have never been issued at all.</summary>
    public static string FirstIssues(int firstIssues) =>
        Loc.FormatCount(KeyPrefix + nameof(FirstIssues), firstIssues);
}

/// <summary>
/// The keystore window's heading — the one word-bearing member its view model composes.
/// </summary>
/// <remarks>
/// ⭐ The heading names the TASK, not the product: the title bar and its icon already name the application,
/// and the operator needs to know which of the two modes they are in.
/// </remarks>
[StringCatalog(KeyPrefix)]
internal static class UnlockCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Unlock.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>A keystore exists and is being opened.</summary>
    public static string HeadlineUnlock => Word(nameof(HeadlineUnlock));

    /// <summary>There is no keystore yet, so this is the key ceremony.</summary>
    public static string HeadlineCreate => Word(nameof(HeadlineCreate));
}
