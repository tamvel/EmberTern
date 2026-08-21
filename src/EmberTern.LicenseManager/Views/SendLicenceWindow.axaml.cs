using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Views;

/// <summary>
/// The Send licence window.
///
/// <para>⭐ Two platform services arrive here as delegates — the confirmation and the Save-As — so the view
/// model keeps no Avalonia types (Architecture rule 1) and every one of its decisions, refusals included,
/// is reachable in a test without a window.</para>
///
/// <para>⛔ <b>The confirmation is wired HERE and nowhere else.</b> With none attached the view model
/// refuses to send rather than proceeding, which is the half that matters: an outward-facing act must not
/// lose its guard because a view forgot to attach one.</para>
/// </summary>
public sealed partial class SendLicenceWindow : Window
{
    /// <summary>Creates the window.</summary>
    public SendLicenceWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SendLicenceViewModel model)
        {
            return;
        }

        model.Confirm = AskAsync;
        model.SaveFilePicker = SaveAsync;

        // ⚠ Unsubscribe first: DataContextChanged can fire more than once, and a second subscription would
        //   close the window twice.
        model.RequestClose -= OnRequestClose;
        model.RequestClose += OnRequestClose;
    }

    private void OnRequestClose() => Close();

    private async Task<bool> AskAsync(ConfirmRequest request) =>
        await new ConfirmDialog { DataContext = new ConfirmViewModel(request) }
            .ShowDialog<bool>(this)
            .ConfigureAwait(true);

    private async Task<string?> SaveAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = FileTypeCatalog.SaveMessageTitle,
            SuggestedFileName = suggestedName,
            DefaultExtension = EmlFileEmailSender.FileExtension.TrimStart('.'),
            FileTypeChoices = [EmlFileType],
        });

        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType EmlFileType => new(FileTypeCatalog.EmailMessage)
    {
        Patterns = ["*" + EmlFileEmailSender.FileExtension],
        MimeTypes = ["message/rfc822"],
    };
}
