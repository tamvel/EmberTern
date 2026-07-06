using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;

namespace EmberTern.Firebird;

/// <summary>
/// The single authority for a <b>portable object script</b>: the complete DDL of ONE schema
/// object — structure plus its <c>COMMENT ON</c> statements — with deliberately NO
/// <c>GRANT</c>/<c>REVOKE</c>/role/user (security) DDL, so a script exported from one database
/// applies cleanly to another with different users.
/// <para>
/// Composes the existing readers: structure from <see cref="FirebirdDdlReader"/> for the
/// reconstruction kinds (Domain and Index are only stubbed there, so they build from
/// <see cref="DdlGenerator"/> + <see cref="FirebirdTableDetailReader"/> info), and descriptions
/// from <see cref="FirebirdTableDetailReader"/>. Comment layout/selection is the pure
/// <see cref="PortableDdl"/>. Reader calls are sequential (never concurrent), so each
/// completes its own command-lock cycle before the next — no lane contention.
/// </para>
/// </summary>
public sealed class MetadataExportService
{
    private readonly FirebirdDdlReader _ddlReader;
    private readonly FirebirdTableDetailReader _detailReader;

    public MetadataExportService(FirebirdDdlReader ddlReader, FirebirdTableDetailReader detailReader)
    {
        _ddlReader = ddlReader;
        _detailReader = detailReader;
    }

    /// <summary>
    /// Builds the complete portable DDL script for <paramref name="obj"/>. Wraps
    /// <see cref="FirebirdDdlReader"/> — same lane/lock/transaction discipline; a
    /// <c>MetadataReadException</c> from a reader propagates to the caller.
    /// </summary>
    public async Task<string> BuildObjectScriptAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        switch (obj.Kind)
        {
            // FetchDdlAsync only stubs Domain — build the real CREATE DOMAIN from catalog
            // info, then append its comment uniformly.
            case MetadataObjectKind.Domain:
            {
                var info = await _detailReader.GetDomainInfoAsync(obj.Name, cancellationToken).ConfigureAwait(false);
                var structure = DdlGenerator.BuildCreateDomain(info);
                return PortableDdl.Compose(structure, new[] { PortableDdl.ObjectComment(obj.Kind, obj.Name, info.Description) });
            }

            // FetchDdlAsync only stubs Index — BuildIndexDdl already bakes the COMMENT ON
            // INDEX only-when-present, so no separate append is needed.
            case MetadataObjectKind.Index:
            {
                var info = await _detailReader.GetIndexDetailAsync(obj.Name, cancellationToken).ConfigureAwait(false);
                return info is null ? string.Empty : PortableDdl.Compose(DdlGenerator.BuildIndexDdl(info));
            }

            // Tables carry both a table comment and per-column comments.
            case MetadataObjectKind.Table:
            case MetadataObjectKind.SystemTable:
            {
                var structure = await _ddlReader.FetchDdlAsync(obj, cancellationToken).ConfigureAwait(false);
                var tableComment = await _detailReader.GetDescriptionAsync(obj.Name, cancellationToken).ConfigureAwait(false);
                var fields = await _detailReader.GetFieldsAsync(obj.Name, cancellationToken).ConfigureAwait(false);

                var trailing = new List<string?> { PortableDdl.ObjectComment(obj.Kind, obj.Name, tableComment) };
                foreach (var field in fields)
                {
                    if (!string.IsNullOrWhiteSpace(field.Description))
                        trailing.Add(DdlGenerator.BuildCommentColumn(obj.Name, field.Name, field.Description));
                }
                return PortableDdl.Compose(structure, trailing);
            }

            // View / Procedure / Trigger / Function / Package / Generator / Exception:
            // structure from FetchDdlAsync (Exception's message is part of the CREATE),
            // then an object-level COMMENT ON.
            default:
            {
                var structure = await _ddlReader.FetchDdlAsync(obj, cancellationToken).ConfigureAwait(false);
                var description = await FetchObjectDescriptionAsync(obj, cancellationToken).ConfigureAwait(false);
                return PortableDdl.Compose(structure, new[] { PortableDdl.ObjectComment(obj.Kind, obj.Name, description) });
            }
        }
    }

    private async Task<string?> FetchObjectDescriptionAsync(MetadataObject obj, CancellationToken cancellationToken) => obj.Kind switch
    {
        MetadataObjectKind.View => await _detailReader.GetDescriptionAsync(obj.Name, cancellationToken).ConfigureAwait(false),
        MetadataObjectKind.Procedure => await _detailReader.GetProcedureDescriptionAsync(obj.Name, cancellationToken).ConfigureAwait(false),
        MetadataObjectKind.Function => await _detailReader.GetFunctionDescriptionAsync(obj.Name, cancellationToken).ConfigureAwait(false),
        MetadataObjectKind.Trigger => await _detailReader.GetTriggerDescriptionAsync(obj.Name, cancellationToken).ConfigureAwait(false),
        MetadataObjectKind.Package => await _detailReader.GetPackageDescriptionAsync(obj.Name, cancellationToken).ConfigureAwait(false),
        MetadataObjectKind.Generator => (await _detailReader.GetGeneratorInfoAsync(obj.Name, cancellationToken).ConfigureAwait(false)).Description,
        MetadataObjectKind.Exception => (await _detailReader.GetExceptionInfoAsync(obj.Name, cancellationToken).ConfigureAwait(false)).Description,
        _ => null,
    };
}
