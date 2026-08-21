using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.Views;

/// <summary>
/// The words the operating system's own file dialogs show — their titles and the file types they offer.
///
/// <para>⭐⭐ <b>These are user-visible text and were the last literals in code-behind.</b> A file picker is
/// as much part of the interface as a button; it simply happens to be drawn by Windows. ⛔ There is no
/// exemption for "the OS renders it".</para>
///
/// <para>⚠ <b>The brand stays inside the value.</b> <c>terminology.md</c> §4.4 makes <c>EmberTern</c> and
/// <c>EmberTern License Manager</c> technical contracts, but <i>"EmberTern licence"</i> and <i>"EmberTern
/// register backup"</i> are not brands — they are a brand plus a translatable noun. So the whole phrase is
/// a catalog value and the brand travels unchanged inside it, in every language. ⛔ Do not split a brand out
/// as an argument: that would hand the translator half a phrase and let word order be English's decision.</para>
///
/// <para>⛔ <see cref="JsonLines"/> is the exception that proves the rule: it is the NAME OF A FORMAT, like
/// <c>.eml</c> or <c>.jsonl</c>, and it is a catalog entry only so a translator can see it and decide to
/// leave it alone.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class FileTypeCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "FileType.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>Title of the dialog that writes a licence artifact.</summary>
    public static string SaveLicenceTitle => Word(nameof(SaveLicenceTitle));

    /// <summary>The licence artifact's file type.</summary>
    public static string Licence => Word(nameof(Licence));

    /// <summary>Title of the dialog that writes a composed message as a file.</summary>
    public static string SaveMessageTitle => Word(nameof(SaveMessageTitle));

    /// <summary>The message file's type.</summary>
    public static string EmailMessage => Word(nameof(EmailMessage));

    /// <summary>Title of the Storage window's save dialog, which serves two file types.</summary>
    public static string SaveTitle => Word(nameof(SaveTitle));

    /// <summary>Title of the dialog that picks a backup to restore.</summary>
    public static string ChooseBackupTitle => Word(nameof(ChooseBackupTitle));

    /// <summary>Title of the dialog that picks where a restore may write.</summary>
    public static string ChooseRestoreFolderTitle => Word(nameof(ChooseRestoreFolderTitle));

    /// <summary>The encrypted register backup's file type.</summary>
    public static string RegisterBackup => Word(nameof(RegisterBackup));

    /// <summary>The plain export's file type. ⛔ A format name — see the class remarks.</summary>
    public static string JsonLines => Word(nameof(JsonLines));
}
