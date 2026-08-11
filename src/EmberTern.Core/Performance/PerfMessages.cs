using EmberTern.Core.Localization;

namespace EmberTern.Core.Performance;

/// <summary>
/// The message keys the Performance advisor produces — etap <b>C7</b>, decision <b>D‑3</b>'s seventh producer
/// in Core.
///
/// <para>⭐ <b>The key is the contract; the words are App's.</b> The English text lives in
/// <c>App/Localization/Strings.resx</c> under exactly these names, and
/// <c>EveryCoreMessageKey_HasAnEnglishEntry</c> binds the two — it arms itself off these very fields, so a key
/// added here without an entry fails the build.</para>
///
/// <para>⭐⭐ <b>Keyed by <see cref="FindingKind"/>, never by <c>RuleId</c>.</b> The kind is the semantic
/// identity of a finding — it is what <c>FindingGuidanceCatalog</c> and <c>RecommendationCatalog</c> already
/// switch on — while <c>"R1"</c>…<c>"R6"</c> are rule identifiers that may be renumbered. Keying on the kind
/// keeps a whole family (title · explanation · guidance · recommendation) adjacent in the catalog, which is
/// what a translator needs in order to keep one finding's voice consistent.</para>
///
/// <para>⛔⛔ <b>THE ARGUMENT RULE, and it governs every key in this file: if a sentence has a dominant COUNT,
/// that count is argument <c>{0}</c>.</b> This is ratified convention <b>R3</b> from etap C6 —
/// <see cref="LocalizableMessage.TryGetCount"/> reads <c>Arguments[0]</c> and nothing else, so a sentence
/// whose count sits anywhere else can never be given plural forms by a language that needs them.
/// ⭐ The English word order does <i>not</i> change because of this: a format string references its arguments
/// out of order (<c>"Table {1} … {0} rows read"</c>) and renders byte-identically. ⚠ Where a sentence carries
/// two independently-inflecting counts (R1's explanation reads N rows to return M), only the dominant one can
/// select a category — and that is precisely why the key covers the WHOLE SENTENCE: the translator can
/// rephrase the second quantity so it needs no inflection. A fragment key would take that freedom away.</para>
///
/// <para>⚠ <b>Which sentences declare plural families in ENGLISH, and why so few.</b> Only four: R5's verb
/// agreement (<c>has</c>/<c>have</c>) and R6's <c>sub-quer{y|ies}</c>, each in two variants. Every other
/// counted sentence stays FLAT in English — not because it cannot inflect, but because English does not
/// inflect it. <c>Loc.Format</c> probes for variants <i>in the rendered culture's own catalog</i>, so Polish
/// may declare <c>.one</c>/<c>.few</c>/<c>.many</c> for any key here with no change to this file, to the
/// producers, or to any code. That is the C6 mechanism working as designed (ratified R4: whether a sentence
/// needs plural forms is a property of the LANGUAGE, and Core must not assert grammar it cannot know).</para>
///
/// <para>⛔ <b>Two keys may carry the same English value on purpose.</b>
/// <c>Evidence.ReadAmplification.Table</c>/<c>.Statement</c> and <c>Evidence.RowsRead.Table</c>/<c>.Statement</c>
/// read identically in English and are different measurements — one scoped to the finding's table, one to the
/// whole statement. Merging them would be deduplication by spelling, which the App stage ratified against
/// (188 App values keep several keys for the same reason). ⛔ Do not fold them together.</para>
/// </summary>
public static class PerfMessages
{
    // ══ Titles ═══════════════════════════════════════════════════════════════════════════════════════════
    // ⚠ A title that carries a count takes it as {0} and names its table as {1} (the argument rule above).

    /// <summary><c>{0}</c> sequential reads (count) · <c>{1}</c> table.</summary>
    public static readonly MessageKey CostlyFullScanTitle = new("Perf.CostlyFullScan.Title");

    /// <summary><c>{0}</c> table · <c>{1}</c> column.</summary>
    public static readonly MessageKey MissingIndexTitle = new("Perf.MissingIndex.Title");

    /// <summary><c>{0}</c> table · <c>{1}</c> column · <c>{2}</c> index.</summary>
    public static readonly MessageKey NonSargableTitle = new("Perf.NonSargable.Title");

    /// <summary><c>{0}</c> index · <c>{1}</c> table.</summary>
    public static readonly MessageKey LowSelectivityTitle = new("Perf.LowSelectivity.Title");

