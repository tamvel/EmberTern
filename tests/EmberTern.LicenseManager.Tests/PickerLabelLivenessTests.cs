using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The measurement L8.4's recon could not answer by reading: does a picker whose items are
/// non-notifying option records re-read its labels when the language changes?</b>
///
/// <para>Every option record in this application deliberately keeps its words OUT of its identity (#394),
/// so a label is a computed property resolved at read time. That makes the C# read live — but a
/// <c>ComboBox</c> renders through <c>{Binding Label}</c> on an item that raises no
/// <c>PropertyChanged</c>, and nothing about switching languages touches the item or the
/// <c>ItemsSource</c>. Whether the control re-reads anyway is a fact about Avalonia, not about our code,
/// and the whole picker design depends on it.</para>
///
/// <para>⚠⚠ Both tests RETURN their <c>Task</c> (gotcha #374), and the lambdas return a value so they bind
/// to <c>Dispatch&lt;T&gt;</c> rather than to <c>Action</c> (#391).</para>
///
/// <para>⚠ Constructs Avalonia controls, so it joins <see cref="ManagerHeadlessCollection"/> — never its
/// own class fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class PickerLabelLivenessTests
{
    private readonly HeadlessUnitTestSession _session;

    public PickerLabelLivenessTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    // ⚠ A real pseudo-locale Windows recognises, so CultureInfo accepts it without a custom culture.
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    /// <summary>A catalog whose answer depends on the culture AND on the key.</summary>
    /// <remarks>
    /// ⚠ Key-dependent on purpose: a picker holds several options, and a catalog answering one value for
    /// every key could not tell "the labels re-read" from "they all collapsed to one word".
    /// </remarks>
    private sealed class PerKeyCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture) =>
            Equals(culture, Pseudo) ? "[[" + name + "]]" : name;
    }

    /// <summary>
    /// ⭐⭐ <b>THE measurement.</b> A realised <c>ComboBox</c> over option records shows the new language
    /// after a change, without its <c>ItemsSource</c> being rebuilt.
    /// </summary>
    [Fact]
    public Task APickerLabel_RereadsWhenTheLanguageChanges() =>
        _session.Dispatch(() =>
        {
            using var isolated = Loc.IsolateSubscribersForVerification();

            try
            {
                Loc.UseCatalogForVerification(new PerKeyCatalog(), CultureInfo.InvariantCulture);

                var options = new List<FilterOption>
                {
                    new StatusFilter(null),
                    new StatusFilter(EmberTern.LicenseManager.Data.LicenseStatuses.Active),
                };

                var combo = new ComboBox
                {
                    ItemsSource = options,
                    SelectedIndex = 0,
                    ItemTemplate = new FuncDataTemplate<FilterOption>(
                        (_, _) =>
                        {
                            var block = new TextBlock();
                            block.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Caption.Value"));
                            return block;
                        },
                        supportsRecycling: true),
                };

                var window = new Window { Content = combo };
                window.Show();
                window.UpdateLayout();

                var before = RenderedSelection(combo);
                Assert.False(string.IsNullOrEmpty(before), "The picker rendered nothing at all — the measurement would be vacuous.");

                Loc.UseCatalogForVerification(new PerKeyCatalog(), Pseudo);
                window.UpdateLayout();

                var after = RenderedSelection(combo);

                window.Close();

                // ⭐ Reported rather than asserted-equal: the POINT is to learn which of the two Avalonia
                //    does, and the assertion below states the conclusion this application relies on.
                Assert.NotEqual(before, after);
                return true;
            }
            finally
            {
                Loc.UseCatalogForVerification(null, null);
            }
        }, default);

    // Reads the text the closed ComboBox is actually showing for its selection.
    private static string? RenderedSelection(ComboBox combo) =>
        combo.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));
}
