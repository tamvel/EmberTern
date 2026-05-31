using System;

namespace EmberTern.App.ViewModels;

public enum MessageSeverity
{
    Info,
    Warning,
    Error,
}

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
    public MessageSeverity Severity { get; }
    public string Text { get; }

    public string TimestampLabel => Timestamp.LocalDateTime.ToString("HH:mm:ss");

    public bool IsError => Severity == MessageSeverity.Error;
    public bool IsWarning => Severity == MessageSeverity.Warning;
    public bool IsInfo => Severity == MessageSeverity.Info;
}
