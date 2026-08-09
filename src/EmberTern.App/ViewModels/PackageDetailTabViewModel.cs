using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird PACKAGE. Six tabs — Package (editable header
/// source + Compile), Body (editable body source), Members (read-only grouped
/// tree, double-click navigates into the header/body editor), Description
/// (editable COMMENT ON PACKAGE), Dependencies (dual tree), DDL (read-only).
///
/// Deliberately NOT a subclass of <see cref="SourceObjectDetailTabViewModel"/>:
/// that base is built around ONE source artifact + a Source⇄Easy structured-model
/// toggle, but a package has TWO editable source artifacts (header + body) and no
/// meaningful Easy/structured mode. The right sibling is
/// <see cref="ViewDetailTabViewModel"/> — editable source + Compile + dirty/Revert
/// + Dependencies + DDL + editable Description, all without the base. Reuse happens
/// at the reader / static-helper level (same <see cref="FirebirdTableDetailReader"/>
/// /<see cref="FirebirdDdlReader"/> patterns, <see cref="TableDetailTabViewModel.BuildDependencyTree"/>,
/// the shared <see cref="SqlFormatter"/>), not via inheritance. A package is treated
/// as a single logical object: ONE Compile that runs the header first, then the body.
/// </summary>
public partial class PackageDetailTabViewModel : ViewModelBase, IUnsavedWorkSource, ISavableObjectEditor, IDependencyNavigator
{
    // Sub-tab indices — must match the TabItem order in PackageDetailTabView.axaml.
    // (Diagnostics is appended LAST so these indices, which are also persisted per tab, never shift.)
    public const int PackageSubTabIndex = 0;
    public const int BodySubTabIndex = 1;

    /// <summary>Diagnostics panel (Stage 7 / S4) for this editor's own Diagnostics sub-tab — the same view
    /// and VM the SQL Editor uses, hosted per object exactly as the routine editors do. Fed by the View's
    /// <c>DiagnosticsPanelHost</c> from the active SQL document's cached diagnostics; this VM computes
    /// nothing.</summary>
    public DiagnosticsPanelViewModel DiagnosticsPanel { get; } = new();

    // Templates for the New Package flow (the user edits the SQL directly).
    public const string NewPackageHeaderTemplate =
        "CREATE OR ALTER PACKAGE NEW_PACKAGE\nAS\nBEGIN\n  /* PROCEDURE / FUNCTION declarations */\nEND";
    public const string NewPackageBodyTemplate =
        "RECREATE PACKAGE BODY NEW_PACKAGE\nAS\nBEGIN\n  /* implementations */\nEND";

    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlReader? _ddlReader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    // ─── Change safety ────────────────────────────────────────────────────
    //
    // Compile writes CREATE OR ALTER PACKAGE (header) and then the body — a whole-object replacement of both
    // halves, so the buffer may descend from a definition another session has since replaced.
    private readonly ObjectChangeGate _changeGate = new();

    /// <summary>The change-safety gate guarding this tab's compile. Exposed for tests.</summary>
    internal ObjectChangeGate ChangeGate => _changeGate;

    /// <summary>Owner-supplied "is this name already taken?" probe for the New-package flow. The New body
    /// template is a <c>CREATE OR ALTER PACKAGE</c>, so a name collision would overwrite rather than fail.</summary>
    internal Func<string, CancellationToken, Task<bool>>? ObjectExistsProbe { get; set; }

    /// <summary>
    /// The package's definition as ONE comparable artifact. A package is two stored sources that are compiled
    /// together, so change safety has to be judged over both: a colleague who altered only the body must be
    /// detected just as surely as one who altered the header.
    /// <para>The separator is not cosmetic — concatenating the halves directly would let a change that moves
    /// text across the header/body boundary produce an unchanged combined string.</para>
    /// </summary>
    private static string ComposeDefinition(string? header, string? body)
        => (header ?? string.Empty) + "\n/*-- package body --*/\n" + (body ?? string.Empty);

    /// <summary>Reads both halves as the database holds them now — the ONE read that answers "has it changed
    /// since?", composed exactly as the baseline was.</summary>
    private async Task<string> ReadDefinitionAsync(CancellationToken cancellationToken)
    {
        var obj = new MetadataObject(PackageName, MetadataObjectKind.Package);
        var header = await _ddlReader!.FetchPackageHeaderSourceAsync(obj, cancellationToken).ConfigureAwait(true);
        var body = await _ddlReader!.FetchPackageBodySourceAsync(obj, cancellationToken).ConfigureAwait(true);
        return ComposeDefinition(header, body);
    }

    /// <summary>Mirrors <c>SourceObjectDetailTabViewModel.CheckChangeSafetyAsync</c>: an existing package is
    /// checked by re-reading it, a new one by looking its name up.</summary>
    private Task<ObjectChangeCheck> CheckChangeSafetyAsync(string headerSql, CancellationToken cancellationToken)
    {
        if (!IsNew)
        {
            return _changeGate.CheckOverwriteAsync(
                PackageName, async ct => await ReadDefinitionAsync(ct).ConfigureAwait(true), cancellationToken);
        }

        // The name that will ACTUALLY be created, read off the header statement about to run.
        var targetName = TryParsePackageName(headerSql);
        if (string.IsNullOrWhiteSpace(targetName)) return Task.FromResult(ObjectChangeCheck.Allowed);

        var probe = ObjectExistsProbe;
        return _changeGate.CheckCreateAsync(
            targetName,
            probe is null ? null : ct => probe(targetName, ct),
            cancellationToken);
    }

    public PackageDetailTabViewModel(string packageName)
        : this(packageName, null, null, null)
    {
    }

    public PackageDetailTabViewModel(
        string packageName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        PackageName = packageName;
        _reader = reader;
        _ddlReader = ddlReader;
        _ddlExecutor = ddlExecutor;
        MemberGroups = new ObservableCollection<PackageMemberGroupNode>();
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();
        // Ctor field assignments must not flip dirty — release the suppression at ctor end.
        _suppressDirty = false;
    }

    public string PackageName { get; }

    // ─── Dirty tracking (drives IUnsavedWorkSource + Revert) ──────────────
    //
    // Same explicit-flag approach as ViewDetailTabViewModel: programmatic writes
    // (load, refresh) are wrapped in _suppressDirty; genuine user edits flip it via
    // MarkDirty. Both source editors mark dirty.
    private bool _isDirty;
    private bool _suppressDirty = true;

    public bool IsDirty => _isDirty;
    internal void ClearDirty() => SetDirty(false);
    private void MarkDirty() { if (!_suppressDirty) SetDirty(true); }

    private void SetDirty(bool value)
    {
        if (_isDirty == value) return;
        _isDirty = value;
        OnPropertyChanged(nameof(IsDirty));
        RevertChangesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>True for a not-yet-created package (the New Package flow). The
    /// non-source tabs stay empty until the first successful Compile, after which
    /// the owner reopens the real package. Compile in this mode raises
    /// <see cref="PackageCreated"/>.</summary>
    public bool IsNew { get; init; }

    // ─── Editable source (two artifacts) ──────────────────────────────────

    /// <summary>Editable CREATE OR ALTER PACKAGE header source — the Package tab.</summary>
    [ObservableProperty]
    private string _headerSource = string.Empty;

    /// <summary>Editable RECREATE PACKAGE BODY source — the Body tab. May be empty
    /// when the package has no body (Compile then skips the body step).</summary>
    [ObservableProperty]
    private string _bodySource = string.Empty;

    /// <summary>Read-only reconstructed DDL (header + body) — the DDL tab.</summary>
    [ObservableProperty]
    private string _ddlText = string.Empty;

    partial void OnHeaderSourceChanged(string value) => MarkDirty();
    partial void OnBodySourceChanged(string value) => MarkDirty();

    public UnsavedWorkItem? GetUnsavedWork()
    {
        if (!IsDirty) return null;
        return IsNew
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedNewPackageFormat, PackageName))
            : new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedModifiedPackageFormat, PackageName));
    }

    // ─── Members (read-only, from the catalog) ─────────────────────────────

    public ObservableCollection<PackageMemberGroupNode> MemberGroups { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMembers))]
    [NotifyPropertyChangedFor(nameof(ShowMembersEmpty))]
    private bool _membersLoaded;

    public bool HasMembers => MembersLoaded && MemberGroups.Count > 0;
    public bool ShowMembersEmpty => MembersLoaded && MemberGroups.Count == 0;

    /// <summary>Raised when the user double-clicks a member — the view moves the
    /// caret + selection in the matching editor and brings it into view.</summary>
    public event Action<PackageMemberLocation>? NavigateToMemberRequested;

    /// <summary>Raised when the user debugs a package member (Stage X / D11 seam C — procedures; Seam D —
    /// functions) — carries the whole <see cref="PackageMember"/> (name + kind) so the owner reconstructs it as
    /// the right CREATE PROCEDURE/FUNCTION and launches it as a debug ROOT via the ONE launch path
    /// (<c>MainWindowViewModel.OpenDebuggerForPackageMember</c>). This VM only signals intent — the debugger
    /// architecture is untouched.</summary>
    public event Action<PackageMember>? DebugMemberRequested;

    /// <summary>Signals a debug request for a package member. PROCEDURE and FUNCTION members are debuggable
    /// (both launch as a ROOT — the packaged-function root, Seam D); a null member is a no-op.</summary>
    public void RequestDebugMember(PackageMember? member)
    {
        if (member is null || member.Kind is not (PackageMemberKind.Procedure or PackageMemberKind.Function)) return;
        DebugMemberRequested?.Invoke(member);
    }

    // ── Members-tab Debug button (D15.3 Seam E) ─────────────────────────────────────────────────────
    // A persistent, discoverable Debug entry point on the Members tab, driving the SAME DebugMemberRequested
    // path as the context menu (no parallel launch logic). It is always visible; enabled only for a currently
    // debuggable member kind. Extending support to package FUNCTIONS (the Function-as-Root milestone) is the
    // ONLY change needed here — widen CanDebugSelectedMember + its tooltip case; the button/XAML are already
    // data-driven, so the UI does not change.

    /// <summary>The Members tree's current selection (a group or member node, or null) — bound to the
    /// TreeView's SelectedItem. The single source of truth for the Debug button's enabled state + tooltip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDebugSelectedMember))]
    [NotifyPropertyChangedFor(nameof(DebugMemberTooltip))]
    [NotifyCanExecuteChangedFor(nameof(DebugSelectedMemberCommand))]
    private object? _selectedMemberNode;

    /// <summary>True when the selected member is a debuggable kind — a PROCEDURE (D11) or a FUNCTION (Seam D),
    /// both launched as a ROOT.</summary>
    public bool CanDebugSelectedMember
        => SelectedMemberNode is PackageMemberItemNode { Member.Kind: PackageMemberKind.Procedure or PackageMemberKind.Function };

    /// <summary>The contextual reason shown on the (possibly disabled) Debug button, so it explains its own
    /// availability: ready (procedure or function) / not debuggable / nothing selected.</summary>
    public string DebugMemberTooltip => SelectedMemberNode switch
    {
        PackageMemberItemNode { Member.Kind: PackageMemberKind.Procedure or PackageMemberKind.Function }
            => UiStrings.PackageDebugMemberTooltipReady,
        PackageMemberItemNode => UiStrings.PackageDebugMemberTooltipNotDebuggable,
        _ => UiStrings.PackageDebugMemberTooltipNoSelection,
    };

    /// <summary>Debug the selected member — routes through the SAME <see cref="RequestDebugMember"/> path as the
    /// context menu (no second launch logic). Gated by <see cref="CanDebugSelectedMember"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanDebugSelectedMember))]
    private void DebugSelectedMember()
    {
        if (SelectedMemberNode is PackageMemberItemNode node) RequestDebugMember(node.Member);
    }

    /// <summary>Navigates to a member's declaration/implementation: prefers the
    /// body (where you'd edit it), falling back to the header. No-op when the token
    /// can't be located in either source.</summary>
    public void NavigateToMember(PackageMember? member)
    {
        if (member is null) return;
        var loc = ResolveMemberLocation(member);
        if (loc is null) return;
        ActiveSubTabIndex = loc.InBody ? BodySubTabIndex : PackageSubTabIndex;
        NavigateToMemberRequested?.Invoke(loc);
    }

    /// <summary>Pure resolution (testable): body first, then header. Returns null
    /// when the member's FUNCTION/PROCEDURE token isn't found in either source.</summary>
    internal PackageMemberLocation? ResolveMemberLocation(PackageMember member)
    {
        var bodyOffset = PackageSourceScanner.FindMemberOffset(BodySource, member.Kind, member.Name);
        if (bodyOffset >= 0) return new PackageMemberLocation(true, bodyOffset, member.Name.Length);
        var headerOffset = PackageSourceScanner.FindMemberOffset(HeaderSource, member.Kind, member.Name);
        if (headerOffset >= 0) return new PackageMemberLocation(false, headerOffset, member.Name.Length);
        return null;
    }

    internal void SetMembers(IReadOnlyList<PackageMember> members)
    {
        MemberGroups.Clear();
        AddMemberGroup(UiStrings.MetadataGroupFunctions, PackageMemberKind.Function, members);
        AddMemberGroup(UiStrings.MetadataGroupProcedures, PackageMemberKind.Procedure, members);
        MembersLoaded = true;
        OnPropertyChanged(nameof(HasMembers));
        OnPropertyChanged(nameof(ShowMembersEmpty));
    }

    private void AddMemberGroup(string label, PackageMemberKind kind, IReadOnlyList<PackageMember> members)
    {
        var metaKind = kind == PackageMemberKind.Function
            ? MetadataObjectKind.Function
            : MetadataObjectKind.Procedure;
        var iconGeometry = MetadataNodeViewModel.GeometryKeyFor(metaKind);
        var iconKey = MetadataNodeViewModel.ResourceKeyFor(metaKind);

        var children = new List<PackageMemberItemNode>();
        foreach (var m in members)
        {
            if (m.Kind != kind) continue;
            children.Add(new PackageMemberItemNode
            {
                Member = m,
                IconGeometryKey = iconGeometry,
                IconResourceKey = iconKey,
            });
        }
        if (children.Count == 0) return;

        MemberGroups.Add(new PackageMemberGroupNode
        {
            Header = string.Format(CultureInfo.CurrentCulture, "{0} ({1})", label, children.Count),
            IconGeometryKey = iconGeometry,
            IconResourceKey = iconKey,
            Children = children,
        });
    }

    // ─── Description (editable COMMENT ON PACKAGE) ────────────────────────

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(ShowDescriptionEmpty))]
    private bool _descriptionLoaded;

    public bool HasDescription => DescriptionLoaded && !string.IsNullOrEmpty(Description);
    public bool ShowDescriptionEmpty => DescriptionLoaded && string.IsNullOrEmpty(Description);

    [ObservableProperty]
    private string _editableDescription = string.Empty;

    partial void OnDescriptionChanged(string value) => EditableDescription = value ?? string.Empty;

    public bool CanEditDescription => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanEditDescription))]
    private Task SaveDescription() => SaveDescriptionCoreAsync();

    [RelayCommand(CanExecute = nameof(CanEditDescription))]
    private Task ClearDescription()
    {
        EditableDescription = string.Empty;
        return SaveDescriptionCoreAsync();
    }

    private async Task SaveDescriptionCoreAsync()
    {
        if (_ddlExecutor is null) return;
        ErrorMessage = null;
        var comment = string.IsNullOrWhiteSpace(EditableDescription) ? null : EditableDescription;
        var sql = DdlGenerator.BuildCommentPackage(PackageName, comment);
        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.TableDescriptionSaveFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.TableDescriptionSaveFailedFormat, ex.Message);
            return;
        }
        await RefreshAsync().ConfigureAwait(true);
    }

    // ─── SQL formatting / comment (acts on the active editor) ─────────────
    //
    // Source / Body are two editors; the active one is chosen by ActiveSubTabIndex.
    // The view installs the selection/replace callbacks against whichever editor is
    // active, so Format / Comment act on the editor the user is looking at.

    public Func<string?>? SelectedTextProvider { get; set; }
    public Action<string>? ReplaceSelectedOrAllText { get; set; }
    public event Action? CommentRequested;
    public event Action? UncommentRequested;

    private string ActiveEditorText
    {
        get => ActiveSubTabIndex == BodySubTabIndex ? BodySource : HeaderSource;
        set { if (ActiveSubTabIndex == BodySubTabIndex) BodySource = value; else HeaderSource = value; }
    }

    /// <summary>
    /// The casing style the Format SQL action uses — supplied by the tab factory, which has the app's one
    /// <c>PreferencesService</c> in hand (<c>WorkspaceTabViewModel.Create*Detail</c>).
    /// <para>⚠ A <b>provider</b>, not a captured value: apply-on-change means the preference can change
    /// while this tab is open, and a captured style would silently format with the previous setting.</para>
    /// <para>⚠ Non-nullable with a real default, so a view model constructed without a factory (every unit
    /// test) formats deterministically in the shipped style — "nullable meaning unset" would hand the
    /// default decision to each reader, the shape <c>Preferences</c>' own contract forbids.</para>
    /// </summary>
    public Func<FormatterStyle> CurrentFormatterStyle { get; set; } = () => FormatterStyle.Default;

    [RelayCommand]
    private void FormatSql()
    {
        var selected = SelectedTextProvider?.Invoke();
        var hasSelection = !string.IsNullOrEmpty(selected);
        var source = hasSelection ? selected! : ActiveEditorText;
        if (string.IsNullOrEmpty(source)) return;

        var formatted = SqlFormatter.Format(source, CurrentFormatterStyle());
        if (string.Equals(formatted, source, StringComparison.Ordinal)) return;

        if (ReplaceSelectedOrAllText is { } replace) replace(formatted);
        else if (!hasSelection) ActiveEditorText = formatted;
    }

    [RelayCommand]
    private void CommentBody() => CommentRequested?.Invoke();

    [RelayCommand]
    private void UncommentBody() => UncommentRequested?.Invoke();

    // ─── Dependencies (reuses TableDetail's routing) ───────────────────────

    public ObservableCollection<DependencyGroupNode> DependsOnTree { get; }
    public ObservableCollection<DependencyGroupNode> DependedOnByTree { get; }

    public event Action<MetadataObject>? OpenObjectRequested;

    public void RequestOpen(DependencyLeafNode leaf)
    {
        if (leaf is null) return;
        var dep = leaf.Dependency;
        if (dep is null || string.IsNullOrEmpty(dep.ObjectName)) return;
        var kind = TableDetailTabViewModel.MapObjectTypeToKind(dep.ObjectType);
        if (kind is null) return;
        OpenObjectRequested?.Invoke(new MetadataObject(dep.ObjectName, kind.Value));
    }

    [ObservableProperty]
    private int _activeSubTabIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // ─── Compile (one logical object: header then body) ───────────────────

    /// <summary>Raised after a successful Compile in <see cref="IsNew"/> mode —
    /// carries the package name parsed from the header (or null). The owner refreshes
    /// the tree, closes the New tab and reopens the real package.</summary>
    public event Action<string?>? PackageCreated;

    /// <summary>Raised after a successful Compile of an existing package — the owner offers to
    /// recompile its dependents (Part 2).</summary>
    public event Action? CompiledExistingObject;

    public bool CanCompile => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

    // ─── ISavableObjectEditor (Save-and-close / Save-and-disconnect WorkGuard) ──
    // Thin adapter over ExecuteCompileAsync (header + body — the ONE save path); not a second mechanism.
    public async Task<EditorSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        await ExecuteCompileAsync(cancellationToken).ConfigureAwait(true);
        return ErrorMessage is null ? new EditorSaveResult(true, null) : new EditorSaveResult(false, ErrorMessage);
    }

    /// <summary>
    /// Compiles the package as one logical object: the header (CREATE OR ALTER
    /// PACKAGE) FIRST, then the body (RECREATE PACKAGE BODY). If the header fails the
    /// run stops immediately and the body is NOT attempted (the body can't compile
    /// against a header that didn't apply). Each step runs in its own autonomous DDL
    /// transaction (auto-committed) like every other Compile in the app.
    /// </summary>
    public async Task ExecuteCompileAsync(CancellationToken cancellationToken = default)
    {
        // Both pre-condition refusals REPORT (Seam 6b) — see the contract on ISavableObjectEditor:
        // a compile that never ran must not leave ErrorMessage null, or SaveAsync claims success
        // having written nothing. An empty header means there is no package to compile at all (the
        // body alone cannot be applied), so it is the same "nothing to compile" refusal.
        if (_ddlExecutor is null)
        {
            ErrorMessage = UiStrings.NoConnectionMessage;
            return;
        }

        var header = HeaderSource;
        if (string.IsNullOrWhiteSpace(header))
        {
            ErrorMessage = UiStrings.EditorNothingToCompile;
            return;
        }

        // ⭐ CHANGE SAFETY — see ObjectChangeGate. Checked once, before the header goes out, because the
        // header/body pair is one logical write: refusing between the two would leave the package half
        // replaced, which is worse than either outcome.
        var safety = await CheckChangeSafetyAsync(header, cancellationToken).ConfigureAwait(true);
        if (!safety.MayProceed)
        {
            ErrorMessage = safety.RefusalMessage;
            return;
        }

        ErrorMessage = null;
        try
        {
            await _ddlExecutor.ExecuteAsync(header, cancellationToken).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.PackageCompileHeaderFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.PackageCompileHeaderFailedFormat, ex.Message);
            return;
        }

        var body = BodySource;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                await _ddlExecutor.ExecuteAsync(body, cancellationToken).ConfigureAwait(true);
            }
            catch (DdlExecutionException ex)
            {
                ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.PackageCompileBodyFailedFormat, ex.Message);
                return;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.PackageCompileBodyFailedFormat, ex.Message);
                return;
            }
        }

        if (IsNew)
        {
            PackageCreated?.Invoke(TryParsePackageName(header));
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        CompiledExistingObject?.Invoke();
    }

    /// <summary>Best-effort extraction of the package name from a
    /// <c>CREATE [OR ALTER] PACKAGE name …</c> header so the New flow can reopen the
    /// created object. Pure + internal for unit tests.</summary>
    internal static string? TryParsePackageName(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;
        var tokens = sql!.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        if (i >= tokens.Length || !tokens[i].Equals("CREATE", StringComparison.OrdinalIgnoreCase)) return null;
        i++;
        if (i < tokens.Length && tokens[i].Equals("OR", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            if (i < tokens.Length && tokens[i].Equals("ALTER", StringComparison.OrdinalIgnoreCase)) i++;
            else return null;
        }
        if (i >= tokens.Length || !tokens[i].Equals("PACKAGE", StringComparison.OrdinalIgnoreCase)) return null;
        i++;
        if (i >= tokens.Length) return null;

        var name = tokens[i];
        if (name.StartsWith('"'))
        {
            var end = name.IndexOf('"', 1);
            return end > 0 ? name.Substring(1, end - 1) : null;
        }
        name = name.TrimEnd(',', ';');
        return name.Length == 0 ? null : name.ToUpperInvariant();
    }

    // ─── Revert (discard uncompiled edits, reload from DB) ────────────────

    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    private Task<bool> RequestConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

    public bool CanRevertChanges => IsDirty && !IsNew;

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private async Task RevertChanges()
    {
        if (!CanRevertChanges) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.RevertChangesConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.RevertChangesConfirmFormat, PackageName),
            ConfirmLabel = UiStrings.RevertChangesConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await RefreshAsync().ConfigureAwait(true);
    }

    // ─── Delete (DROP PACKAGE) ────────────────────────────────────────────
    //
    // Raised after the user confirms; the owner runs DROP PACKAGE, closes the tab,
    // and refreshes the metadata tree. A FB rejection (e.g. another object depends on
    // the package) surfaces to the owner's message log. Existing packages only.

    public event Func<PackageDetailTabViewModel, Task>? DeleteRequested;

    public bool CanDelete => _ddlExecutor is not null && !IsNew;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (!CanDelete) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.PackageDeleteConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.PackageDeleteConfirmFormat, PackageName),
            ConfirmLabel = UiStrings.PackageDeleteConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await (DeleteRequested?.Invoke(this) ?? Task.CompletedTask).ConfigureAwait(true);
    }

    // ─── Load / refresh ───────────────────────────────────────────────────

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        => _loadTask ??= LoadAsync(cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _loadTask = null;
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsNew) return;
        if (_reader is null || _ddlReader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        _suppressDirty = true;
        try
        {
            var obj = new MetadataObject(PackageName, MetadataObjectKind.Package);

            // Change-safety baseline. Dropped first, so a re-read that fails leaves the gate unverifiable
            // rather than holding a stale baseline that would authorise an overwrite.
            _changeGate.Forget();
            string? loadedHeader = null;
            string? loadedBody = null;

            await SafeLoadAsync(async () =>
            {
                loadedHeader = await _ddlReader.FetchPackageHeaderSourceAsync(obj, cancellationToken).ConfigureAwait(true);
                HeaderSource = loadedHeader;
            });

            await SafeLoadAsync(async () =>
            {
                loadedBody = await _ddlReader.FetchPackageBodySourceAsync(obj, cancellationToken).ConfigureAwait(true);
                BodySource = loadedBody;
            });

            // A package IS its header and its body together — Compile writes both — so the gate is armed only
            // when BOTH were read. The two steps above stay independently failure-trapped (a body-read failure
            // must not blank the header the user can see), which is exactly why the baseline is taken from the
            // raw reads rather than from the editable properties: those can still hold a previous load's text.
            if (loadedHeader is not null && loadedBody is not null)
            {
                _changeGate.CaptureBaseline(ComposeDefinition(loadedHeader, loadedBody));
            }

            await SafeLoadAsync(async () =>
            {
                var members = await _reader.GetPackageMembersAsync(PackageName, cancellationToken).ConfigureAwait(true);
                SetMembers(members);
            });

            await SafeLoadAsync(async () =>
            {
                var (dependsOn, dependedOnBy) = await _reader.GetPackageDependenciesAsync(PackageName, cancellationToken).ConfigureAwait(true);
                DependsOnTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
                DependedOnByTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
            });

            await SafeLoadAsync(async () =>
            {
                // DDL tab == Export (structure + COMMENT ON via MetadataExportService); the
                // editable header/body Source editors are untouched.
                DdlText = await new MetadataExportService(_ddlReader, _reader).BuildObjectScriptAsync(obj, cancellationToken).ConfigureAwait(true);
            });

            await SafeLoadAsync(async () =>
            {
                Description = await _reader.GetPackageDescriptionAsync(PackageName, cancellationToken).ConfigureAwait(true);
                DescriptionLoaded = true;
            });
        }
        finally
        {
            IsLoading = false;
            _suppressDirty = false;
            ClearDirty();
        }
    }

    private async Task SafeLoadAsync(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(true);
        }
        catch (MetadataReadException ex)
        {
            if (string.IsNullOrEmpty(ErrorMessage)) ErrorMessage = ex.Message;
        }
    }
}

