using System;
using System.Collections.Generic;
using EmberTern.Core.Metadata;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>One coloured run of a plan node's text: the literal characters, plus the theme key the view
/// should paint them with.
///
/// <para>⚠ It carries a resource KEY, never a brush — the same convention as
/// <c>MetadataNodeViewModel.IconResourceKey</c>, and for the same reason (architecture rule #1: no Avalonia
/// types in a view model). The view resolves it through the existing <c>IconBrushConverter</c>.</para>
/// </summary>
/// <param name="Text">The literal source characters. Concatenating every segment's Text reproduces the
/// node's <see cref="PlanNode.RawText"/> exactly — see <see cref="PlanTextSegments"/>.</param>
/// <param name="BrushKey">Theme resource key for the run's foreground.</param>
public readonly record struct PlanTextSegment(string Text, string BrushKey);

/// <summary>
/// Splits a plan node's raw text into coloured runs, using the classification the plan parser ALREADY
/// produced. There is no second parser here and there must never be one.
///
/// <para>⭐⭐ THE POINT OF THIS CLASS IS THAT THE INFORMATION WAS ALWAYS THERE AND WAS BEING THROWN AWAY.
/// <see cref="PlanNode"/> carries <see cref="PlanNode.Method"/>, <see cref="PlanNode.TableName"/>,
/// <see cref="PlanNode.IndexName"/> and <see cref="PlanNode.Detail"/>, computed by
/// <c>PlanNodeDescriptor.Parse</c> for every node — and the view rendered <c>Node.RawText</c> in one flat
/// colour. Colouring the plan therefore needed no new grammar; it needed the view to stop flattening.</para>
///
/// <para>⛔ SQL syntax highlighting would be the WRONG tool and this is not a matter of taste: a Firebird
/// plan is not SQL. <c>Table</c>, <c>Bitmap</c>, <c>Access By ID</c> and <c>Range Scan</c> are not SQL
/// keywords, and an XSHD definition would colour incidental words while missing every name that matters.</para>
///
/// <para>⭐ THE COLOURS ARE THE OBJECT-KIND COLOURS THE REST OF THE APPLICATION ALREADY USES
/// (<c>MetadataNodeViewModel.ResourceKeyFor</c>), so a table in a plan is the same colour as that table in
/// the Metadata Explorer. That is the colour language's S1 axis doing exactly its job: the user learns a
/// kind's colour once and recognises it everywhere, rather than learning a palette that is local to one
/// collapsed panel.</para>
///
/// <para>⚠⚠ THE SPLIT IS TAKEN FROM THE RAW TEXT, NEVER RE-COMPOSED FROM THE PARSED FIELDS — and that is a
/// correctness decision, not a style one. Re-composing (<c>"Table " + name + " as " + alias</c>) would put
/// this class in the business of reproducing the engine's own wording, and any drift would silently show the
/// user a plan the server did not print. Splitting the original string makes "the rendered text equals the
/// raw text" true <b>by construction</b> — the same §0 discipline the SQL formatter applies to source.</para>
///
/// <para>⛔⛔ SCOPE, RATIFIED AFTER AN EXPERIMENT THAT WAS TRIED AND REJECTED (2026-08-09): the ONLY things
/// that carry colour are the OBJECT NAME (its kind colour) and a FULL SCAN (the whole row, as a warning).
/// Everything else — the verb, the alias, the qualifiers, the access method — is ordinary or recessed text.
/// A follow-up variant that also differentiated access methods and receded the <c>Table</c>/<c>Index</c>
/// verbs was built, rendered and <b>withdrawn</b>: the two neutral text levels are only <b>1,78:1</b> apart
/// in Dark, so the extra distinction was not reliably visible, and a third level would have needed a new
/// token. ⛔ Do not re-open it without new measurement.</para>
/// </summary>
public static class PlanTextSegments
{
    /// <summary>Theme key for the structural verb — "Table", "Sort", "Nested Loop Join". Ordinary reading
    /// colour: it is the sentence's grammar, not its payload.</summary>
    public const string KeywordBrushKey = "ForegroundBrush";

