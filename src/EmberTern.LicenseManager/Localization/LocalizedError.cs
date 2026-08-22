using System;
using System.Collections.Generic;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// An exception whose sentence is OURS, and which therefore carries its catalog key.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>It exists because "the raw text travels as an argument" is right for a FOREIGN message and
/// wrong for one of our own.</b> An <c>IOException</c>'s words belong to Windows and are not ours to
/// translate; but a sentence we wrote, thrown from our own code and printed through <c>ex.Message</c>,
/// would stay English forever behind a perfectly translated catalog — which is the Phase‑5 defect
/// (§17.3) one layer further in, and exactly what L8.2 exists to remove.</para>
///
/// <para>⛔ <b>This is NOT a migration of the exception system, and must not become one</b> (the user's
/// ratified narrowing of decision P1). It is implemented by the small number of throw sites whose sentence
/// reaches the message strip UNFRAMED — measured at two: the keystore-already-exists refusal and the
/// SMTP sender's no-host refusal. ⚠ Every other exception in this application keeps its plain type, and
/// its message keeps travelling as an ARGUMENT to one of our keys
/// (<c>"The backup could not be written: {0}"</c>). That framing is correct and is not debt.</para>
///
/// <para>⭐ An interface rather than a base class, because the two throw sites need different bases:
/// callers already <c>catch (ArgumentException)</c> and <c>catch (InvalidOperationException)</c>
/// respectively, and changing what a method throws would be a behavioural change smuggled into a
/// localization stage.</para>
/// </remarks>
public interface ILocalizedError
{
    /// <summary>The catalog key for this failure's sentence.</summary>
    MessageKey Key { get; }

    /// <summary>The values that sentence interpolates.</summary>
    IReadOnlyList<object?> Arguments { get; }
}

/// <summary>An <see cref="ArgumentException"/> whose sentence is ours. See <see cref="ILocalizedError"/>.</summary>
public sealed class LocalizedArgumentException : ArgumentException, ILocalizedError
{
    /// <summary>Creates the exception.</summary>
    /// <param name="key">The catalog key for the sentence the operator will read.</param>
    /// <param name="englishText">
    /// ⚠ The same sentence in English, for diagnostics only — a debugger, a crash log, a stack trace.
    /// ⛔ Never displayed: the display path is <see cref="Key"/>. Keeping it here means an unhandled
    /// escape still says something a developer can read.
    /// </param>
    /// <param name="paramName">The offending parameter, as <see cref="ArgumentException"/> expects.</param>
    /// <param name="arguments">The values the sentence interpolates.</param>
    public LocalizedArgumentException(
        MessageKey key, string englishText, string? paramName, params object?[] arguments)
        : base(englishText, paramName)
    {
        Key = key;
        Arguments = arguments ?? [];
    }

    /// <inheritdoc />
    public MessageKey Key { get; }

    /// <inheritdoc />
    public IReadOnlyList<object?> Arguments { get; }
}

/// <summary>An <see cref="InvalidOperationException"/> whose sentence is ours.</summary>
public sealed class LocalizedOperationException : InvalidOperationException, ILocalizedError
{
    /// <summary>Creates the exception.</summary>
    /// <param name="key">The catalog key for the sentence the operator will read.</param>
    /// <param name="englishText">⚠ Diagnostics only — see <see cref="LocalizedArgumentException"/>.</param>
    /// <param name="arguments">The values the sentence interpolates.</param>
    public LocalizedOperationException(MessageKey key, string englishText, params object?[] arguments)
        : base(englishText)
    {
        Key = key;
        Arguments = arguments ?? [];
    }

    /// <inheritdoc />
    public MessageKey Key { get; }

    /// <inheritdoc />
    public IReadOnlyList<object?> Arguments { get; }
}
