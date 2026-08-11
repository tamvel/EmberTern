using System;
using System.Collections.Generic;
using System.Globalization;
using EmberTern.Core.Localization;

namespace EmberTern.Core.Query;

/// <summary>
/// The English half of C6's <b>dual form</b>: the exact wording <see cref="ExecutionSummary"/> and
/// <see cref="ExecutionActivity"/> emitted before the migration, expressed as a resolver so that the English
/// and the localized renders go through <i>the same layout code</i> and can differ only in their words.
///
/// <para>⭐ <b>Why a dual form here.</b> Fourteen <c>ExecutionSummaryTests</c> and four
/// <c>ExecutionActivityTests</c> pin these sentences literally. Under §2.7's measured criterion that makes
/// this a dual-form migration (like C3 / C4a / C4b), and the payoff is the strongest proof available: the
/// existing tests stay untouched, and a separate guard requires the catalog to reproduce every one of these
/// strings for every shape and every count.</para>
///
/// <para>⛔ <b>This is a FALLBACK, not the mechanism.</b> It renders English because it <i>is</i> English —
/// including English's two-way singular/plural split, which is stated here in the one place where the
/// language is known and nowhere else. A localized render never reaches this class: it passes
/// <c>Loc.Format</c>, and the plural category then comes from the reader's own culture. ⛔ Do not consult
/// this table from a localized path, and do not "generalise" it into a rules engine — the moment it decides
/// a category for a language other than English it has taken a decision Core is not allowed to take.</para>
///
/// <para>⚠ Numbers are formatted with <see cref="CultureInfo.CurrentCulture"/>, matching <c>Loc.Format</c>
/// (localization.md §4.2 — words follow the language, numbers follow the reader). For the integral counts
/// this module carries that is byte-identical to the invariant formatting it replaces, because a
/// <c>long</c> under the default <c>G</c> format never groups; the unification exists so the two halves
/// cannot drift on the number side and the equality guard measures WORDS only (#357).</para>
/// </summary>
internal static class ExecutionEnglish
{
    /// <summary>Keys with one English form.</summary>
    private static readonly Dictionary<string, string> Flat = new(StringComparer.Ordinal)
    {
        [QueryExecutionMessages.ExecutedIn.Value] = "Executed in {0} ms",
        [QueryExecutionMessages.NoModifications.Value] = "No data modifications detected.",
        [QueryExecutionMessages.StatusFormat.Value] = "{0} in {1} ms",
        [QueryExecutionMessages.StatusInserted.Value] = "inserted {0}",
        [QueryExecutionMessages.StatusUpdated.Value] = "updated {0}",
        [QueryExecutionMessages.StatusDeleted.Value] = "deleted {0}",
        [QueryExecutionMessages.TermInserted.Value] = "{0} inserted",
        [QueryExecutionMessages.TermUpdated.Value] = "{0} updated",
        [QueryExecutionMessages.TermDeleted.Value] = "{0} deleted",
        [QueryExecutionMessages.TermRead.Value] = "{0} read",
        [QueryExecutionMessages.TableInserted.Value] = "{0} inserted into {1}",
        [QueryExecutionMessages.TableUpdated.Value] = "{0} updated in {1}",
        [QueryExecutionMessages.TableDeleted.Value] = "{0} deleted from {1}",
    };

    /// <summary>Keys whose English wording depends on the count in <c>{0}</c>: singular, then plural.</summary>
    private static readonly Dictionary<string, (string One, string Other)> Counted = new(StringComparer.Ordinal)
    {
        [QueryExecutionMessages.RowsInserted.Value] = ("{0} row inserted", "{0} rows inserted"),
        [QueryExecutionMessages.RowsUpdated.Value] = ("{0} row updated", "{0} rows updated"),
        [QueryExecutionMessages.RowsDeleted.Value] = ("{0} row deleted", "{0} rows deleted"),
        [QueryExecutionMessages.RowsRead.Value] = ("{0} row read", "{0} rows read"),
        [QueryExecutionMessages.RowsAffected.Value] = ("{0} row affected", "{0} rows affected"),
    };

    /// <summary>The English text for a message this module produces.</summary>
    /// <exception cref="ArgumentException">
    /// The key is not one of this module's. ⚠ Deliberately loud: this resolver is reached only from Core's
    /// own no-argument overloads, so an unknown key means a producer added a key without its English form —
    /// a build-time defect, not something a user can provoke.
    /// </exception>
    public static string Resolve(LocalizableMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var key = message.Key.Value;
        string format;

        if (Flat.TryGetValue(key, out var flat))
        {
            format = flat;
        }
        else if (Counted.TryGetValue(key, out var counted))
        {
            // English's split, and only English's: exactly one is singular. ⛔ Never generalise this — a
            // language with more categories is served by its own catalog entries, chosen in the App.
            format = message.TryGetCount(out var count) && count == 1 ? counted.One : counted.Other;
        }
        else
        {
            throw new ArgumentException(
                $"'{key}' has no English form in {nameof(ExecutionEnglish)}. Every key this module produces " +
                "needs one, because the no-resolver overloads are what unmigrated callers and the tests use.",
                nameof(message));
        }

        if (message.Arguments.Count == 0)
        {
            return format;
        }

        var arguments = new object?[message.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = message.Arguments[i];
        }

        return string.Format(CultureInfo.CurrentCulture, format, arguments);
    }
}
