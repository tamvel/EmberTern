namespace EmberTern.Core.Performance;

/// <summary>An unparsed plan string exactly as the driver/engine returned it, plus which
/// dialect it is. Produced by the Firebird layer, consumed by <c>PlanParser</c> — the
/// boundary DTO that keeps FbCommand out of Core.</summary>
public sealed record RawPlanCapture(PlanDialect Dialect, string PlanText);
