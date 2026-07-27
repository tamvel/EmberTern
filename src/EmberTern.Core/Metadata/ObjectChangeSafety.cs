using System;
using System.Security.Cryptography;
using System.Text;

namespace EmberTern.Core.Metadata;

/// <summary>What a pre-compile safety check concluded about writing an object definition.</summary>
public enum ObjectChangeVerdict
{
    /// <summary>The database still holds exactly what the editor loaded (or, for a new object, holds
    /// nothing under that name). The write may proceed.</summary>
    Safe,

    /// <summary>The definition in the database is no longer the one the editor loaded — another session
    /// compiled it, or dropped it, in the meantime. Writing would silently discard that work.</summary>
    ChangedInDatabase,

    /// <summary>New-object flow: something already exists under the chosen name. Because every editor
    /// generates <c>CREATE OR ALTER</c>, proceeding would overwrite that object instead of failing.</summary>
    AlreadyExists,

    /// <summary>Safety could not be established — no baseline was captured, or the current state could not
    /// be read. Not a conflict, and not permission to write either.</summary>
    Unverifiable,
}

/// <summary>
/// The pre-compile change-safety check for object definitions: <b>may EmberTern write over what the
/// database holds right now?</b>
/// <para>
/// This exists because every source editor compiles by REPLACING a whole object
/// (<c>CREATE OR ALTER PROCEDURE … AS &lt;entire body&gt;</c>). Two distinct hazards follow, and they are
/// asked as two separate questions here because they rest on different evidence:
/// </para>
/// <list type="number">
///   <item><b>Overwrite an existing object</b> (<see cref="EvaluateOverwrite"/>). A colleague who compiles
///   the same procedure after this editor loaded it has their version discarded, with no error, the moment
///   the user presses Compile — the buffer descends from a definition that no longer exists.</item>
///   <item><b>Create over someone else's object</b> (<see cref="EvaluateCreate"/>). In the New-object flow
///   the generated statement is <c>CREATE OR ALTER</c> as well, so typing the name of an existing object
///   overwrites it rather than failing. That hazard needs no concurrency at all — one user and a name
///   collision are enough, which arguably makes it the likelier of the two.</item>
/// </list>
/// <para><b>Why re-read and compare, rather than a version number.</b> Firebird's catalog carries no
/// change counter or modification timestamp for a routine — there is no <c>RDB$UPDATE_TIME</c>, no row
/// version, nothing that increments when a procedure is recompiled. Re-reading the definition and
/// comparing it is therefore the only mechanism the engine makes available, not one option among several.
/// </para>
/// <para><b>Why the comparison is byte-exact and unnormalised.</b> The baseline is taken over the very
/// artifact the editor loaded, and that artifact is rebuilt from the catalog by a deterministic
/// reconstruction — so an unchanged object yields an identical string and cannot produce a false conflict.
/// Normalising (line endings, whitespace, case) would be strictly worse: a whitespace-only edit to a
/// routine body IS a change to the user's code, and silently tolerating it is exactly the "close enough"
/// reasoning Architecture rule #11 forbids. One accepted consequence, in the safe direction: changing the
/// connection charset mid-session can change how the same stored bytes decode, which would read as a
/// conflict rather than as permission to write. Revert re-reads and re-captures.</para>
/// <para><b>Why "removed from the database" is not a verdict.</b> It would be unreachable. The DDL
/// reconstruction never returns nothing for a missing routine — it synthesizes a well-formed stub with a
/// <c>/* source not available */</c> body — so a dropped object simply produces a definition that differs
/// from the baseline and lands in <see cref="ChangedInDatabase"/>, which refuses for the right reason
/// anyway. Detecting it separately would cost an extra catalog round trip on every compile to make one
/// refusal message more specific, and would add a state that could silently stop being produced.</para>
/// <para><b>What this type does NOT do.</b> It renders a verdict; it never writes, never reads, and never
/// decides policy. Refusing on <see cref="ObjectChangeVerdict.Unverifiable"/> versus proceeding is the
/// caller's call — though rule #11 ("uncertainty ⇒ do nothing or ask") leaves only one honest answer, and
/// every caller today refuses. A deliberate force-overwrite would also live at the call site, as a
/// separate decision the user makes with the conflict in front of them; nothing here needs to change for
/// that to be added.</para>
/// </summary>
public static class ObjectChangeSafety
{
    /// <summary>
    /// A witness for "this is what the database held when we loaded it" — the SHA-256 of the definition,
    /// lowercase hex. Returns null for a null/blank definition, which is how "we never read it" stays
    /// representable.
    /// <para>A hash rather than the text itself, for two reasons. It keeps the retained state a fixed 32
    /// bytes per open tab; and, more importantly, it is <i>structurally incapable of being mistaken for
    /// content</i> — a later change cannot accidentally fall back to the baseline as though it were source
    /// code, which is the class of mistake rule #11 exists to prevent.</para>
    /// </summary>
    public static string? Fingerprint(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition)) return null;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(definition));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Decides whether overwriting an EXISTING object is safe: is the definition in the database still the
    /// one this editor loaded?
    /// </summary>
    /// <param name="baselineFingerprint">
    /// <see cref="Fingerprint"/> of the definition the editor loaded, or null when none was captured
    /// (the load failed). Null yields <see cref="ObjectChangeVerdict.Unverifiable"/>: reporting
    /// <see cref="ObjectChangeVerdict.Safe"/> would defeat the whole mechanism on exactly the path where
    /// the load already went wrong.
    /// </param>
    /// <param name="currentDefinition">
    /// The definition the database holds now. Must come from the SAME reconstruction that produced the
    /// baseline, or the comparison is meaningless. Null/blank means the read produced nothing usable,
    /// which is unverifiable rather than a conflict.
    /// </param>
    public static ObjectChangeVerdict EvaluateOverwrite(string? baselineFingerprint, string? currentDefinition)
    {
        if (baselineFingerprint is null) return ObjectChangeVerdict.Unverifiable;

        var current = Fingerprint(currentDefinition);
        if (current is null) return ObjectChangeVerdict.Unverifiable;

        return string.Equals(baselineFingerprint, current, StringComparison.Ordinal)
            ? ObjectChangeVerdict.Safe
            : ObjectChangeVerdict.ChangedInDatabase;
    }

    /// <summary>
    /// Decides whether creating a NEW object is safe: is its chosen name free?
    /// <para>Takes the answer rather than a definition, because existence cannot be read off the
    /// reconstruction (see the type remarks) — the caller must establish it against the catalog.</para>
    /// </summary>
    /// <param name="nameIsTaken">
    /// True when an object of this kind already answers to the chosen name. Null when the caller could not
    /// find out, which must not be read as "free".
    /// </param>
    public static ObjectChangeVerdict EvaluateCreate(bool? nameIsTaken) => nameIsTaken switch
    {
        false => ObjectChangeVerdict.Safe,
        true => ObjectChangeVerdict.AlreadyExists,
        null => ObjectChangeVerdict.Unverifiable,
    };
}
