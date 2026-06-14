using System;

namespace EmberTern.Core.Settings;

// The on-disk envelope around the (encrypted) settings payload. Before this existed,
// settings.dat was the bare protector output with no way to tell what it was or how it
// was encrypted. The container is a single UTF-8 header line, then the payload verbatim:
//
//     EMBERTERN-SETTINGS<TAB><containerVersion><TAB><encryptionScheme>\n
//     <payload exactly as the protector produced it>
//
// The header is deliberately OUTSIDE the encryption so a load can read it WITHOUT the
// key: identify the file as ours (magic), pick the matching protector (scheme), and
// refuse a newer container layout (version) — all in ApplicationSettingsStore.
//
// Backward compatibility: a legacy headerless settings.dat (bare blob, no magic on the
// first line) makes TryParse return false; the caller treats the whole file as the
// payload and re-wraps it with a header on the next Save.
public static class SettingsFileContainer
{
    // First token of the header line. Identifies the file as an EmberTern settings file.
    public const string Magic = "EMBERTERN-SETTINGS";

    // Version of the CONTAINER layout (the header itself) — distinct from the data
    // SchemaVersion that lives inside the payload. Bump only when the header format
    // changes (e.g. a new mandatory field), never for data-model changes.
    public const int CurrentContainerVersion = 1;

    private const char Separator = '\t';

    // header line + payload. Payload is appended verbatim (no re-encoding); its own
    // internal newlines, if any, are irrelevant because parsing only splits on the FIRST
    // newline (the header terminator we write here).
    public static string Wrap(int containerVersion, string encryptionScheme, string payload)
        => $"{Magic}{Separator}{containerVersion}{Separator}{encryptionScheme}\n{payload}";

    // Parses the container header off the front of a settings.dat. Returns false for a
    // legacy headerless file (no magic on the first line), in which case `payload` is the
    // whole content and the caller decrypts it with the build's injected protector.
    public static bool TryParse(string content, out SettingsContainerHeader header, out string payload)
    {
        header = default;
        payload = content ?? string.Empty;
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        var newlineIndex = content.IndexOf('\n');
        var firstLine = newlineIndex >= 0 ? content[..newlineIndex] : content;
        // Defensive: tolerate a stray CR even though we always write a bare \n.
        if (firstLine.EndsWith('\r'))
        {
            firstLine = firstLine[..^1];
        }

        var parts = firstLine.Split(Separator);
        // Minimum: magic, version, scheme. Extra trailing fields are tolerated and
        // ignored here — reserved for forward-compatible header additions.
        if (parts.Length < 3 || !string.Equals(parts[0], Magic, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var version))
        {
            return false;
        }

        payload = newlineIndex >= 0 ? content[(newlineIndex + 1)..] : string.Empty;
        header = new SettingsContainerHeader(version, parts[2]);
        return true;
    }
}

// Parsed container header. EncryptionScheme is one of EmberTern.Core.Security.EncryptionSchemes.
public readonly record struct SettingsContainerHeader(int ContainerVersion, string EncryptionScheme);
