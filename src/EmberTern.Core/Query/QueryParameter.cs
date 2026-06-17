namespace EmberTern.Core.Query;

/// <summary>
/// A named value bound to a parameterized command — used by Execute Procedure so
/// input values are bound, never embedded as SQL literals. <see cref="Value"/> is
/// a plain CLR value (or null for SQL NULL); the executor maps it to an
/// <c>FbParameter</c>. Core holds no Firebird types, so the value is untyped here.
/// </summary>
public sealed record QueryParameter(string Name, object? Value);