    /// <summary><c>{0}</c> table.</summary>
    public static readonly MessageKey StaleStatisticsTitle = new("Perf.StaleStatistics.Title");

    // ⭐ D‑3: the statement's output verb is no longer a WORD Core picks and conjugates — it is which KEY the
    // rule selects. `PerformanceContext.OutputVerb` is gone; substituting "return"/"change" (and gluing an
    // English "s" onto it) worked in English and cannot work in a language that inflects.

    /// <summary>Result-producing statement. <c>{0}</c> amplification (pre-formatted).</summary>
    public static readonly MessageKey HighAmplificationTitleSelect = new("Perf.HighAmplification.Title.Select");

    /// <summary>DML / procedure. <c>{0}</c> amplification (pre-formatted).</summary>
    public static readonly MessageKey HighAmplificationTitleChange = new("Perf.HighAmplification.Title.Change");

    // ══ Explanations ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary><c>{0}</c> sequential reads (count) · <c>{1}</c> output rows.</summary>
    public static readonly MessageKey CostlyFullScanExplanationSelect =
        new("Perf.CostlyFullScan.Explanation.Select");

    /// <inheritdoc cref="CostlyFullScanExplanationSelect"/>
    public static readonly MessageKey CostlyFullScanExplanationChange =
        new("Perf.CostlyFullScan.Explanation.Change");

    /// <summary>
    /// <c>{0}</c> sequential reads (count) · <c>{1}</c> table · <c>{2}</c> output rows · <c>{3}</c> column ·
    /// <c>{4}</c> condition.
    /// </summary>
    public static readonly MessageKey MissingIndexExplanationSelect =
        new("Perf.MissingIndex.Explanation.Select");

    /// <inheritdoc cref="MissingIndexExplanationSelect"/>
    public static readonly MessageKey MissingIndexExplanationChange =
        new("Perf.MissingIndex.Explanation.Change");

    // ⭐ The sargability issue used to be a CLAUSE substituted into the front of the sentence. Two whole-
    // sentence keys instead, selected by the closed `SargabilityIssue` enum — the ratified enum→key shape.

    /// <summary>
    /// <c>{0}</c> sequential reads (count) · <c>{1}</c> index · <c>{2}</c> column · <c>{3}</c> condition ·
    /// <c>{4}</c> table.
    /// </summary>
    public static readonly MessageKey NonSargableExplanationFunctionOnColumn =
        new("Perf.NonSargable.Explanation.FunctionOnColumn");

    /// <inheritdoc cref="NonSargableExplanationFunctionOnColumn"/>
    public static readonly MessageKey NonSargableExplanationLeadingWildcardLike =
        new("Perf.NonSargable.Explanation.LeadingWildcardLike");

    /// <summary>
    /// <c>{0}</c> index reads (count) · <c>{1}</c> table · <c>{2}</c> index · <c>{3}</c> selectivity
    /// (pre-formatted) · <c>{4}</c> output rows.
    /// </summary>
    public static readonly MessageKey LowSelectivityExplanationSelect =
        new("Perf.LowSelectivity.Explanation.Select");

    /// <inheritdoc cref="LowSelectivityExplanationSelect"/>
    public static readonly MessageKey LowSelectivityExplanationChange =
        new("Perf.LowSelectivity.Explanation.Change");

    /// <summary>
    /// 🔢 <b>Plural family</b> — the count chooses <c>has</c>/<c>have</c>, which is VERB agreement rather than
    /// a noun's plural. The same "count → category → sentence variant" mechanism serves both, which is why C6
    /// scoped it to <i>choosing a variant</i> and not to <i>pluralizing a noun</i>.
    /// <para><c>{0}</c> number of indexes without statistics (count) · <c>{1}</c> index names · <c>{2}</c> table.</para>
    /// </summary>
    public static readonly MessageKey StaleStatisticsExplanation = new("Perf.StaleStatistics.Explanation");

    /// <summary>
    /// 🔢 <b>Plural family.</b> As <see cref="StaleStatisticsExplanation"/>, for the case where the table was
    /// also read sequentially. ⭐ A separate key rather than a clause glued onto the end: a fixed tail welded
    /// to a sentence cannot be translated into a language that inflects.
    /// </summary>
    public static readonly MessageKey StaleStatisticsExplanationCorroborated =
        new("Perf.StaleStatistics.Explanation.Corroborated");

