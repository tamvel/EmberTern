using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>The outcome of one change-safety check: the verdict, plus the message to show when it refuses.</summary>
internal readonly record struct ObjectChangeCheck(ObjectChangeVerdict Verdict, string? RefusalMessage)
{
    public bool MayProceed => Verdict == ObjectChangeVerdict.Safe;

    public static ObjectChangeCheck Allowed => new(ObjectChangeVerdict.Safe, null);
}

/// <summary>
/// One editor's change-safety state and the act of checking it — the App-side half of
/// <see cref="ObjectChangeSafety"/>, which owns the pure decision.
/// <para>
/// The split is deliberate. The decision table (what counts as a conflict, and why unverifiable is not
/// permission) is pure Core and unit-testable with no server. Everything that cannot be pure lives here:
/// holding the baseline across the tab's life, calling back into the caller's own reader, and turning a
/// verdict into a sentence the user can act on. The reader arrives as a delegate rather than as a typed
/// dependency because each editor reads its own kind of object through its own call — the gate must not
/// grow a per-kind switch, which would be a second place that knows how to read a definition.
/// </para>
/// <para><b>One gate per editor, not a shared service.</b> The baseline belongs to a tab: two tabs on the
/// same object have independently loaded it and must be judged independently.</para>
/// <para><b>What refusal means.</b> The gate never writes and never repairs; it reports. Every caller today
/// treats anything other than <see cref="ObjectChangeCheck.MayProceed"/> as "do not execute the DDL, show
/// the message" — Architecture rule #11's "uncertainty ⇒ do nothing or ask". Because the caller refuses
/// before executing, its <c>ErrorMessage</c> is set and the save-and-close WorkGuard correctly reads the
/// save as failed rather than discarding the user's buffer.</para>
/// </summary>
internal sealed class ObjectChangeGate
{
    private string? _baseline;

    /// <summary>The witness for what the database held at load time, or null when none was captured.
    /// Exposed for tests and diagnostics — never for content (it is a hash, not source).</summary>
    public string? BaselineFingerprint => _baseline;

    /// <summary>
    /// Records what the database holds, as the editor loads it. Called from the load path, so it also
    /// re-arms after a successful compile (which reloads) and after Revert — a stale baseline would refuse
    /// the user's very next legitimate compile.
    /// <para>A blank definition captures nothing, leaving the gate unverifiable rather than pretending an
    /// unread object was read.</para>
    /// </summary>
    public void CaptureBaseline(string? definition) => _baseline = ObjectChangeSafety.Fingerprint(definition);

    /// <summary>Drops the baseline — the object this gate was guarding is no longer the one in front of the
    /// user (e.g. a rename reopened the tab under a new name).</summary>
    public void Forget() => _baseline = null;

    /// <summary>
    /// Checks whether overwriting an existing object is safe, re-reading its current definition through
    /// <paramref name="readCurrentAsync"/>.
    /// </summary>
    /// <param name="objectLabel">How to name the object in the refusal message.</param>
    /// <param name="readCurrentAsync">
    /// Reads the definition the database holds NOW. Must be the same read that produced the baseline, or
    /// the comparison compares two different artifacts and every compile looks like a conflict.
    /// </param>
    public async Task<ObjectChangeCheck> CheckOverwriteAsync(
        string objectLabel,
        Func<CancellationToken, Task<string?>> readCurrentAsync,
        CancellationToken cancellationToken = default)
    {
        string? current;
        try
        {
            current = await readCurrentAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The user's own decision, never a conflict and never a fault (gotcha #253). Let it out.
            throw;
        }
        catch (Exception)
        {
            // The read reaches the world through a delegate, so its exception types are not this class's to
            // enumerate. Any failure means the same thing: we cannot prove the write is safe.
            current = null;
        }

        return Describe(ObjectChangeSafety.EvaluateOverwrite(_baseline, current), objectLabel);
    }

    /// <summary>
    /// Checks whether creating a new object under <paramref name="objectLabel"/> is safe, establishing
    /// whether the name is already taken through <paramref name="existsAsync"/>.
    /// <para>A blank name skips the check: there is nothing to look up, and the editors already refuse an
    /// unnamed object with their own validation.</para>
    /// </summary>
    public async Task<ObjectChangeCheck> CheckCreateAsync(
        string objectLabel,
        Func<CancellationToken, Task<bool>>? existsAsync,
        CancellationToken cancellationToken = default)
    {
        if (existsAsync is null || string.IsNullOrWhiteSpace(objectLabel))
        {
            return Describe(ObjectChangeSafety.EvaluateCreate(nameIsTaken: null), objectLabel);
        }

        bool? taken;
        try
        {
            taken = await existsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            taken = null; // could not find out — not the same as free
        }

        return Describe(ObjectChangeSafety.EvaluateCreate(taken), objectLabel);
    }

    // The ONE verdict→message mapping. Kept here rather than at each call site so eight editors cannot
    // describe the same conflict eight different ways.
    private static ObjectChangeCheck Describe(ObjectChangeVerdict verdict, string objectLabel)
    {
        var label = string.IsNullOrWhiteSpace(objectLabel) ? UiStrings.ObjectChangeUnnamedObject : objectLabel;
        var format = verdict switch
        {
            ObjectChangeVerdict.Safe => null,
            ObjectChangeVerdict.ChangedInDatabase => UiStrings.ObjectChangedInDatabaseFormat,
            ObjectChangeVerdict.AlreadyExists => UiStrings.ObjectAlreadyExistsFormat,
            ObjectChangeVerdict.Unverifiable => UiStrings.ObjectChangeUnverifiableFormat,
            _ => UiStrings.ObjectChangeUnverifiableFormat,
        };

        return new ObjectChangeCheck(
            verdict,
            format is null ? null : string.Format(CultureInfo.CurrentCulture, format, label));
    }
}
