using System;

namespace EmberTern.Core.Import;

/// <summary>
/// A stored import configuration plus the metadata that identifies it. The payload is
/// <see cref="ImportConfiguration"/> unchanged — a profile is not a second representation of an import, it is
/// the same record with a name on it (design §4.8.1).
/// <para>
/// A mutable class rather than a record, matching every other persisted entry in this codebase
/// (<c>DebugWatchEntry</c>, <c>ParameterHistoryEntry</c>, <c>GridProfile</c>): the store does read-modify-write
/// on the shared settings file, and the JSON round trip is the reason those types look the way they do.
/// </para>
/// <para>
/// <b>Metadata vs configuration</b> — the split matters. <see cref="ConnectionId"/> lives here, not in the
/// configuration, because "which database was I connected to" is not a decision about how to read a file. That
/// is also what would let a profile be shared between developers later: the configuration travels, the
/// metadata does not.
/// </para>
/// </summary>
public sealed class ImportProfile
{
    /// <summary>Stable identity, independent of the name.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>User-visible name, or <b>empty for the implicit "last used" profile</b> — the one the surface
    /// writes after every successful import and restores when it opens (§4.8.4). Keeping it in the same list as
    /// future named profiles is deliberate: it means the named-profile UI (etap I11) is a view over a store
    /// that has been exercised in production since the MVP, not a mechanism switched on late.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The connection this profile was made against, or <c>null</c> for a connection-independent one.</summary>
    public string? ConnectionId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The decisions themselves.</summary>
    public ImportConfiguration Configuration { get; set; } = ImportConfiguration.Empty;

    /// <summary>True for the implicit "last used" entry rather than a named profile.</summary>
    public bool IsImplicit => string.IsNullOrEmpty(Name);
}
