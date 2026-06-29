namespace EmberTern.Core.Metadata;

/// <summary>
/// A Firebird generator (sequence) as surfaced in the Generator Detail editor.
/// Plain init-only POCO, zero Avalonia deps. <see cref="CurrentValue"/> is the
/// live value in the database (read via <c>GEN_ID(name, 0)</c>);
/// <see cref="InitialValue"/> / <see cref="Increment"/> come from the sequence
/// definition (RDB$GENERATORS — FB3+; default 0 / 1 when the catalog doesn't
/// expose them, e.g. FB 2.5). Do not conflate CurrentValue with InitialValue.
/// </summary>
public sealed class GeneratorInfo
{
    public string Name { get; init; } = string.Empty;
    public long CurrentValue { get; init; }
    public long InitialValue { get; init; }
    public long Increment { get; init; } = 1;
    public string Description { get; init; } = string.Empty;
}
