using System;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// The ONE place a calendar day chosen by an operator becomes an instant a licence carries.
///
/// <para>⭐⭐ <b>It exists because two surfaces now pick dates.</b> The licence form has always read a
/// chosen day as a UTC day and run the expiry to the END of it; a batch that extends twenty licences to
/// a date asks exactly the same question, and a second copy of the rule is how the two would come to
/// disagree by a day — silently, and only for the licences that went through the other path.</para>
///
/// <para>⚠ The end-of-day rule is not cosmetic. Storing midnight would expire a licence at the START of
/// the day it says it is valid until, which is an off-by-one nobody reads as a bug until a customer is
/// locked out on a date their invoice says they own.</para>
///
/// <para>⚠ A picker hands back a <see cref="DateTime"/> whose <c>Kind</c> is <c>Unspecified</c>. Taking
/// <c>.Date</c> and pinning the offset to zero is what keeps a licence issued in Warsaw and one issued in
/// London meaning the same thing.</para>
/// </summary>
public static class LicenseDay
{
    /// <summary>The first instant of the chosen day, in UTC.</summary>
    public static DateTimeOffset StartOf(DateTime day) => new(day.Date, TimeSpan.Zero);

    /// <summary>The LAST instant of the chosen day, in UTC — one second before the next day begins.</summary>
    public static DateTimeOffset EndOf(DateTime day) => StartOf(day).AddDays(1).AddSeconds(-1);
}
