using System;
using System.Globalization;
using EmberTern.Core.Performance;
using EmberTern.Core.Diagnostics;

namespace EmberTern.App.ViewModels;

/// <summary>One horizontal bar in the Table Access chart: red = sequential (Natural) reads,
/// blue = index reads. Widths are normalized to the widest bar in the profile so the most-read
/// table fills the track — the scale context that separates a costly scan from a small one.</summary>
public sealed class TableAccessBarViewModel
{
    private const double TrackWidth = 160;

    public TableAccessBarViewModel(TableAccessStat stat, long maxTotalReads)
    {
        Stat = stat;
        double scale = Math.Max(maxTotalReads, 1);
        SeqWidth = stat.SequentialReads <= 0 ? 0 : Math.Max(2, stat.SequentialReads / scale * TrackWidth);
        IdxWidth = stat.IndexReads <= 0 ? 0 : Math.Max(2, stat.IndexReads / scale * TrackWidth);
    }

    public TableAccessStat Stat { get; }

    public string Table => Stat.Table;

    public bool IsSequential => Stat.IsSequential;

    /// <summary>Pixel width of the red (sequential) segment.</summary>
    public double SeqWidth { get; }

    /// <summary>Pixel width of the blue (index) segment.</summary>
    public double IdxWidth { get; }

    public string ReadsText
    {
        get
        {
            if (Stat.SequentialReads > 0 && Stat.IndexReads > 0)
            {
                return string.Format(CultureInfo.CurrentCulture, UiStrings.TableAccessSeqFormat, N(Stat.SequentialReads)) + " · " + string.Format(CultureInfo.CurrentCulture, UiStrings.TableAccessIdxFormat, N(Stat.IndexReads));
            }
            if (Stat.SequentialReads > 0)
            {
                return string.Format(CultureInfo.CurrentCulture, UiStrings.TableAccessSeqFormat, N(Stat.SequentialReads));
            }
            return Stat.IndexReads > 0 ? string.Format(CultureInfo.CurrentCulture, UiStrings.TableAccessIdxFormat, N(Stat.IndexReads)) : string.Empty;
        }
    }

    public bool HasChanges => Stat.TotalChanges > 0;

    /// <summary>Rows this DML / procedure wrote against the table, e.g. "8 upd" or
    /// "3 ins · 8 upd · 2 del" (zero terms omitted). Empty for a read-only table.</summary>
    public string ChangesText
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (Stat.Inserts > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, UiStrings.TableAccessInsFormat, N(Stat.Inserts)));
            if (Stat.Updates > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, UiStrings.TableAccessUpdFormat, N(Stat.Updates)));
            if (Stat.Deletes > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, UiStrings.TableAccessDelFormat, N(Stat.Deletes)));
            return string.Join(" · ", parts);
        }
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
