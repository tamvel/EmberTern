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
/// <para>⚠ <b>Status after L1: the seam is built and exercised, but has no PRODUCER in Core yet.</b> That is
/// deliberate and it is a knowing exception to the project's usual "no component without a consumer" rule
/// (gotcha #233). The narrower truth is that the seam <i>is</i> consumed — the App's resolver takes this type
/// — and what is missing is only the ~250–300 Core call sites, which are stage <b>L4</b> and were explicitly
/// excluded from L1. ⛔ Do not "tidy this away" as dead code before L4; and ⛔ do not start migrating Core
/// messages onto it early, because that stage's decisions (which messages stay raw) are not taken yet.</para>
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
}
