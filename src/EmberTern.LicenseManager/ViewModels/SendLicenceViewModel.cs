using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The Send licence window: what is about to be sent, to whom, and the one click that sends it.
///
/// <para>⭐⭐ <b>THE PREVIEW IS THE MESSAGE.</b> This view model is handed a composed
/// <see cref="LicenseMessage"/> and shows THAT value; when the operator confirms, that same value is what
/// goes to the sender. ⛔ Nothing is re-composed on the way out — a preview built from one composition and
/// a send built from another is a window that can lie, and the whole point of showing the message first is
/// that it cannot.</para>
///
/// <para>⭐ <b>ONE licence, ONE customer, per send</b> (user directive). There is no bulk path here and no
/// recipient list: §14.1 allows bulk only behind a full recipient list and one explicit confirmation, and
/// nothing has asked for it.</para>
///
/// <para>⚠ <b>The plain-text body is what the preview shows.</b> The message also carries an HTML
/// alternative, and both are sent; rendering HTML would mean a browser control this application does not
/// have and does not need. ⛔ The preview does not therefore claim to be a rendering — it says so.</para>
///
/// <para>⚠ No Avalonia types (Architecture rule 1): the confirmation and the Save-As arrive as delegates,
/// exactly as <c>ShellViewModel.SaveFilePicker</c> and <c>SettingsViewModel.Confirm</c> do.</para>
/// </summary>
public sealed partial class SendLicenceViewModel : MessageHostViewModel
{
    private readonly LicenceDelivery _delivery;
    private readonly SmtpSettings _settings;
    private readonly Func<SmtpSettings, ILicenseEmailSender> _smtpSenderFactory;
    private readonly Func<string, ILicenseEmailSender> _fileSenderFactory;

    /// <summary>Creates the view model over a composed message.</summary>
    /// <param name="message">⭐ Already composed, from the register's CURRENT artifact. ⛔ Never re-composed here.</param>
    /// <param name="settings">The configuration that will carry it.</param>
    /// <param name="delivery">Sends and records.</param>
    /// <param name="smtpSenderFactory">
    /// ⭐ A seam, and the only one that matters for testing: a test substitutes a sender that reports a
    /// refusal without a network, so <c>licence.send-failed</c> is provable. ⚠ Defaults to the real one,
    /// so production wiring is not a thing a caller can forget.
    /// </param>
    /// <param name="fileSenderFactory">The same, for the <c>.eml</c> route.</param>
    public SendLicenceViewModel(
        LicenseMessage message,
        SmtpSettings settings,
        LicenceDelivery delivery,
        Func<SmtpSettings, ILicenseEmailSender>? smtpSenderFactory = null,
        Func<string, ILicenseEmailSender>? fileSenderFactory = null)
    {
        Composed = message ?? throw new ArgumentNullException(nameof(message));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _smtpSenderFactory = smtpSenderFactory ?? (s => new SmtpLicenseEmailSender(s));
        _fileSenderFactory = fileSenderFactory ?? (path => new EmlFileEmailSender(path));
    }

    /// <summary>
    /// The composed message — ⭐ the exact value that will be sent.
    /// </summary>
    /// <remarks>
    /// ⚠ Not called "Message": <see cref="MessageHostViewModel.Message"/> already means "the status strip"
    /// throughout this application, and one word meaning two things in one view model is how a binding
    /// ends up showing the wrong one.
    /// </remarks>
    public LicenseMessage Composed { get; }

    // ── What the operator is looking at ─────────────────────────────────────────────────────────────

    /// <summary>Who receives it, name and address.</summary>
    public string Recipient => string.IsNullOrWhiteSpace(Composed.ToName)
        ? Composed.ToAddress
        : $"{Composed.ToName} <{Composed.ToAddress}>";

    /// <summary>Who it comes from.</summary>
    public string Sender => string.IsNullOrWhiteSpace(Composed.FromName)
        ? Composed.FromAddress
        : $"{Composed.FromName} <{Composed.FromAddress}>";

    /// <summary>The subject line, exactly as it will be sent.</summary>
    public string Subject => Composed.Subject;

    /// <summary>The plain-text body, exactly as it will be sent.</summary>
    public string Body => Composed.TextBody;

    /// <summary>
    /// The attachment, described the way the customer will see it — name, type and size.
    ///
    /// <para>⭐ The size is worth showing: it is the cheapest signal that the file is a licence and not an
    /// empty artifact, and an operator who sees "0 bytes" stops before sending.</para>
    /// </summary>
    public string Attachment => string.Create(
        CultureInfo.InvariantCulture,
        $"{Composed.AttachmentFileName} · {Composed.AttachmentBytes.Length} bytes · " +
        $"{Composed.AttachmentMediaType}");

    /// <summary>Which language the message is written in, and where that is decided.</summary>
    public string LanguageNote => string.Create(
        CultureInfo.InvariantCulture,
        $"Written in '{Composed.Language}'. The language applies to every customer and is changed under " +
        $"Settings ▸ E-mail.");

