using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using EmberTern.Core.Security;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// Writes a settings export: applies the ratified content classification (design §6.3.4), serializes the result
/// exactly as <c>settings.dat</c> is serialized, encrypts it under the caller's passphrase, and wraps it in the
/// versioned envelope.
///
/// <para><b>⚠ Every export is encrypted, unconditionally (ratified Q3).</b> There is no unencrypted variant and
/// no parameter that could produce one — an empty passphrase throws. One uniform format means the user never
/// chooses a variant, and future data can be added without changing what the file behaves like.</para>
///
/// <para><b>⭐ The <c>appVersion</c> seam, and why it is an input rather than something Core reads.</b> The design
/// says the app version is written from <c>AppInfo</c>, never as a literal — but <c>AppInfo</c> lives in
/// <c>EmberTern.App</c> and Core cannot see it (nor should it: architecture rule #1's direction). So it travels
/// IN, as a required parameter. That is also exactly the right shape for a field that is <b>diagnostics only and
/// must never be branched on</b>: Core does not derive it, and cannot condition on what it never computes.
/// ⛔ Do not add a literal fallback here — a literal version in code is the stale copy
/// <c>AppInfoTests</c> exists to prevent.</para>
/// </summary>
public static class SettingsExporter
{
    /// <summary>
    /// Produces the whole export file as text.
    /// </summary>
    /// <param name="source">The live settings. <b>Never mutated</b> — the writer works on a deep copy, so
    /// stripping a password out of the export cannot strip it out of the running app.</param>
    /// <param name="options">What to include. Its defaults are §6.3.4's classification.</param>
    /// <param name="passphrase">Required. The key is derived from it; it is unrecoverable.</param>
    /// <param name="appVersion">From <c>AppInfo.Version</c> at the App boundary. Diagnostics only.</param>
    /// <param name="iterations">KDF iterations. Defaulted for production; tests pass a small value because a
    /// production count deliberately costs a noticeable fraction of a second, and the file states what it used.</param>
    /// <exception cref="ArgumentException">The passphrase is empty, or no section was selected.</exception>
    public static string Export(
        ApplicationSettings source,
        SettingsExportOptions options,
        string passphrase,
        string appVersion,
        int iterations = PassphraseProtector.DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(appVersion);

        if (string.IsNullOrEmpty(passphrase))
        {
            throw new ArgumentException("A passphrase is required — every export is encrypted.", nameof(passphrase));
        }
        if (options.IsEmpty)
        {
            // Refusing beats writing an encrypted envelope around nothing: such a file imports "successfully"
            // and changes nothing, which is the least diagnosable outcome available.
            throw new ArgumentException("Select at least one section to export.", nameof(options));
        }

        var content = BuildContent(source, options);
        var json = JsonSerializer.Serialize(content, ApplicationSettingsStore.JsonOptions);

        var salt = PassphraseProtector.NewSalt();
        var protector = PassphraseProtector.Create(passphrase, salt, iterations);
        var header = new SettingsExportHeader(
            SettingsExportFormat.CurrentFormatVersion,
            appVersion,
            EncryptionSchemes.PassphraseAes256,
            PassphraseProtector.Pbkdf2Sha256,
            iterations,
            salt);

        return SettingsExportEnvelope.Wrap(header, protector.Protect(json));
    }

    /// <summary>Writes the export to <paramref name="path"/>. UTF-8 <b>without a BOM</b>: the magic must be the
    /// literal first bytes of the file, and a BOM would put three bytes in front of it.</summary>
    public static void ExportTo(
        string path,
        ApplicationSettings source,
        SettingsExportOptions options,
        string passphrase,
        string appVersion,
        int iterations = PassphraseProtector.DefaultIterations)
    {
        var content = Export(source, options, passphrase, appVersion, iterations);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// ⭐ The content classification itself — design §6.3.4, in one method, so "what travels" has one place to
    /// read and one place to change.
    ///
    /// <para>Exposed because it is worth testing directly: these decisions are a rule #11 surface (they include
    /// credentials), and asserting them through decryption only would prove the round trip rather than the
    /// policy.</para>
    /// </summary>
    public static SettingsExportContent BuildContent(ApplicationSettings source, SettingsExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        // Deep copy first, then remove. Copying via the store's own serializer rather than a hand-written clone
        // is deliberate: a copy constructor is a second list of properties, and it goes stale silently the day
        // someone adds a field — which is the failure this whole sprint keeps designing against.
        var copy = DeepCopy(source);
        var exported = new ApplicationSettings { SchemaVersion = copy.SchemaVersion };

        if (options.Preferences)
        {
            exported.UserSettings.Preferences = copy.UserSettings.Preferences;
        }

        if (options.GridProfiles)
        {
            exported.UserSettings.GridProfiles = copy.UserSettings.GridProfiles;
        }

        if (options.Folders)
        {
            exported.Folders = copy.Folders;
        }

        if (options.Connections)
        {
            foreach (var connection in copy.Connections)
            {
                // ⚠ Opt-in (Q2). Cleared rather than omitted, because the property is non-nullable and an empty
                // password is what "no password stored" already means everywhere else in the app.
                if (!options.Passwords)
                {
                    connection.Password = string.Empty;
                }

                // ❌ The v1→v2 migration shim. It is cleared on the next save of settings.dat anyway, and an
                // export carrying it would hand the importer a legacy field to re-consume for no reason.
                connection.LegacyTransactionProfile = null;

                exported.Connections.Add(connection);
            }
        }

        if (options.Workspaces)
        {
            // ⚠ Q6: tabs, SQL text and saved queries travel — but NOT WindowBounds, which is monitor geometry
            // and can place the window off-screen on the importing machine. The rest of WorkspaceState (sidebar
            // width, panel heights, the four Source/Easy seeds, the last bottom tab) is ordinary layout
            // preference and rides with the opt-in.
            exported.Workspace = copy.Workspace;
            exported.Workspace.WindowBounds = null;
        }

        if (options.ImportProfiles)
        {
            exported.UserSettings.ImportProfiles = copy.UserSettings.ImportProfiles;
        }

        // ❌ ParameterHistory and DebugWatches are never copied across — execution history rather than settings,
        // and keyed to connection ids. There is deliberately no option that could include them.

        return new SettingsExportContent
        {
            Sections = new List<string>(options.Sections()),
            Settings = exported,
        };
    }

    private static ApplicationSettings DeepCopy(ApplicationSettings source)
    {
        var json = JsonSerializer.Serialize(source, ApplicationSettingsStore.JsonOptions);
        return JsonSerializer.Deserialize<ApplicationSettings>(json, ApplicationSettingsStore.JsonOptions)
               ?? new ApplicationSettings();
    }
}
