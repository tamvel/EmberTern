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
    private ManagerPaths? _paths;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = ManagerPaths.Default;
            paths.EnsureFolder();

            _paths = paths;
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
                _register ?? throw new InvalidOperationException("The register is not open."),
                session,
                _paths ?? throw new InvalidOperationException("The paths are not resolved.")),
        };

        // ⚠ Order matters: the new window becomes MainWindow BEFORE the old one closes. Closing the
        //    lifetime's current MainWindow first shuts the application down, which is a very quick way
        //    to make a successful unlock look like a crash.
        desktop.MainWindow = shell;
        shell.Show();
        previous?.Close();
    }

    /// <summary>
    /// Closes the active register and releases its file, reporting whether it let go.
    ///
    /// <para>⭐ The one operation that needs this is replacing the active register (D‑6): SQLite holds the
    /// file while the register is open, so it cannot be moved aside until this has run. ⛔ It does NOT
    /// reopen anything — after it, this application has no register and is on its way to shutting down.
    /// Re-pointing the running view models at a different register is a separate stage.</para>
    ///
    /// <para>⚠ The restore still PROVES the file is free by opening it exclusively rather than trusting
    /// the <see langword="true"/> this returns. A caller that believes a claim about the one fact that
    /// must hold before anything is moved is a caller that finds out half way through.</para>
    /// </summary>
    internal bool ReleaseRegister()
    {
        try
        {
            _register?.Dispose();
            _register = null;
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Data.Common.DbException)
        {
            return false;
        }
    }

    private void Release()
    {
        _session?.Dispose();
        _register?.Dispose();
        _session = null;
        _register = null;
    }
}
