namespace EmberTern.Core.Settings;

/// <summary>
/// ⭐ The scalar user preferences — the section <c>settings.dat</c> did not have until now.
/// <para>
/// Before this existed, <c>UserSettings</c> held four <i>lists</i> and not one scalar, which is why every
/// scalar preference the app already had ended up in <c>WorkspaceState</c> beside window bounds and tab
/// lists: it was the only class that ever accepted one. This is that home.
/// </para>
/// <para>
/// <b>The dividing line that keeps it from becoming the next dumping ground:</b> if the user would go
/// looking for it in a settings dialog, it is a preference and belongs here; if they set it by dragging or
/// clicking the thing itself (a panel height, a sidebar width, a maximized results pane), it is layout and
/// stays in <c>WorkspaceState</c>.
/// </para>
///
/// <para><b>⭐ This type is a SELF-SUFFICIENT CONTRACT (ratified — design §5.2.1).</b> Every property
/// carries a valid value from its own initializer, so <c>new Preferences()</c> is <b>always</b> usable and
/// needs no initialization step. Every consumer, every test and every "restore defaults" path may rely on
/// that without a null check or a bootstrap call — and "restore defaults" <i>is</i> <c>new Preferences()</c>,
/// which is why it cannot drift from a separately maintained table of defaults.</para>
///
/// <para>Three rules follow, and each is easy to violate while technically honouring the first:</para>
/// <list type="number">
///   <item><description>
///     ⚠ <b>No property may be "nullable meaning unset".</b> A <c>string?</c> whose null means <i>not chosen
///     yet</i> hands the default decision to whoever reads it, and there will eventually be more than one
///     reader — which is how one default becomes three. A property has a real value or it does not belong
///     here.
///   </description></item>
///   <item><description>
///     <b>Defaults live in <see cref="PreferenceOptions"/>, not as literals here.</b> Each initializer reads
///     its option set's <c>Default</c>, so the legal values and the default are declared together, once. A
///     literal here would be a second copy of a fact the catalog already states — and the catalog is what
///     the validator and the UI read.
///   </description></item>
///   <item><description>
///     <b>It is a <c>record</c> so equality is real.</b> The contract's pinning test compares
///     <c>Validate(new Preferences())</c> with <c>new Preferences()</c>; on a plain class that comparison is
///     reference equality and passes vacuously — pinning nothing while looking authoritative, which is worse
///     than having no test. Settable properties are fine and it serializes exactly as a class does.
///   </description></item>
/// </list>
///
/// <para>
/// <b>Additive by design: the settings schema version is deliberately NOT bumped for this.</b> An older
/// <c>settings.dat</c> simply has no <c>Preferences</c> key and deserializes to a default instance. A bump
/// would trip the store's downgrade protection and make an <i>older</i> build refuse the whole file —
/// the lesson <c>ParameterValue.TypeText</c> and <c>ImportProfiles</c> already recorded.
/// </para>
/// <para>
/// ⚠ That licence covers adding a <b>property</b>. It does not cover adding a value to a persisted
/// <b>enum</b> — see the rule in <see cref="PreferenceOptions"/>, which is also why every property here is a
/// string.
/// </para>
/// </summary>
public sealed record Preferences
{
    /// <summary>Application theme. Applied through the existing
    /// <c>Application.RequestedThemeVariant</c>; the titlebar toggle and the settings radio write this same
    /// value, so they cannot disagree.</summary>
    public string Theme { get; set; } = PreferenceOptions.Theme.Default;

    /// <summary>
    /// UI language as an ISO code.
    /// <para>
    /// ⚠ <b>Stored and validated from day one, consumed by nothing yet</b> — and that is the ratified design,
    /// not an unfinished wire. It is precisely because it has no reader that it is the property most likely
    /// to be left unvalidated "until it matters", and the localization milestone is far enough away that a
    /// bad value would be thoroughly entrenched by then.
    /// </para>
    /// </summary>
    public string Language { get; set; } = PreferenceOptions.Language.Default;

    /// <summary>How <c>SqlFormatter</c> cases SQL/PSQL keywords. Defaults to <c>Lower</c>, so shipped output
    /// is byte-identical to today's.</summary>
    public string FormatterKeywordCase { get; set; } = PreferenceOptions.Casing.Default;

    /// <summary>
    /// How <c>SqlFormatter</c> cases identifiers. Defaults to <c>Lower</c>, so shipped output is
    /// byte-identical to today's.
    /// <para>
    /// ⚠ This governs the formatter only. Generated DDL keeps uppercasing identifiers
    /// (<c>DdlGenerator.PresentIdentifier</c>) and that separation is ratified: the formatter reformats the
    /// <i>user's</i> text, whereas <c>DdlGenerator</c> composes <i>new</i> DDL for the catalog — folding them
    /// would let a Lower setting emit <c>create procedure foo</c> into the database.
    /// </para>
    /// <para>
    /// ⚠ A <b>quoted</b> identifier is case-sensitive in Firebird, so re-casing one changes which object is
    /// named. The formatter already passes quoted identifiers through verbatim; this setting must be applied
    /// <i>inside</i> that existing guard, never around it (§0 / rule #11).
    /// </para>
    /// </summary>
    public string FormatterIdentifierCase { get; set; } = PreferenceOptions.Casing.Default;
}
