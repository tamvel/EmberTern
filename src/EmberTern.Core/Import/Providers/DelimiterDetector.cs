using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EmberTern.Core.Import.Providers;

/// <summary>
/// A delimiter proposal together with the evidence for it.
/// <para>
/// The evidence is not decoration. §0.4 lets auto-detection PROPOSE but never decide silently, and the UI shows
/// the basis („wykryto »;« — 240/240 wierszy ma tę samą liczbę kolumn"). Structured numbers, not a sentence:
/// Core holds no UI strings (rule #6).
/// </para>
/// </summary>
/// <param name="Delimiter">The proposed separator.</param>
/// <param name="FieldCount">The field count it produces on the majority of sampled records.</param>
/// <param name="ConsistentRecords">How many sampled records yielded exactly <paramref name="FieldCount"/> fields.</param>
/// <param name="SampledRecords">How many records were examined.</param>
public sealed record DelimiterProposal(char Delimiter, int FieldCount, int ConsistentRecords, int SampledRecords)
{
    /// <summary>Share of sampled records that agree on the field count, 0…1.</summary>
    public double Consistency => SampledRecords == 0 ? 0 : (double)ConsistentRecords / SampledRecords;

    /// <summary>True when every sampled record agreed — the case the UI can present without hedging.</summary>
    public bool IsUnanimous => SampledRecords > 0 && ConsistentRecords == SampledRecords;
}

/// <summary>
/// Proposes the field separator of a delimited source by measuring which candidate produces the most
/// self-consistent record shape over a sample.
/// <para>
/// The method is deliberately dumb and explainable: for each candidate, parse the sample with the REAL reader
/// (so quoting is handled exactly as it will be at import time), find the most common field count, and score by
/// how many records agree. A candidate that yields one field everywhere scores nothing — it simply is not a
/// separator for this file. Ties break toward MORE fields, because a file that parses consistently into 4
/// columns under one candidate and 2 under another is almost always the 4-column one (the 2-column reading is
/// usually a sub-separator inside a value).
/// </para>
/// </summary>
public static class DelimiterDetector
{
    /// <summary>Candidates, in the order they are tried. `;` leads because that is what Excel writes in a PL
    /// locale, which is the source this module was designed against.</summary>
    public static readonly IReadOnlyList<char> Candidates = new[] { ';', ',', '\t', '|' };

    /// <summary>Records examined when proposing. Enough to be convincing, small enough to be instant.</summary>
    public const int SampleRecords = 50;

    /// <summary>
    /// Proposes a delimiter for <paramref name="text"/>, or <c>null</c> when no candidate produces more than one
    /// field (a single-column file — there is nothing to detect, and inventing a separator would be a guess).
    /// </summary>
    public static DelimiterProposal? Propose(string text, DelimitedOptions options)
    {
        if (string.IsNullOrEmpty(text)) return null;

        DelimiterProposal? best = null;
        foreach (var candidate in Candidates)
        {
            var proposal = Measure(text, candidate, options);
            if (proposal is null) continue;
            if (best is null || IsBetter(proposal, best)) best = proposal;
        }
        return best;
    }

    private static bool IsBetter(DelimiterProposal candidate, DelimiterProposal incumbent)
    {
        // Consistency first — a separator the file agrees on beats one that merely splits into more pieces.
        const double epsilon = 0.0001;
        if (candidate.Consistency > incumbent.Consistency + epsilon) return true;
        if (candidate.Consistency < incumbent.Consistency - epsilon) return false;
        return candidate.FieldCount > incumbent.FieldCount;
    }

    private static DelimiterProposal? Measure(string text, char candidate, DelimitedOptions options)
    {
        // Parse with the real reader so quoted delimiters and embedded line breaks behave exactly as they will
        // at import time. Trimming is irrelevant to counting, so it is left as configured.
        var reader = new DelimitedTextReader(options with { Delimiter = candidate, AutoDetectDelimiter = false });
        using var text0 = new StringReader(text);
        var sample = reader.ReadSample(text0, SampleRecords);
        if (sample.Count == 0) return null;

        var counts = new Dictionary<int, int>();
        foreach (var record in sample)
        {
            counts[record.Fields.Length] = counts.GetValueOrDefault(record.Fields.Length) + 1;
        }

        var modal = counts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First();
        // One field means "this character does not separate anything in this file".
        return modal.Key < 2 ? null : new DelimiterProposal(candidate, modal.Key, modal.Value, sample.Count);
    }
}
