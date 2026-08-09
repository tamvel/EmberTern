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

    // ── Etap 6 — the approved §7 settings (ratified Q9) ──────────────────────────────────────────────────
    //
    // ⚠ These are the first NON-STRING preferences, and the reason that is safe is worth stating once here so
    // nobody "fixes" them into strings for consistency with the four above. §5.2.3's strings-not-enums rule is
    // about ENUMS specifically: JsonStringEnumConverter throws on a name it has never seen, so a newer build
    // writing a new member makes an older build refuse the WHOLE settings file. A bool and an int have no such
    // hazard — the set of legal JSON booleans never grows, and WorkspaceState has persisted dozens of both
    // since it shipped. What a number DOES need is bounds, which is PreferenceRange's job.

    /// <summary>
    /// Whether the previous session's open tabs are restored (§7.5).
    /// <para>
    /// ⚠ It gates <b>restore</b>, never <b>capture</b>: the workspace keeps being written at app close either
    /// way, so turning this back on restores the LAST session rather than whichever session last had it on.
    /// </para>
    /// <para>
    /// ⚠ And it gates the <i>tabs</i> only. A connection's <b>saved queries</b> live in the same stored
    /// workspace and are the user's own content, not clutter to be started clean from — dropping them would be
    /// rule-#11 data loss dressed up as a preference.
    /// </para>
    /// </summary>
    public bool RestoreWorkspaceOnStartup { get; set; } = true;

    /// <summary>
    /// Whether a newly opened <b>procedure</b> starts in Easy mode rather than Source (§7.6).
    ///
    /// <para>⭐ <b>These four replace four <c>WorkspaceState</c> flags that no user ever knowingly set.</b> They
    /// were written by whatever editor mode the user last toggled, so opening a procedure in Easy mode because
    /// of something done to a <i>different</i> procedure yesterday looked like a bug. The default now has one
    /// home and one way to change it; toggling Source/Easy in an editor affects that tab only.</para>
    ///
    /// <para>⚠ A workspace-restored tab still carries its own per-tab mode, which continues to win over this
    /// default — that half of the hybrid model was never the problem.</para>
    /// </summary>
    public bool ProcedureEasyModeDefault { get; set; }

    /// <inheritdoc cref="ProcedureEasyModeDefault"/>
    public bool ViewEasyModeDefault { get; set; }

    /// <inheritdoc cref="ProcedureEasyModeDefault"/>
    public bool TriggerEasyModeDefault { get; set; }

    /// <inheritdoc cref="ProcedureEasyModeDefault"/>
    public bool FunctionEasyModeDefault { get; set; }

    /// <summary>Rows a Preview (F5) execution stops at (§7.2). Travels as a value on
    /// <c>ExecutionRequest</c> — nothing reads a global.</summary>
    public int PreviewRowLimit { get; set; } = PreferenceOptions.PreviewRowLimit.Default;

    /// <summary>Rows at which a Full load pauses to ask whether to keep loading (§7.2). Bounded below
    /// <c>ExecutionDefaults.FullSafetyCeiling</c>, which stays a non-configurable memory backstop.</summary>
    public int FullLoadPromptThreshold { get; set; } = PreferenceOptions.FullLoadPromptThreshold.Default;

    /// <summary>Page size the Table Data and View Data grids open with (§7.7). Each grid's own page-size box
    /// still overrides it for that grid.</summary>
    public int DataPageSize { get; set; } = PreferenceOptions.DataPageSize.Default;

    /// <summary>Whether a grid whose column layout has never been saved auto-fits its columns (§7.4). A grid
    /// the user has adjusted keeps its own stored <c>GridProfile</c>.</summary>
    public bool GridAutoFitColumns { get; set; } = true;

    /// <summary>The transaction isolation the debugger's launch panel opens with (§7.3). The per-launch
    /// selector is unchanged; this only decides its initial value.</summary>
    public string DebuggerIsolation { get; set; } = PreferenceOptions.DebuggerIsolation.Default;

    /// <summary>
    /// How the workspace tab strip lays its tabs out — <c>MultiRow</c> (the default) or <c>SingleRow</c>
    /// (product-polish §8.2, ratified D5/D7).
    /// </summary>
    /// <remarks>
    /// ⚠ The two modes differ in PHILOSOPHY, not only in geometry, which is why this is a mode and not a
    /// flag on one layout: in <c>MultiRow</c> <b>no tab is ever hidden behind a menu</b> — the strip grows
    /// to <see cref="TabStripMaxRows"/> rows and then scrolls vertically, and that promise is the ratified
    /// difference from Visual Studio. <c>SingleRow</c> deliberately accepts hiding tabs, and pays for it
    /// with an overflow button carrying a COUNT plus a name-filtered list of everything off screen.
    /// </remarks>
    public string TabStripMode { get; set; } = PreferenceOptions.TabStripMode.Default;

    /// <summary>Maximum rows the multi-row tab strip may grow to before it starts scrolling (§8.2).</summary>
    /// <remarks>
    /// ⚠ Read only in <c>MultiRow</c> mode; the value is kept across a switch to <c>SingleRow</c> and back,
    /// because a mode is a view of the same workspace and losing the row limit on a round trip would be a
    /// silent settings loss.
    /// <para>⭐ This is also what makes the Settings Center safe to HIDE the row in single-row layout: the
    /// row disappears, the number does not. Hiding a setting and discarding it are different things, and
    /// only the first one happens.</para>
    /// </remarks>
    public int TabStripMaxRows { get; set; } = PreferenceOptions.TabStripMaxRows.Default;
}
