using System;

namespace EmberTern.Office;

/// <summary>
/// The ONE owner of Excel's serial-number calendar — in both directions.
/// <para>
/// ⚠ Excel and <see cref="DateTime.FromOADate"/> do not share an epoch below 1900-03-01: Excel's serial 1 is
/// 1900-01-01 while OLE's is 1899-12-31, and Excel additionally carries the phantom 1900-02-29 (serial 60) that
/// never existed. Blindly calling <c>FromOADate</c> shifts every January/February 1900 date by a day, silently —
/// which is §0.1's worst class of failure.
/// </para>
/// <para>
/// ⭐ <b>Why it is a class of its own rather than a method on the reader.</b> Etap I9 needed only
/// <see cref="FromSerial"/>, so it lived inside <c>WorkbookCellReader</c>. I10 added a second spreadsheet
/// provider that needs the same fact <b>in reverse</b>: ExcelDataReader hands back a <see cref="DateTime"/>
/// already, so honouring "give me the serial number instead" means converting back — and an inverse written
/// beside a forward function it cannot see is exactly how two halves of one calendar drift apart. Both
/// providers now compute a date from a serial through the same code, which is what makes them agree cell for
/// cell on the same workbook content.
/// </para>
/// </summary>
internal static class ExcelSerialDate
{
    /// <summary>Excel serial 61 is 1900-03-01, the first day on which Excel's calendar and
    /// <see cref="DateTime.FromOADate"/> agree.</summary>
    private const double FirstUnambiguousSerial = 61d;

    /// <summary>9999-12-31 — <see cref="DateTime.FromOADate"/>'s upper bound.</summary>
    private const double MaximumSerial = 2958465.9999999d;

    /// <summary>
    /// A serial number → the CLR value it names: a <see cref="DateTime"/>, a <see cref="TimeSpan"/> for a pure
    /// time of day, or the <see cref="double"/> itself when no date can be named truthfully.
    /// <para>
    /// The phantom day and any negative serial deliberately stay NUMBERS. The converter will then refuse them
    /// for a DATE column with an honest message, which is the §0.1 answer — inventing a date that has no
    /// calendar is not.
    /// </para>
    /// </summary>
    public static object FromSerial(double serial)
    {
        // A pure time of day (Excel formats 45–47): no date part at all, so a TimeSpan says exactly that. The
        // converter maps it straight onto a TIME column.
        if (serial >= 0d && serial < 1d) return TimeSpan.FromDays(serial);

        if (serial >= FirstUnambiguousSerial)
        {
            return serial <= MaximumSerial ? DateTime.FromOADate(serial) : serial;
        }

        // 1 ≤ serial < 60 — before the phantom day, so Excel is exactly one ahead of OLE.
        if (serial >= 1d && serial < 60d) return DateTime.FromOADate(serial + 1d);

        // serial < 0, or the phantom 1900-02-29.
        return serial;
    }

    /// <summary>
    /// A <see cref="DateTime"/> → the serial number Excel stores for it: the exact inverse of
    /// <see cref="FromSerial"/> over every value that function can produce.
    /// <para>
    /// Note the second branch also covers a pure time of day, which reaches this method as OLE's 1899-12-31 plus
    /// the time (OA 1.x) and must come back out as the sub-1 serial it was.
    /// </para>
    /// </summary>
    public static double ToSerial(DateTime value)
    {
        var oaDate = value.ToOADate();
        return oaDate >= FirstUnambiguousSerial ? oaDate : oaDate - 1d;
    }
}
