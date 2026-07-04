namespace EmberTern.Core.Diagnostics;

/// <summary>
/// One database attachment — a <c>MON$ATTACHMENTS</c> row enriched with its currently
/// active statement (<c>MON$STATEMENTS</c>) and snapshot record totals
/// (<c>MON$RECORD_STATS</c>). Raw facts only; <see cref="SessionHealthAnalyzer"/> derives
/// risk / load / findings from these. Pure — zero Avalonia, zero <c>Fb*</c>.
/// </summary>
public sealed record SessionInfo
{
    public required long AttachmentId { get; init; }

    public string User { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    /// <summary><c>MON$REMOTE_PROCESS</c> — the client executable path/name.</summary>
    public string Application { get; init; } = string.Empty;

    /// <summary><c>MON$REMOTE_ADDRESS</c> — <c>ip:port</c> or host of the client.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary><c>MON$REMOTE_PID</c> — the client-side process id, when reported.</summary>
    public int? RemotePid { get; init; }

    public string Protocol { get; init; } = string.Empty;

    public string CharacterSet { get; init; } = string.Empty;

    /// <summary><c>MON$STATE</c>: 0 = idle, 1 = active.</summary>
    public int StateCode { get; init; }

    public bool IsActive => StateCode == 1;

    /// <summary><c>MON$TIMESTAMP</c> — when the attachment connected (server local time).</summary>
    public DateTime? ConnectedAt { get; init; }

    /// <summary><c>MON$GARBAGE_COLLECTION</c> — whether this attachment cooperates in GC.</summary>
    public bool GarbageCollectionAllowed { get; init; } = true;

    /// <summary><c>MON$STATEMENTS.MON$SQL_TEXT</c> of its currently active statement, if any.</summary>
    public string CurrentStatement { get; init; } = string.Empty;

    /// <summary>Snapshot total record reads (sequential + indexed) for the attachment.</summary>
    public long RecordReads { get; init; }

    /// <summary>Snapshot total record writes (insert + update + delete) for the attachment.</summary>
    public long RecordWrites { get; init; }

    /// <summary>True for EmberTern's own attachments (the data + metadata lanes). Excluded
    /// from findings, heavy-user ranking, and counters — we never warn about our own tool.</summary>
    public bool IsSelf { get; init; }

    /// <summary>Single load proxy (not CPU — Firebird exposes no per-attachment CPU): the
    /// snapshot sum of record reads + writes. Used only for the heavy-user marker.</summary>
    public long Load => RecordReads + RecordWrites;

    /// <summary>The executable leaf of <see cref="Application"/> (no directory, no extension),
    /// e.g. <c>C:\Prestiz\PCbiznes.exe</c> → <c>PCbiznes</c>. Empty stays empty.</summary>
    public string ApplicationName
    {
        get
        {
            var app = Application;
            if (string.IsNullOrWhiteSpace(app))
            {
                return string.Empty;
            }

            var slash = app.LastIndexOfAny(new[] { '\\', '/' });
            var leaf = slash >= 0 ? app[(slash + 1)..] : app;
            var dot = leaf.LastIndexOf('.');
            return dot > 0 ? leaf[..dot] : leaf;
        }
    }
}
