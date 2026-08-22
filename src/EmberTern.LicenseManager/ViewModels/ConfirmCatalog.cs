using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// Every word a confirmation dialog says, as keys.
///
/// <para>⭐ Its own catalog rather than a region of <see cref="StatusCatalog"/>: a confirmation is a
/// QUESTION with an action name, not a report of something that happened, and the two vocabularies are
/// translated differently — decision D‑5's thematic split, which the key prefix is what makes safe.</para>
///
/// <para>⚠⚠ <b><see cref="Cancel"/> is the reason this file matters most.</b> It used to be a defaulted
/// parameter — <c>string CancelLabel = "Cancel"</c> — and a default parameter value is copied into every
/// CALLER at compile time, exactly like a <c>const</c>. All three call sites relied on the default, so the
/// word was baked into three assemblies' worth of call sites and no lookup could ever have reached it.
/// ⛔ Never give a method or record a defaulted parameter carrying words.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class ConfirmCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Confirm.";

    private static MessageKey Key(string member) => new(KeyPrefix + member);

    /// <summary>Send this licence?</summary>
    public static MessageKey SendLicenceTitle => Key(nameof(SendLicenceTitle));

    /// <summary>The message and {0} will be sent to {1} through {2}. This cannot be recalled.</summary>
    public static MessageKey SendLicenceMessage => Key(nameof(SendLicenceMessage));

    /// <summary>Send</summary>
    public static MessageKey SendLicenceAction => Key(nameof(SendLicenceAction));

    /// <summary>Forget SMTP settings?</summary>
    public static MessageKey ForgetSmtpTitle => Key(nameof(ForgetSmtpTitle));

    /// <summary>This will permanently remove the saved SMTP configuration, including the stored password…</summary>
    public static MessageKey ForgetSmtpMessage => Key(nameof(ForgetSmtpMessage));

    /// <summary>Forget settings</summary>
    public static MessageKey ForgetSmtpAction => Key(nameof(ForgetSmtpAction));

    /// <summary>Send a test message?</summary>
    public static MessageKey TestMessageTitle => Key(nameof(TestMessageTitle));

    /// <summary>A test message will be sent to {0} through {1}, using the settings on this page…</summary>
    public static MessageKey TestMessageMessage => Key(nameof(TestMessageMessage));

    /// <summary>Send test</summary>
    public static MessageKey TestMessageAction => Key(nameof(TestMessageAction));

    /// <summary>Send these licences?</summary>
    public static MessageKey BulkSendTitle => Key(nameof(BulkSendTitle));

    /// <summary>{0} messages will be sent through {1}. It will take at least {2}. This cannot be recalled.</summary>
    /// <remarks>
    /// ⭐⭐ A plural FAMILY, resolved through <c>ConfirmRequest.Count</c>: the number of messages is the one
    /// fact the operator must read correctly before an act that cannot be recalled, and Polish inflects it
    /// three ways. ⚠ The count is <c>{0}</c>; the host and the duration follow.
    /// ⛔ The number of ADDRESSES is deliberately not in this sentence — a sentence has one plural pivot,
    /// and the address count has its own sentence on the preview the operator has just read.
    /// </remarks>
    public static MessageKey BulkSendMessage => Key(nameof(BulkSendMessage));

    /// <summary>Send messages</summary>
    public static MessageKey BulkSendAction => Key(nameof(BulkSendAction));

    /// <summary>Remove this licence?</summary>
    public static MessageKey RemoveLicenceTitle => Key(nameof(RemoveLicenceTitle));

    /// <summary>Licence {0} … was never issued … it will be removed from the register entirely.</summary>
    /// <remarks>
    /// ⭐ The two outcomes get two SENTENCES rather than one hedged one. The operator is confirming a
    /// different act in each case — one erases a row that never produced anything, the other retires a row
    /// whose artifacts are permanent — and a single sentence covering both would have to be vague about
    /// exactly the part that matters.
    /// </remarks>
    public static MessageKey RemoveLicenceNeverIssuedMessage => Key(nameof(RemoveLicenceNeverIssuedMessage));

    /// <summary>Licence {0} … has {1} issued artifact(s) … it will be retired; the history is kept.</summary>
    public static MessageKey RemoveLicenceIssuedMessage => Key(nameof(RemoveLicenceIssuedMessage));

    /// <summary>Remove licence</summary>
    public static MessageKey RemoveLicenceAction => Key(nameof(RemoveLicenceAction));

    /// <summary>{0} ({1}) has {2} retired licence(s) … the customer will be retired.</summary>
    /// <remarks>
    /// ⭐ The second of two messages, exactly as the licence side has: the operator is confirming a
    /// different act — retiring a row whose licence history is permanent, rather than erasing one that
    /// never had any — and one hedged sentence would be vague about the part that matters.
    /// </remarks>
    public static MessageKey RetireCustomerMessage => Key(nameof(RetireCustomerMessage));

    /// <summary>Remove this customer?</summary>
    public static MessageKey RemoveCustomerTitle => Key(nameof(RemoveCustomerTitle));

    /// <summary>{0} ({1}) will be removed from the register…</summary>
    /// <remarks>
    /// ⭐ It says what SURVIVES as well as what goes: the audit line keeps the whole record and cannot be
    /// deleted, so this is reversible in the sense that matters — the operator can find out who they were.
    /// ⛔ It is not offered at all when the customer has licences; that refusal happens before this.
    /// </remarks>
    public static MessageKey RemoveCustomerMessage => Key(nameof(RemoveCustomerMessage));

    /// <summary>Remove customer</summary>
    public static MessageKey RemoveCustomerAction => Key(nameof(RemoveCustomerAction));

    /// <summary>The way out. ⚠ Shared by every confirmation — see the type's remarks.</summary>
    public static MessageKey Cancel => Key(nameof(Cancel));
}