    /// <summary><c>{0}</c> rows read (count) · <c>{1}</c> output rows · <c>{2}</c> amplification (pre-formatted).</summary>
    public static readonly MessageKey HighAmplificationExplanationSelect =
        new("Perf.HighAmplification.Explanation.Select");

    /// <inheritdoc cref="HighAmplificationExplanationSelect"/>
    public static readonly MessageKey HighAmplificationExplanationChange =
        new("Perf.HighAmplification.Explanation.Change");

    /// <summary>
    /// 🔢 <b>Plural family.</b> ⚠ Here the count at <c>{0}</c> is the SUB-QUERY count, not the rows read —
    /// because it is the sub-query noun that inflects. The argument rule picks the count the sentence's
    /// grammar depends on, not the largest number in it.
    /// <para><c>{0}</c> sub-queries (count) · <c>{1}</c> rows read · <c>{2}</c> output rows · <c>{3}</c>
    /// amplification (pre-formatted).</para>
    /// </summary>
    public static readonly MessageKey HighAmplificationExplanationSelectWithSubqueries =
        new("Perf.HighAmplification.Explanation.Select.WithSubqueries");

    /// <inheritdoc cref="HighAmplificationExplanationSelectWithSubqueries"/>
    public static readonly MessageKey HighAmplificationExplanationChangeWithSubqueries =
        new("Perf.HighAmplification.Explanation.Change.WithSubqueries");

    // ══ Evidence labels ══════════════════════════════════════════════════════════════════════════════════
    // ⭐ A label is EmberTern's word and is keyed; the VALUE beside it stays a verbatim string, because it is
    // a measured number, an index name or a SQL condition — data, in the C2 (Quick Info) split.

    /// <summary>Merged: the same measurement in R1, R2 and R3 — this table's sequential reads.</summary>
    public static readonly MessageKey EvidenceSequentialReads = new("Perf.Evidence.SequentialReads");

    /// <summary>Merged: the same measurement in R1 and R4 — this table's index reads.</summary>
    public static readonly MessageKey EvidenceIndexReads = new("Perf.Evidence.IndexReads");

    /// <summary>Merged: the same catalog figure in R1 and R2.</summary>
    public static readonly MessageKey EvidenceApproxRowsInTable = new("Perf.Evidence.ApproxRowsInTable");

    /// <summary>⛔ NOT merged with <see cref="EvidenceReadAmplificationStatement"/> — this one is scoped to the
    /// finding's TABLE (its sequential reads ÷ the output).</summary>
    public static readonly MessageKey EvidenceReadAmplificationTable =
        new("Perf.Evidence.ReadAmplification.Table");

    /// <summary>⛔ NOT merged with <see cref="EvidenceReadAmplificationTable"/> — this one is scoped to the whole
    /// STATEMENT (every row it read ÷ the output). Same English words, different measurement.</summary>
    public static readonly MessageKey EvidenceReadAmplificationStatement =
        new("Perf.Evidence.ReadAmplification.Statement");

    /// <summary>⛔ NOT merged with <see cref="EvidenceRowsReadStatement"/> — this one is this TABLE's total reads.</summary>
    public static readonly MessageKey EvidenceRowsReadTable = new("Perf.Evidence.RowsRead.Table");

    /// <summary>⛔ NOT merged with <see cref="EvidenceRowsReadTable"/> — this one is the whole STATEMENT's reads.</summary>
    public static readonly MessageKey EvidenceRowsReadStatement = new("Perf.Evidence.RowsRead.Statement");

    public static readonly MessageKey EvidencePercentOfTableScanned = new("Perf.Evidence.PercentOfTableScanned");

    public static readonly MessageKey EvidenceSubqueries = new("Perf.Evidence.Subqueries");

    public static readonly MessageKey EvidenceIndexAmplification = new("Perf.Evidence.IndexAmplification");

    public static readonly MessageKey EvidenceIndexSelectivity = new("Perf.Evidence.IndexSelectivity");

    public static readonly MessageKey EvidenceFilter = new("Perf.Evidence.Filter");

    public static readonly MessageKey EvidenceCondition = new("Perf.Evidence.Condition");

    public static readonly MessageKey EvidenceExistingIndex = new("Perf.Evidence.ExistingIndex");

    public static readonly MessageKey EvidenceIndexesWithoutStatistics =
        new("Perf.Evidence.IndexesWithoutStatistics");

