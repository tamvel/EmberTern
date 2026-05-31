using System;
using System.Xml;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Connections;
using EmberTern.Firebird;

namespace EmberTern.App;

public class App : Application
{
    // Dark variant is the default (and the name `MainWindow` keeps as the
    // initial assignment when no DataContext is attached yet). The light
    // variant exists so the editor's foregrounds stay readable on the
    // near-white #F3F3F3 background.
    public const string FirebirdSyntaxName = "Firebird SQL";
    public const string FirebirdSyntaxLightName = "Firebird SQL Light";

    private FirebirdConnectionService? _service;
    private TransactionService? _transactionService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        RegisterFirebirdSyntax();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new ConnectionProfileStore();
            _service = new FirebirdConnectionService();
            _transactionService = new TransactionService(_service);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(store, _service, _transactionService),
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _transactionService?.Dispose();
                _service?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterFirebirdSyntax()
    {
        RegisterIfMissing(FirebirdSyntaxName, "avares://EmberTern/Assets/FirebirdSql.xshd");
        RegisterIfMissing(FirebirdSyntaxLightName, "avares://EmberTern/Assets/FirebirdSql.Light.xshd");
    }

    private static void RegisterIfMissing(string name, string avaresUri)
    {
        if (HighlightingManager.Instance.GetDefinition(name) is not null)
        {
            return;
        }

        using var stream = AssetLoader.Open(new Uri(avaresUri));
        using var reader = XmlReader.Create(stream);
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting(name, new[] { ".sql" }, definition);
    }
}