    /// <summary>How it will travel, or why it cannot travel directly.</summary>
    public string DeliveryNote => _settings.CanSendDirectly
        ? $"Sending through {_settings.Host}:{_settings.Port.ToString(CultureInfo.InvariantCulture)}."
        : "No SMTP server is configured, so this message can only be saved as an .eml file and sent " +
          "from your own mail client. Add a server under Settings ▸ E-mail to send directly.";

    /// <summary>⚠ Said in the window: the preview is the text body, and an HTML version travels with it.</summary>
    public string PreviewNote =>
        "This is exactly what will be sent. An HTML version of the same message is included for mail " +
        "clients that show it; clients that strip HTML show the text above.";

    // ── State ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>True while the server is being talked to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanSaveFile))]
    private bool _isSending;

    /// <summary>
    /// True once it has been sent.
    ///
    /// <para>⭐⭐ It DISABLES Send. A second click a moment after the first would deliver the licence twice
    /// and write two <c>licence.sent</c> lines — and the operator's own reason for clicking again is
    /// usually that the first click gave no visible answer yet.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool _isSent;

    /// <summary>Whether Send is available.</summary>
    public bool CanSend => _settings.CanSendDirectly && !IsSending && !IsSent;

    /// <summary>Whether the file route is available. ⭐ Always, unless a send is in flight.</summary>
    public bool CanSaveFile => !IsSending;

    // ── The platform seams ──────────────────────────────────────────────────────────────────────────

    /// <summary>Asks the operator to confirm. Assigned by the view.</summary>
    public Func<ConfirmRequest, Task<bool>>? Confirm { get; set; }

    /// <summary>Asks where to save the <c>.eml</c>. Assigned by the view.</summary>
    public Func<string, Task<string?>>? SaveFilePicker { get; set; }

    /// <summary>Raised when the window should close.</summary>
    public event Action? RequestClose;

    // ── Actions ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the message — ⭐ <b>after an explicit confirmation, always</b> (§14.1).
    ///
    /// <para>⛔ <b>With no confirmer wired it REFUSES rather than proceeding</b>, the rule L6.1a's
    /// <c>Forget settings</c> established: an outward-facing act must not lose its guard because a view
    /// forgot to attach one, with every test still green.</para>
    /// </summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        if (!CanSend)
        {
            return;
        }

        if (Confirm is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.ConfirmationUnavailableNothingSent);
            return;
        }

        var confirmed = await Confirm(new ConfirmRequest(
            ConfirmCatalog.SendLicenceTitle,
            ConfirmCatalog.SendLicenceMessage,
            ConfirmCatalog.SendLicenceAction,
            Composed.AttachmentFileName,
            Composed.ToAddress,
            _settings.Host)).ConfigureAwait(true);

        if (!confirmed)
        {
            // ⭐ Cancel changes nothing and says nothing — a "cancelled" notice reports the absence of an event.
            return;
        }

        IsSending = true;
        Message = StatusMessage.Info(StatusCatalog.SendingTo, Composed.ToAddress);

        SendOutcome outcome;
        try
        {
            outcome = await _delivery
                .SendAsync(_smtpSenderFactory(_settings), Composed)
                .ConfigureAwait(true);
        }
        catch (ArgumentException e)
        {
            // The sender refused to be built at all — settings with no host. ⚠ Not a delivery failure, so
            // it is not recorded as one.
            IsSending = false;
            Message = StatusMessage.FromError(e, MessageSeverity.Warning);
            return;
        }
        finally
        {
            IsSending = false;
        }

        if (outcome.Sent)
        {
            IsSent = true;
            Message = StatusMessage.Success(StatusCatalog.SentThrough, Composed.ToAddress, outcome.Delivered);
            return;
        }

        // ⚠ The server's own words, and the way out beside them — §14.1's rule for a failed send.
        Message = StatusMessage.Error(StatusCatalog.MessageNotSent, outcome.Error);
    }

    /// <summary>
    /// Writes the same message as an <c>.eml</c> the operator sends themselves.
    ///
    /// <para>⭐ Always available, never a consolation prize: some mailboxes refuse basic auth and some
    /// operators simply want the message in their own Sent items (§14.3). ⛔ Recorded as an EXPORT, not as
    /// a send — nothing has reached the customer yet.</para>
    /// </summary>
    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (!CanSaveFile)
        {
            return;
        }

        if (SaveFilePicker is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.NoSaveLocationOffered);
            return;
        }

        var suggested = "EmberTern licence" + EmlFileEmailSender.FileExtension;
        var path = await SaveFilePicker(suggested).ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        var outcome = await _delivery
            .ExportAsync(_fileSenderFactory(path), Composed)
            .ConfigureAwait(true);

        Message = outcome.Sent
            ? StatusMessage.Success(StatusCatalog.MessageSavedToFile, outcome.Delivered)
            : StatusMessage.Error(StatusCatalog.FileNotWritten, outcome.Error);
    }

    /// <summary>Closes the window.</summary>
    [RelayCommand]
    private void Close() => RequestClose?.Invoke();
}
