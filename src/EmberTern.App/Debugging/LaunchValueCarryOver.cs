using System;
using System.Collections.Generic;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Debugging;

/// <summary>
/// Carries the values already entered on a launch panel across a rebuild of that panel (the debugger's
/// launch-config rebuild). The rule it exists to serve: <b>keep everything that can be proven still correct,
/// hand back everything that cannot, and never guess in between.</b>
/// <para>
/// It knows nothing about procedures, functions, package members or triggers — it works on rows
/// (<see cref="ExecuteProcedureParamRowViewModel"/>), which is what all of those launch surfaces are built
/// from. That is deliberate: a rule that had to be told what kind of routine it was looking at would be a rule
/// with places to diverge.
/// </para>
/// <para>
/// <b>The proof is not implemented here.</b> A value is moved through the same stored form the parameter
/// history uses (<see cref="ExecuteProcedureParamRowViewModel.ToHistoryValue"/> →
/// <see cref="ExecuteProcedureParamRowViewModel.ApplyHistoryValue"/>), which already refuses anything whose
/// recorded type does not classify to the target row's input kind. So carry-over and history cannot drift
/// apart on what "provably still fits" means — there is one answer, in one place.
/// </para>
/// <para>
/// <b>Two primitives, composed by the caller.</b> <see cref="ByName"/> is always right: a parameter is the
/// same parameter when it is called the same thing. <see cref="SoleRemainingPair"/> is the one inference the
/// panel makes, and it is only sound where identity is positional — so the caller composes
/// <c>ByName → SoleRemainingPair</c> for a routine's parameters and <c>ByName</c> alone for a trigger's
/// NEW/OLD columns, whose identity is the column name in the catalog and nothing else. The difference is a
/// line you can read at the call site rather than a flag inside a method.
/// </para>
/// </summary>
internal static class LaunchValueCarryOver
{
    /// <summary>Which rows on each side a pass has already spoken for. Passed from one primitive to the next so
    /// the second only ever considers what the first left over.</summary>
    internal sealed class Matches
    {
        public HashSet<int> Previous { get; } = new();
        public HashSet<int> Current { get; } = new();
    }

    /// <summary>Carries each previous row's value into the current row of the same name (case-insensitive).
    /// <para>A name match <b>consumes</b> the pair whether or not the value survives the proof: the two rows
    /// are the same parameter, and the proof decides only whether its value still means anything. Leaving a
    /// retyped parameter unconsumed would let it be paired positionally with some other row, which would be a
    /// guess about two parameters we can already tell apart.</para></summary>
    internal static Matches ByName(
        IReadOnlyList<ExecuteProcedureParamRowViewModel> previous,
        IReadOnlyList<ExecuteProcedureParamRowViewModel> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var matches = new Matches();
        for (int j = 0; j < current.Count; j++)
        {
            for (int i = 0; i < previous.Count; i++)
            {
                if (matches.Previous.Contains(i)) continue;
                if (!string.Equals(previous[i].Name, current[j].Name, StringComparison.OrdinalIgnoreCase)) continue;

                matches.Previous.Add(i);
                matches.Current.Add(j);
                current[j].ApplyHistoryValue(previous[i].ToHistoryValue(), ValueOrigin.Restored);
                break;
            }
        }
        return matches;
    }

    /// <summary>Carries the value when — and only when — exactly one row is left unmatched on each side. That
    /// is the renamed-parameter case, and the single point at which the panel infers anything: the position is
    /// the only evidence there is, so one candidate is evidence and several are guesswork. Two or more left
    /// over on either side carries nothing at all.
    /// <para>The carried row reports <see cref="ValueOrigin.Assumed"/>, because a renamed parameter and one
    /// that merely replaced another are indistinguishable in the text — the user is told an assumption was
    /// made rather than left to discover it.</para></summary>
    internal static void SoleRemainingPair(
        IReadOnlyList<ExecuteProcedureParamRowViewModel> previous,
        IReadOnlyList<ExecuteProcedureParamRowViewModel> current,
        Matches matches)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(matches);

        if (SoleRemaining(previous.Count, matches.Previous) is not { } i) return;
        if (SoleRemaining(current.Count, matches.Current) is not { } j) return;

        matches.Previous.Add(i);
        matches.Current.Add(j);
        current[j].ApplyHistoryValue(previous[i].ToHistoryValue(), ValueOrigin.Assumed);
    }

    // The index of the only unmatched row, or null when none or several are left.
    private static int? SoleRemaining(int count, HashSet<int> matched)
    {
        int? only = null;
        for (int i = 0; i < count; i++)
        {
            if (matched.Contains(i)) continue;
            if (only is not null) return null; // more than one candidate — nothing to infer from
            only = i;
        }
        return only;
    }
}