    /// <summary>Replaces <c>PerformanceContext.OutputRowsLabel</c>'s result-set half.</summary>
    public static readonly MessageKey EvidenceRowsReturned = new("Perf.Evidence.RowsReturned");

    /// <summary>Replaces <c>PerformanceContext.OutputRowsLabel</c>'s DML half.</summary>
    public static readonly MessageKey EvidenceRowsChanged = new("Perf.Evidence.RowsChanged");

    // ══ Investigation guidance ═══════════════════════════════════════════════════════════════════════════
    // ⚠ Numbered rather than named: these are the items of an ordered list, and the order is part of what the
    // author wrote. A name per bullet would invite reordering them one at a time.

    public static readonly MessageKey GuidanceHeading = new("Perf.Guidance.Heading");

    public static readonly MessageKey CostlyFullScanGuidance1 = new("Perf.CostlyFullScan.Guidance.1");
    public static readonly MessageKey CostlyFullScanGuidance2 = new("Perf.CostlyFullScan.Guidance.2");
    public static readonly MessageKey CostlyFullScanGuidance3 = new("Perf.CostlyFullScan.Guidance.3");

    public static readonly MessageKey MissingIndexGuidance1 = new("Perf.MissingIndex.Guidance.1");
    public static readonly MessageKey MissingIndexGuidance2 = new("Perf.MissingIndex.Guidance.2");
    public static readonly MessageKey MissingIndexGuidance3 = new("Perf.MissingIndex.Guidance.3");

    public static readonly MessageKey NonSargableGuidance1 = new("Perf.NonSargable.Guidance.1");
    public static readonly MessageKey NonSargableGuidance2 = new("Perf.NonSargable.Guidance.2");
    public static readonly MessageKey NonSargableGuidance3 = new("Perf.NonSargable.Guidance.3");

    public static readonly MessageKey LowSelectivityGuidance1 = new("Perf.LowSelectivity.Guidance.1");
    public static readonly MessageKey LowSelectivityGuidance2 = new("Perf.LowSelectivity.Guidance.2");
    public static readonly MessageKey LowSelectivityGuidance3 = new("Perf.LowSelectivity.Guidance.3");

    public static readonly MessageKey StaleStatisticsGuidance1 = new("Perf.StaleStatistics.Guidance.1");
    public static readonly MessageKey StaleStatisticsGuidance2 = new("Perf.StaleStatistics.Guidance.2");

    public static readonly MessageKey HighAmplificationGuidance1 = new("Perf.HighAmplification.Guidance.1");
    public static readonly MessageKey HighAmplificationGuidance2 = new("Perf.HighAmplification.Guidance.2");
    public static readonly MessageKey HighAmplificationGuidance3 = new("Perf.HighAmplification.Guidance.3");

    // ══ Recommendations ══════════════════════════════════════════════════════════════════════════════════

    public static readonly MessageKey RecommendationHeading = new("Perf.Recommendation.Heading");

    public static readonly MessageKey CostlyFullScanRecommendation = new("Perf.CostlyFullScan.Recommendation");

    /// <summary>
    /// The column is known. <c>{0}</c> column name.
    /// <para>⭐ Split from <see cref="MissingIndexRecommendationOnFilteredColumn"/> in C7 (ratified D‑6). The
    /// producer used to substitute EmberTern's own NOUN — <c>"the filtered column"</c> — into the sentence
    /// when it had no column name. That works in English and breaks in a language that inflects, because an
    /// argument cannot know which case the sentence needs. Same ratified shape as C3's
    /// <c>UnsupportedServerUnknownVersion</c> and C5's two <c>ET0008</c> keys.</para>
    /// </summary>
    public static readonly MessageKey MissingIndexRecommendationOnColumn =
        new("Perf.MissingIndex.Recommendation.OnColumn");

    /// <summary>The column is not known — a whole sentence, not a noun substituted into one.</summary>
    public static readonly MessageKey MissingIndexRecommendationOnFilteredColumn =
        new("Perf.MissingIndex.Recommendation.OnFilteredColumn");

    public static readonly MessageKey NonSargableRecommendation = new("Perf.NonSargable.Recommendation");

    public static readonly MessageKey LowSelectivityRecommendation = new("Perf.LowSelectivity.Recommendation");

    public static readonly MessageKey StaleStatisticsRecommendation = new("Perf.StaleStatistics.Recommendation");

    public static readonly MessageKey HighAmplificationRecommendation =
        new("Perf.HighAmplification.Recommendation");
}