    /// <summary>Theme key for the qualifiers — "Access By ID", "Range Scan (full match)", "(record length:
    /// 860…)". Recessed, because they are read only after the name has been found.</summary>
    public const string DetailBrushKey = "SubtleForegroundBrush";

    /// <summary>Theme key for a full/sequential scan, which paints the WHOLE row — see <see cref="Build"/>.</summary>
    public const string SequentialScanBrushKey = "WarningBrush";

    public static IReadOnlyList<PlanTextSegment> Build(PlanNode node)
    {
        var raw = node.RawText ?? string.Empty;
        if (raw.Length == 0) return Array.Empty<PlanTextSegment>();

        // ⭐ A full scan keeps its whole row in the warning colour. It is the one node kind the reader is
        //   hunting for, so sub-dividing it into three quieter colours would make the most important row
        //   LESS visible — a change nobody asked for, smuggled in beside one they did.
        if (node.IsSequentialScan)
        {
            return [new PlanTextSegment(raw, SequentialScanBrushKey)];
        }

        var segments = new List<PlanTextSegment>(4);

        // ⚠ `Detail` is a SUFFIX of the raw text — `PlanNodeDescriptor` produces it by slicing the remainder
        //   and trimming. Deriving the boundary from that fact means no keyword table lives here; a second
        //   copy of the parser's word list is exactly the drift this class exists to avoid.
        //   The `EndsWith` test is the guard: if the invariant ever stops holding, the split is skipped and
        //   the node renders faithfully in one colour rather than being cut in the wrong place.
        var head = raw;
        var detail = string.Empty;
        if (!string.IsNullOrEmpty(node.Detail) && raw.EndsWith(node.Detail, StringComparison.Ordinal))
        {
            var start = raw.Length - node.Detail!.Length;
            head = raw[..start];
            detail = raw[start..];
        }

        AppendHead(segments, head, NameBrushKeyFor(node.Method));
        if (detail.Length > 0) segments.Add(new PlanTextSegment(detail, DetailBrushKey));

        return segments;
    }

    /// <summary>The head is "verb + quoted names". The FIRST quoted identifier is the object the node is
    /// about; a later one is an alias, which names nothing new and stays quiet.</summary>
    private static void AppendHead(List<PlanTextSegment> segments, string head, string nameBrushKey)
    {
        var index = 0;
        var nameTaken = false;

        while (index < head.Length)
        {
            var open = head.IndexOf('"', index);
            if (open < 0) break;

            var close = head.IndexOf('"', open + 1);
            if (close < 0) break;

            if (open > index) segments.Add(new PlanTextSegment(head[index..open], KeywordBrushKey));

            var quoted = head[open..(close + 1)];
            segments.Add(new PlanTextSegment(quoted, nameTaken ? DetailBrushKey : nameBrushKey));
            nameTaken = true;

            index = close + 1;
        }

        if (index < head.Length) segments.Add(new PlanTextSegment(head[index..], KeywordBrushKey));
    }

    /// <summary>The object-kind colour for the thing this node names — resolved through the SAME
    /// <c>ResourceKeyFor</c> the metadata tree uses, so the two cannot drift apart.
    /// ⚠ A node whose method names no object (Sort, Filter, Bitmap, a join) has no name to colour; it returns
    /// the ordinary reading colour, which is also what an unrecognised node gets.</summary>
    private static string NameBrushKeyFor(AccessMethod method) => method switch
    {
        AccessMethod.FullScan or AccessMethod.AccessById
            => MetadataNodeViewModel.ResourceKeyFor(MetadataObjectKind.Table),
        AccessMethod.IndexScan
            => MetadataNodeViewModel.ResourceKeyFor(MetadataObjectKind.Index),
        AccessMethod.ProcedureScan
            => MetadataNodeViewModel.ResourceKeyFor(MetadataObjectKind.Procedure),
        _ => KeywordBrushKey,
    };
}
