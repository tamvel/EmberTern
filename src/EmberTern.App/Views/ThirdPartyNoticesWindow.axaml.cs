using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EmberTern.App.Views;

/// <summary>
/// Shows <c>THIRD-PARTY-NOTICES.txt</c> verbatim.
///
/// <para>The document is a real file at the repository root, embedded in this assembly and also copied beside
/// the executable (see the App csproj). Keeping it a file rather than strings in code means it is reviewable in
/// a diff and editable without touching the UI — and it is what the licences actually require to travel with
/// the application: MIT obliges its notice "in all copies", and <c>FirebirdSql.Data.FirebirdClient</c> is
/// IDPL 1.0, whose §3.6 wants a source-availability notice with an executable distribution.</para>
///
/// <para>⚠ It reads the EMBEDDED copy, not the one on disk beside the exe. A notices document that could go
/// missing — or be edited — after the build is not a notice; the embedded resource is part of the binary.</para>
/// </summary>
public partial class ThirdPartyNoticesWindow : Window
{
    /// <summary>The <c>LogicalName</c> declared in the csproj — explicit, so it cannot drift with the file's
    /// path relative to the project.</summary>
    private const string ResourceName = "EmberTern.THIRD-PARTY-NOTICES.txt";

    public ThirdPartyNoticesWindow()
    {
        InitializeComponent();
        NoticesText.Text = ReadNotices();
    }

    /// <summary>
    /// The embedded notices text, or a plain explanation when the resource is missing.
    ///
    /// <para>Internal so a test can assert the build really carries it — the failure this guards against is a
    /// packaging change that silently drops the resource, which no amount of UI testing would notice.</para>
    ///
    /// <para>⚠ A plain manifest resource rather than Avalonia's asset loader, deliberately: the asset loader
    /// needs a live Avalonia application, which would have confined both the reading and the testing of a
    /// plain text file to a headless UI session. This works anywhere.</para>
    /// </summary>
    internal static string ReadNotices()
    {
        try
        {
            using var stream = typeof(ThirdPartyNoticesWindow).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return UiStrings.ThirdPartyNoticesUnavailable;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            // Never throw from a window whose only job is to display text: an unreadable resource is reported
            // in place rather than taking the application down on a menu click.
            return UiStrings.ThirdPartyNoticesUnavailable;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
