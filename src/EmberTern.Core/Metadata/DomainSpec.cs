namespace EmberTern.Core.Metadata;

/// <summary>
/// A user-defined domain plus its formatted SQL type (e.g. "VARCHAR(80)").
/// Surfaced by <c>FirebirdMetadataReader.ListDomainsAsync</c> so the
/// AddFieldDialog's Domain ComboBox can show the underlying type alongside
/// the domain name — saves the user from cross-referencing the catalog.
/// </summary>
public sealed record DomainSpec(string Name, string Type);
