namespace EmberTern.LicenseManager.ViewModels;

/// <summary>How loudly a message speaks. Mirrors EmberTern's <c>MessageBanner</c> severities.</summary>
public enum MessageSeverity
{
    /// <summary>Neutral information.</summary>
    Info,

    /// <summary>Something worked.</summary>
    Success,

    /// <summary>Something needs attention but nothing failed.</summary>
    Warning,

    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>
/// The one message surface of the License Manager.
///
/// <para>⛔ A message is never a loose coloured <c>TextBlock</c> in a view — same rule EmberTern applies
/// to <c>MessageBanner</c>, and for the same reason: a per-host message is a per-host colour decision,
/// and those diverge.</para>
/// </summary>
/// <param name="Text">What to say. ⭐ What happened · why · what to do now.</param>
/// <param name="Severity">How loudly.</param>
public sealed record StatusMessage(string Text, MessageSeverity Severity)
{
    /// <summary>Nothing to say.</summary>
    public static StatusMessage? None => null;

    /// <summary>Neutral information.</summary>
    public static StatusMessage Info(string text) => new(text, MessageSeverity.Info);

    /// <summary>Something worked.</summary>
    public static StatusMessage Success(string text) => new(text, MessageSeverity.Success);

    /// <summary>Something needs attention.</summary>
    public static StatusMessage Warning(string text) => new(text, MessageSeverity.Warning);

    /// <summary>Something failed.</summary>
    public static StatusMessage Error(string text) => new(text, MessageSeverity.Error);

    // ⛔ There is deliberately no "which style class" member here. The views bind Classes.info /
    //    Classes.success / Classes.warning / Classes.error to the IsX properties on
    //    MessageHostViewModel, so a severity→class member would be a second answer to a question that
    //    already has one — and the one nothing reads is the one that goes wrong unnoticed.
}
