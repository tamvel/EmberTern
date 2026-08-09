using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Controls;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The Database Properties window's content.
///
/// <para>⭐ <b>Reader and writer arrive as delegates, not as Firebird types.</b> Not layering ceremony —
/// it is what makes every rule below (what Apply sends, what Apply is gated on, how a partial success reads)
/// testable with no server at all, which is exactly where the risk of this feature lives.</para>
///
/// <para>⚠ <b>Explicit Apply, deliberately the OPPOSITE of Settings Center's ratified apply-on-change.</b>
/// Settings Center writes a local file; this writes to a shared production database through an API with no
/// rollback, outside every connection lane and outside the user's transaction.</para>
/// </summary>
public sealed partial class DatabasePropertiesViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<DatabaseProperties>> _load;
    private readonly Func<DatabaseConfigurationChange, CancellationToken, Task<DatabaseConfigurationResult>> _apply;

    private DatabaseProperties? _original;

    public DatabasePropertiesViewModel(
        string connectionName,
        bool canAttemptWrite,
        Func<CancellationToken, Task<DatabaseProperties>> load,
        Func<DatabaseConfigurationChange, CancellationToken, Task<DatabaseConfigurationResult>> apply)
    {
        ConnectionName = connectionName;
        CanAttemptWrite = canAttemptWrite;
        _load = load;
        _apply = apply;
    }

    public string ConnectionName { get; }

    /// <summary>
    /// Whether an Apply can be attempted at all.
    /// <para>⭐ False only for the one case measurement showed is knowable UP FRONT: a profile with no stored
    /// password, which the driver refuses before reaching the server. ⛔ The <c>USE_GFIX_UTILITY</c> privilege
    /// is deliberately NOT pre-checked (ratified) — it is not knowable without trying, and its server message
    /// is specific enough to stand on its own.</para>
    /// </summary>
    public bool CanAttemptWrite { get; }

    // ── Informational ───────────────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _databasePath = string.Empty;
    [ObservableProperty] private string _owner = string.Empty;
    [ObservableProperty] private string _engineVersion = string.Empty;
    [ObservableProperty] private string _ods = string.Empty;
    [ObservableProperty] private string _dialect = string.Empty;
    [ObservableProperty] private string _charset = string.Empty;
    [ObservableProperty] private string _createdAt = string.Empty;
    [ObservableProperty] private string _pageSize = string.Empty;
    [ObservableProperty] private string _pages = string.Empty;
    [ObservableProperty] private string _size = string.Empty;
    [ObservableProperty] private string _pageBuffers = string.Empty;
    [ObservableProperty] private string _linger = string.Empty;

    // ── Editable ────────────────────────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _sweepIntervalText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _forcedWrites;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _reserveSpace;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _isLoaded;

    // ── Message surface ─────────────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string? _message;
    [ObservableProperty] private MessageSeverity _messageSeverity = MessageSeverity.Error;
    [ObservableProperty] private bool _hasMessage;

    /// <summary>The reason the editors are unavailable, or null when they are available.</summary>
    public string? WriteBlockedReason => CanAttemptWrite ? null : UiStrings.DatabasePropertiesNoPassword;

    public bool ShowWriteBlockedReason => !CanAttemptWrite;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            Adopt(await _load(cancellationToken).ConfigureAwait(true));
            IsLoaded = true;
            Clear();
        }
        catch (Exception ex)
        {
            Show(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// What Apply would send. Public so a guard can assert the central promise — <b>only edited values
    /// travel</b> — without going near a server.
    /// </summary>
    public DatabaseConfigurationChange PendingChange
        => _original is null || !TryParseSweep(out var sweep)
            ? new DatabaseConfigurationChange()
            : DatabaseConfigurationChange.Between(_original, sweep, ForcedWrites, ReserveSpace);

    private bool CanApply() => CanAttemptWrite && !IsBusy && IsLoaded && PendingChange.HasChanges;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var change = PendingChange;
        if (!change.HasChanges)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _apply(change, cancellationToken).ConfigureAwait(true);

            // ⚠ Re-read ALWAYS — after a full success, a partial one and a total failure alike. The window
            // must show what the database now holds rather than what the user asked for, and after a partial
            // Apply those two genuinely differ.
            try
            {
                Adopt(await _load(cancellationToken).ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                Show(ex.Message, MessageSeverity.Error);
                return;
            }

            Report(result);
        }
        catch (Exception ex)
        {
            Show(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Turns one Apply into the message the user sees.
    /// <para>⭐ Pure and public so the partial-success wording is assertable — that is the branch a live test
    /// cannot reach on demand, because it needs two settings to disagree about succeeding.</para>
    /// </summary>
    public static (string Text, MessageSeverity Severity) Describe(DatabaseConfigurationResult result)
    {
        if (result.Outcomes.Count == 0)
        {
            return (UiStrings.DatabasePropertiesNothingToApply, MessageSeverity.Info);
        }

        if (result.AllSucceeded)
        {
            return (UiStrings.DatabasePropertiesApplied, MessageSeverity.Success);
        }

        var lines = new List<string>();
        if (result.IsPartial)
        {
            // ⚠ Naming what DID land is the load-bearing half: Apply is not atomic, so "it failed" would
            // leave the user unable to tell which changes are now live in the database.
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.DatabasePropertiesPartial,
                string.Join(", ", result.Outcomes.Where(o => o.Succeeded).Select(o => NameOf(o.Setting)))));
        }

        foreach (var failure in result.Failures)
        {
            // ⭐ The server's raw words ALWAYS, with a short lead ONLY where the case is recognised by
            // SQLSTATE / GDS. Never by message text — see DatabaseConfigurationDiagnosis.
            var lead = DatabaseConfigurationDiagnosis.Classify(failure) switch
            {
                DatabaseApplyFailure.MissingPrivilege => UiStrings.DatabasePropertiesMissingPrivilege + " ",
                DatabaseApplyFailure.DatabaseInUse => UiStrings.DatabasePropertiesInUse + " ",
                _ => string.Empty,
            };
            lines.Add($"{NameOf(failure.Setting)}: {lead}{failure.Error}");
        }

        return (string.Join(Environment.NewLine, lines),
            result.IsPartial ? MessageSeverity.Warning : MessageSeverity.Error);
    }

    private static string NameOf(DatabaseSetting setting) => setting switch
    {
        DatabaseSetting.SweepInterval => UiStrings.DatabasePropertiesSweepInterval,
        DatabaseSetting.ForcedWrites => UiStrings.DatabasePropertiesForcedWrites,
        DatabaseSetting.ReserveSpace => UiStrings.DatabasePropertiesReserveSpace,
        _ => setting.ToString(),
    };

    /// <summary>Human-readable size. Pure + internal so the rounding is pinned without a database.</summary>
    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Format(CultureInfo.CurrentCulture, "{0:N0} {1}", value, units[unit])
            : string.Format(CultureInfo.CurrentCulture, "{0:N1} {1}", value, units[unit]);
    }

    private bool TryParseSweep(out int value)
        => int.TryParse(SweepIntervalText, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
           && value >= 0;

    private void Adopt(DatabaseProperties p)
    {
        _original = p;

        DatabasePath = p.DatabasePath;
        Owner = p.Owner;
        EngineVersion = p.EngineVersion;
        Ods = $"{p.OdsMajor}.{p.OdsMinor}";
        Dialect = p.Dialect.ToString(CultureInfo.CurrentCulture);
        Charset = p.Charset;
        CreatedAt = p.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        PageSize = string.Format(CultureInfo.CurrentCulture, "{0:N0}", p.PageSize);
        Pages = string.Format(CultureInfo.CurrentCulture, "{0:N0}", p.Pages);
        Size = FormatSize(p.SizeBytes);
        PageBuffers = string.Format(CultureInfo.CurrentCulture, "{0:N0}", p.PageBuffers);
        // ⚠ NULL is "not set", not 0 — the distinction the reader preserves on purpose.
        Linger = p.LingerSeconds is { } seconds
            ? string.Format(CultureInfo.CurrentCulture, UiStrings.DatabasePropertiesLingerSeconds, seconds)
            : UiStrings.DatabasePropertiesLingerNotSet;

        SweepIntervalText = p.SweepInterval.ToString(CultureInfo.CurrentCulture);
        ForcedWrites = p.ForcedWrites;
        ReserveSpace = p.ReserveSpace;

        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void Report(DatabaseConfigurationResult result)
    {
        var (text, severity) = Describe(result);
        Show(text, severity);
    }

    private void Show(string text, MessageSeverity severity)
    {
        Message = text;
        MessageSeverity = severity;
        HasMessage = true;
    }

    private void Clear()
    {
        Message = null;
        HasMessage = false;
    }
}
