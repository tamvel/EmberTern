using System;
using System.Collections.Generic;

namespace EmberTern.Core.Localization;

/// <summary>
/// What a language-unaware layer hands upwards when it has something to say: a <see cref="MessageKey"/> and
/// the <i>data</i> that belongs in the sentence — never the sentence.
///
/// <para>This is the whole of decision <b>D‑3</b>'s seam. Core/Firebird produce it; the App resolves it
/// against the current language and formats it. Nothing here knows what language exists, so there is nowhere
/// in this type — or in any producer of it — for a <c>language == "pl"</c> branch to live.</para>
///
/// <para>⚠ <b>This paragraph used to say the seam had no producer in Core and that migration must not start —
/// both true when written and both false since etap C1.</b> The Core/Firebird stage is under way and its
/// classification decisions are ratified: <c>SessionHealthMessages</c>, <c>QuickInfoMessages</c>,
/// <c>SettingsStoreMessages</c>, <c>SettingsExportMessages</c> and <c>FirebirdConnectionMessages</c> all produce
/// this type today. ⛔ The migration order and the "which messages stay raw" boundary are settled in
/// <c>docs/history/28-localization-core-stage.md</c> — read it before adding a producer, rather than inventing a
/// rule per module.</para>
///
/// <para>⚠ <b>Arguments are DATA and may legitimately contain English</b> — a raw Firebird error, a table
/// name, a path. Only the surrounding sentence is EmberTern's own voice, and that sentence is what the key
/// resolves to. Keeping a raw server message as an <i>argument</i> is exactly how the two are kept apart
/// (design decision D‑3: our wrappers are localizable, the server's own words stay the server's).</para>
/// </summary>
/// <param name="Key">Which message this is.</param>
/// <param name="Arguments">
/// The values to substitute, in order, into the resolved format string (<c>{0}</c>, <c>{1}</c>, …). Empty
/// when the message takes none. ⚠ Formatting is the App's job, because number and date shapes are the
/// reader's culture, not Core's.
/// </param>
public sealed record LocalizableMessage(MessageKey Key, IReadOnlyList<object?> Arguments)
{
    private static readonly object?[] NoArguments = Array.Empty<object?>();

    /// <summary>The common case — a key and its data, read the way a call site wants to write it.</summary>
    public static LocalizableMessage Of(MessageKey key, params object?[]? arguments)
        => new(key, arguments is { Length: > 0 } ? arguments : NoArguments);

    /// <summary>A message with no substitutions.</summary>
    public static LocalizableMessage Of(string key) => new(new MessageKey(key), NoArguments);

    /// <summary>
    /// ⭐⭐ <b>STRUCTURAL equality — element-wise over <see cref="Arguments"/>, not by list reference.</b> Added
    /// in etap C5, and it is the decision that lets this type live inside a value type.
    ///
    /// <para><b>Why the synthesized version was not usable.</b> A positional record compares each member with
    /// <c>EqualityComparer&lt;T&gt;.Default</c>, and for <c>IReadOnlyList&lt;object?&gt;</c> that resolves to the
    /// backing <c>object?[]</c>'s <i>reference</i> equality. So two messages built from the same key and the same
    /// data were unequal. Harmless while nothing compared them — and a silent defect the moment one is embedded
    /// in something whose value equality matters: <c>Diagnostic</c> is a <c>readonly record struct</c> and
    /// <c>DiagnosticsPanelViewModel.Update</c> skips rebuilding its <c>ObservableCollection</c> (and keeps the
    /// user's selection) precisely by comparing findings. With reference equality the panel would have churned on
    /// every debounce tick, dropping the selection, with a green build and no failing test.</para>
    ///
    /// <para>⚠ <b>The precondition this introduces, stated because it is the only way to break it: an argument
    /// must itself be value-equatable.</b> Every argument in the codebase today is a <c>string</c> or an integer
    /// (a boxed <c>int</c> compares by value), which is also what C4b's "arguments are already-formatted data"
    /// discipline produces. ⛔ An argument of a type that does not override <c>Equals</c> — an array, a mutable
    /// holder — would silently restore reference comparison, so a guard forbids one
    /// (<c>DiagnosticsLocalizationTests.NoProducerPassesAnArgumentWithoutValueEquality</c>).</para>
    ///
    /// <para>⚠ Measured before changing it: <b>nothing in the codebase compared this type</b>, so the change can
    /// only make more messages equal, never fewer. It is additive in effect as well as in shape.</para>
    /// </summary>
    public bool Equals(LocalizableMessage? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (!Key.Equals(other.Key)) return false;
        if (Arguments.Count != other.Arguments.Count) return false;

        for (var i = 0; i < Arguments.Count; i++)
        {
            if (!EqualityComparer<object?>.Default.Equals(Arguments[i], other.Arguments[i])) return false;
        }

        return true;
    }

    /// <summary>
    /// The count this message is about, when it has one: <b>argument {0}, if it is an integer</b>.
    ///
    /// <para>⭐ This is the whole of the ratified convention <b>R3</b> — <i>a count is always the first
    /// argument</i> — and it lives here so that there is exactly one place that reads it. Two readers (Core's
    /// English fallback and the App's plural resolver) asking the same question in two ways is how the two
    /// halves of a dual form drift, which is #357's shape.</para>
    ///
    /// <para>⛔ It says nothing about grammar. Whether a sentence carrying a count needs several forms is a
    /// property of the LANGUAGE and is answered in the App, against the reader's own culture; a message that
    /// happens to carry an integer first argument (a transaction id, a version number) simply resolves flat,
    /// because the catalog declares no category variants for its key.</para>
    ///
    /// <para>⚠ <c>int</c> is accepted beside <c>long</c> deliberately: a producer that writes
    /// <c>Of(key, someInt)</c> would otherwise be silently un-pluralizable, and a silent miss is the failure
    /// mode this stage keeps meeting (#337).</para>
    /// </summary>
    public bool TryGetCount(out long count)
    {
        if (Arguments.Count > 0)
        {
            switch (Arguments[0])
            {
                case long l: count = l; return true;
                case int i: count = i; return true;
            }
        }

        count = 0;
        return false;
    }

    /// <inheritdoc cref="Equals(LocalizableMessage)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        foreach (var argument in Arguments)
        {
            hash.Add(argument);
        }
        return hash.ToHashCode();
    }
}
