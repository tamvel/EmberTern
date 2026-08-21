using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// One of OUR sentences, carried as a value — a key and its arguments that render only when read.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>It exists for the case a single key cannot express: a line assembled from a VARIABLE NUMBER
/// of our own complete sentences.</b> The SMTP form reports every validation problem at once, and the
/// register reports every integrity disagreement at once — so the count is not known when the key is
/// chosen. Storing the joined result as text would freeze it; giving the joined result to
/// <see cref="StatusMessage"/> as an argument would freeze it too, if it were a <c>string</c>.</para>
///
/// <para>⭐⭐ <b>The mechanism that makes it work is <c>string.Format</c>'s own contract: it calls
/// <see cref="object.ToString"/> on each argument AT FORMAT TIME.</b> Because
/// <see cref="StatusMessage.Text"/> formats on every read, an argument that resolves in its
/// <see cref="ToString"/> resolves on every read as well. So a message can hold live sentences inside a
/// live sentence, with no extra machinery and no second notification path.</para>
///
/// <para>⛔ It is NOT a way to hand the catalog half a sentence. Every element is a COMPLETE sentence with
/// its own key; what varies is how many of them there are. Splitting one sentence into fragments and
/// gluing them here would be the same defect in a new costume — word order is the translator's decision.</para>
/// </remarks>
public sealed class LocalizedText
{
    private readonly object?[] _arguments;

    /// <summary>Creates a deferred sentence.</summary>
    public LocalizedText(MessageKey key, params object?[] arguments)
    {
        Key = key;
        _arguments = arguments ?? [];
    }

    /// <summary>Which sentence this is.</summary>
    public MessageKey Key { get; }

    /// <summary>The values it interpolates.</summary>
    public IReadOnlyList<object?> Arguments => _arguments;

    /// <summary>⭐ Resolved HERE, at format time — see the type's remarks.</summary>
    public override string ToString() => Loc.Format(Key.Value, _arguments);
}

/// <summary>
/// Several of our sentences, rendered as one run of text when read.
/// </summary>
/// <remarks>
/// ⚠ The separator is a plain space, matching what the old <c>string.Join(" ", problems)</c> produced —
/// L8.2 may not change a single rendered character. ⭐ Each element is a whole sentence, so joining them is
/// punctuation, not composition.
/// </remarks>
public sealed class LocalizedSentences
{
    private readonly IReadOnlyList<LocalizedText> _sentences;

    /// <summary>Creates the run.</summary>
    public LocalizedSentences(IEnumerable<LocalizedText> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        _sentences = [.. sentences];
    }

    /// <summary>How many sentences there are.</summary>
    public int Count => _sentences.Count;

    /// <summary>⭐ Every element resolved at format time, then joined.</summary>
    public override string ToString() => string.Join(" ", _sentences.Select(s => s.ToString()));
}
