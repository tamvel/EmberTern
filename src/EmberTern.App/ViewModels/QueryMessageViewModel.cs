using System;
using EmberTern.App.Controls;
using EmberTern.Core.Formatting;

namespace EmberTern.App.ViewModels;

public sealed class QueryMessageViewModel
{
    public QueryMessageViewModel(MessageSeverity severity, string text)
        : this(DateTimeOffset.Now, severity, text)
    {
    }

    public QueryMessageViewModel(DateTimeOffset timestamp, MessageSeverity severity, string text)
    {
        Timestamp = timestamp;
        Severity = severity;
        Text = text;
    }

    public DateTimeOffset Timestamp { get; }

    /// <summary>The SHARED <see cref="MessageSeverity"/> the whole IDE uses (the one
    /// <see cref="MessageBanner"/> renders). The Messages log stays a log, but a message in it means the
    /// same thing — and reads in the same colour — as the same message on any other surface.</summary>
    public MessageSeverity Severity { get; }

    public string Text { get; }

    public string TimestampLabel => DateTimeDisplay.LogTime(Timestamp.LocalDateTime);

    public bool IsError => Severity == MessageSeverity.Error;
    public bool IsWarning => Severity == MessageSeverity.Warning;
    public bool IsInfo => Severity == MessageSeverity.Info;

    /// <summary>Theme brush key for this message's severity, resolved by <see cref="IconBrushConverter"/> —
    /// the SAME mapping <see cref="MessageBanner"/> paints with, so a log row and a banner carrying the
    /// same message can never drift apart.</summary>
    public string SeverityBrushKey => MessageBanner.BrushKeyFor(Severity);

    /// <summary>Only a problem row earns the severity stripe; an ordinary Info line stays a plain, quiet log
    /// entry — the log must not become a wall of markers. (No severity icon in the log: an icon column
    /// widens only the rows that have one, breaking the timestamp alignment a log is read by.)</summary>
    public bool ShowSeverityMarker => Severity is MessageSeverity.Warning or MessageSeverity.Error;

    /// <summary>Brush key for the message TEXT: a problem carries its severity colour in full (matching the
    /// banner), while an ordinary Info line keeps the normal reading colour — a log is mostly Info, and
    /// greying all of it would cost legibility for no signal.</summary>
    public string MessageBrushKey => ShowSeverityMarker ? SeverityBrushKey : "ForegroundBrush";
}
