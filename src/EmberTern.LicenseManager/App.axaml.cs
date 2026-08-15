using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;

namespace EmberTern.LicenseManager;

/// <summary>
/// The composition root.
///
/// <para>⭐ <b>Dependency injection by constructor, with no container.</b> Every collaborator is passed
/// in — <c>LicenseRegister</c>, <c>SigningSession</c>, the clock — which is what makes the view models
/// testable without a window. What is deliberately absent is a container: EmberTern has none across 126
/// view models, this application has four services, and a container would add a package, a registration
/// list and an indirection whose only job is to build objects that are already trivial to build.
/// ⚠ Flagged for the user's override, since the original brief listed "DI" as a technology — this IS
/// dependency injection, just the wiring rather than the framework.</para>
/// </summary>
public sealed partial class App : Application
{
    private LicenseRegister? _register;
    private SigningSession? _session;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = ManagerPaths.Default;
            paths.EnsureFolder();

            _register = LicenseRegister.Open(paths.Register);

            var unlock = new UnlockViewModel(paths);
            unlock.Unlocked += session => OpenShell(desktop, session);

            desktop.MainWindow = new UnlockWindow { DataContext = unlock };
            desktop.ShutdownRequested += (_, _) => Release();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OpenShell(IClassicDesktopStyleApplicationLifetime desktop, SigningSession session)
    {
        _session = session;

        var previous = desktop.MainWindow;
        var shell = new MainWindow
        {
            DataContext = new ShellViewModel(
                _register ?? throw new InvalidOperationException("The register is not open."), session),
        };

        // ⚠ Order matters: the new window becomes MainWindow BEFORE the old one closes. Closing the
        //    lifetime's current MainWindow first shuts the application down, which is a very quick way
        //    to make a successful unlock look like a crash.
        desktop.MainWindow = shell;
        shell.Show();
        previous?.Close();
    }

    private void Release()
    {
        _session?.Dispose();
        _register?.Dispose();
        _session = null;
        _register = null;
    }
}