/// <summary>Where a package member lives — body vs header, and the editor offset +
/// length of its name token (for select + scroll).</summary>
public sealed record PackageMemberLocation(bool InBody, int Offset, int Length);

/// <summary>A Members-tab group node (Functions / Procedures).</summary>
public sealed class PackageMemberGroupNode
{
    public string Header { get; init; } = string.Empty;
    public string IconGeometryKey { get; init; } = string.Empty;
    public string IconResourceKey { get; init; } = string.Empty;
    public IReadOnlyList<PackageMemberItemNode> Children { get; init; } = Array.Empty<PackageMemberItemNode>();
}

/// <summary>A Members-tab leaf — one packaged routine.</summary>
public sealed class PackageMemberItemNode
{
    public PackageMember Member { get; init; } = null!;
    public string DisplayName => Member.Name;
    public string IconGeometryKey { get; init; } = string.Empty;
    public string IconResourceKey { get; init; } = string.Empty;
    /// <summary>True for a PROCEDURE member — gates the "Debug procedure…" context-menu item (D11 seam C).</summary>
    public bool IsProcedure => Member?.Kind == PackageMemberKind.Procedure;
    /// <summary>True for a FUNCTION member — gates the "Debug function…" context-menu item (Seam D — a packaged
    /// function launches as a debug root).</summary>
    public bool IsFunction => Member?.Kind == PackageMemberKind.Function;
}
