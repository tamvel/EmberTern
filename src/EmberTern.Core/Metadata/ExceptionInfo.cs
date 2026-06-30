namespace EmberTern.Core.Metadata;

/// <summary>
/// A Firebird custom EXCEPTION as surfaced in the Exception Detail editor. Plain
/// init-only POCO, zero Avalonia deps. An exception has no PSQL body and no
/// parameters — just a name, a <see cref="Message"/> (RDB$MESSAGE, raised text)
/// and a <see cref="Description"/> (RDB$DESCRIPTION, COMMENT ON EXCEPTION).
/// </summary>
public sealed class ExceptionInfo
{
    public string Name { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
