using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Settings;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The terminology norm, on the License Manager's side.</b>
///
/// <para><c>terminology.md</c> §4 was ratified as the base of stage L8 and recorded its own gap:
/// <i>"TerminologyTests żyje w EmberTern.Tests, czyli w innym rozwiązaniu, i tej sekcji dziś nie widzi —
/// strażnik po stronie Managera jest pozycją L8.5."</i> This is that guard, and it is deliberately ONE
/// test rather than one per term.</para>
///
/// <para>⭐ <b>What it protects is the half that actually goes wrong: the words INHERITED from the
/// product.</b> §4.1 exists because the License Manager describes the same licences EmberTern describes,
/// so the same fact must not be called two different things on the issuer's side and the customer's. A
/// translator working only in this repository has no way to know that — which is exactly why it is checked
/// mechanically rather than trusted.</para>
///
/// <para>⛔ It does NOT try to judge the rest of the vocabulary. A test asserting that every Polish label
/// reads well is a test nobody can keep green, and wording is a human decision (§4's own caveat: a term
/// that reads badly in the real window may still be corrected, by the user, in L8.6).</para>
/// </summary>
public sealed class ManagerTerminologyTests
{
    /// <summary>
    /// ⛔ The terms §4.1 inherits from the product must read EXACTLY as the product reads them.
    /// </summary>
    /// <remarks>
    /// ⚠ The expected words are spelled out here rather than read from <c>EmberTern.App</c>: the two
    /// solutions are separate on purpose (the private key must never be reachable from one that ships), so
    /// this file cannot reference the product's catalog. ⭐ That makes the list a second copy of a fact —
    /// accepted knowingly, because the alternative is no check at all, and a mismatch here is loud.
    /// </remarks>
    [Theory]
    // §4.1 — odziedziczone z produktu.
    [InlineData("Main.LicensedTo", "Licencjobiorca")]
    [InlineData("Main.LicenceId", "Identyfikator licencji")]
    [InlineData("Main.Seats", "Stanowiska")]
    [InlineData("Main.ValidFrom", "Ważna od")]
    [InlineData("FileType.Licence", "Licencja EmberTern")]
    [InlineData("Settings.SaveAction", "Zapisz")]
    [InlineData("Confirm.Cancel", "Anuluj")]
    [InlineData("Common.Close", "Zamknij")]
    [InlineData("Main.Clear", "Wyczyść")]
    [InlineData("Settings.Language", "Język")]
    [InlineData("Main.FirstName", "Imię")]
    [InlineData("Main.LastName", "Nazwisko")]
    // §4.2 / §4.3 — ratyfikowane słownictwo License Managera.
    [InlineData("Main.SaveTerms", "Zapisz warunki")]
    [InlineData("Main.IssueSave", "Wystaw i zapisz…")]
    [InlineData("Main.InspectLatest", "Sprawdź najnowsze")]
    [InlineData("Storage.BackupAction", "Utwórz kopię zapasową…")]
    [InlineData("Settings.RevertAction", "Przywróć zapisane")]
    [InlineData("Settings.ForgetSettings", "Usuń zapisane ustawienia")]
    [InlineData("Main.Standing", "Termin")]
    [InlineData("Main.Status", "Stan")]
    [InlineData("Main.Expiry", "Wygasa")]
    [InlineData("Unlock.Passphrase", "Hasło dostępu")]
    [InlineData("Reason.Initial", "Pierwsze wystawienie")]
    [InlineData("Reason.ReissueLost", "Ponowne wystawienie — utracony plik")]
    [InlineData("ArtifactStanding.Current", "bieżący")]
    [InlineData("ArtifactStanding.Superseded", "zastąpiony")]
    [InlineData("Storage.RegisterRecord", "Rejestr wzorcowy")]
    // §4.3 — słownictwo powierzchni ceremonii, dodane przez L7.1 jako rozszerzenie normy.
    [InlineData("Storage.SigningKeyTab", "Klucz podpisujący")]
    [InlineData("Storage.KeyId", "Identyfikator klucza")]
    [InlineData("Storage.VerifyBackupAction", "Sprawdź kopię zapasową…")]
    public void EveryRatifiedTerm_ReadsAsTheNormSaysItDoes(string key, string expected)
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.Apply(ApplicationLanguages.Polish);
            Assert.Equal(expected, Loc.Text(key));
        }
        finally
        {
            Loc.Apply(ApplicationLanguages.Default);
        }
    }

    /// <summary>
    /// ⛔ Nothing `terminology.md` §4.4 calls a technical contract has been translated.
    /// </summary>
    /// <remarks>
    /// ⭐ The two language names are the sharp case: a language is named IN ITSELF, so <c>English</c> stays
    /// <c>English</c> in the Polish interface — the one person who cannot read the current language is
    /// exactly the one reaching for that picker. ⚠ And a format's NAME is not a word.
    /// </remarks>
    [Fact]
    public void NothingTheNormCallsATechnicalContract_WasTranslated()
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.Apply(ApplicationLanguages.Polish);

            // Nazwy języków — każdy nazwany w sobie (§4.4).
            Assert.Equal("English", ManagerSettingsCatalog.LanguageLabel("en"));
            Assert.Equal("Polski", ManagerSettingsCatalog.LanguageLabel("pl"));

            // Nazwa formatu, nie słowo.
            Assert.Equal("JSON Lines", Loc.Text("FileType.JsonLines"));

            // ⭐ Verbatim jest nośnikiem cudzych słów i nigdy nie może stać się zdaniem.
            Assert.Equal("{0}", Loc.Text("Status.Verbatim"));

            // Marka zostaje marką — wewnątrz przetłumaczonej frazy.
            Assert.Contains("EmberTern", Loc.Text("FileType.RegisterBackup"), StringComparison.Ordinal);
        }
        finally
        {
            Loc.Apply(ApplicationLanguages.Default);
        }
    }

    /// <summary>
    /// ⭐ Polish really does inflect: the three arms of a family are three different sentences.
    /// </summary>
    /// <remarks>
    /// ⚠ This is the test for the CHANGE L8.5 made, not for the mechanism — the mechanism was measured in
    /// L8.1. Without it the Polish catalog could declare three identical arms and every completeness guard
    /// would stay green.
    /// </remarks>
    [Fact]
    public void ACountedSentence_TakesADifferentFormForOneFewAndMany()
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.Apply(ApplicationLanguages.Polish);

            Assert.Equal("1 stanowisko", Loc.FormatCount("Row.Seats", 1));
            Assert.Equal("3 stanowiska", Loc.FormatCount("Row.Seats", 3));
            Assert.Equal("7 stanowisk", Loc.FormatCount("Row.Seats", 7));

            // ⭐ D‑6: „bajt” odmienia się jak każdy inny rzeczownik — {0} jest osią, {1} liczbą wypisywaną.
            Assert.Equal("1 bajt w dostarczonej postaci",
                Loc.FormatCount("History.TokenSizeAsDelivered", 1, "1"));
            Assert.Equal("512 bajtów w dostarczonej postaci",
                Loc.FormatCount("History.TokenSizeAsDelivered", 512, "512"));

            var arms = new HashSet<string>(StringComparer.Ordinal)
            {
                Loc.FormatCount("Licences.Count", 1),
                Loc.FormatCount("Licences.Count", 2),
                Loc.FormatCount("Licences.Count", 5),
            };

            Assert.Equal(3, arms.Count);
        }
        finally
        {
            Loc.Apply(ApplicationLanguages.Default);
        }
    }
}
