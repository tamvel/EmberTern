using System.Globalization;
using System.Text.Json.Nodes;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// Brings an export payload from the format version its header declares up to
/// <see cref="SettingsExportFormat.CurrentFormatVersion"/>.
///
/// <para><b>A stepwise ladder, copied from <c>ApplicationSettingsStore.MigrateToCurrentVersion</c> deliberately
/// rather than invented</b> — each step upgrades by exactly ONE version and is independent of the others, so a
/// future contributor adds one <c>case</c> without needing to understand any earlier step.</para>
///
/// <para>⭐ <b>ONE deliberate divergence from that model, and it is the important line in this file: a missing
/// step REFUSES here, where the settings.dat ladder stamps the current version and continues.</b> The two are
/// right for their own situations, and confusing them would be a rule #11 defect:</para>
/// <list type="bullet">
///   <item><description>
///     For <c>settings.dat</c>, "no registered step" means a file this build wrote in its own current shape and
///     merely mislabelled; stamping it is harmless and stops an infinite loop.
///   </description></item>
///   <item><description>
///     For an <b>import</b>, it means a file whose shape we genuinely do not know how to read. Claiming it is
///     current would import whatever happened to deserialize and silently drop the rest — and <b>a partial import
///     is worse than none</b>. So it refuses, by name, naming the version.
///   </description></item>
/// </list>
///
/// <para><b>Why a <see cref="JsonObject"/> and not a deserialized <see cref="SettingsExportContent"/>:</b> a
/// format migration is exactly the case where a field may have been renamed, moved or split, so the old shape may
/// not deserialize into the current type at all. Migrating the JSON first and deserializing after is the order
/// that will still work when there is a version 2; the reverse order silently loses whatever the current type has
/// no property for.</para>
///
/// <para>⚠ <b>There are no steps today, and that is not an omission.</b> Version 1 is the first, so nothing older
/// exists to migrate. What IS provable today — and is pinned by tests through the public reader — is the
/// behaviour at both edges: a version above the ceiling is refused naming it, and a version below the oldest step
/// is refused rather than accepted. The first real step arrives with format version 2.</para>
/// </summary>
internal static class SettingsExportMigration
{
    /// <summary>
    /// Migrates <paramref name="payload"/> in place from <paramref name="fromVersion"/> to
    /// <see cref="SettingsExportFormat.CurrentFormatVersion"/>.
    /// </summary>
    /// <returns><c>true</c> when the payload is now current; <c>false</c> when a step is missing, with the reason
    /// in <paramref name="diagnostic"/>.</returns>
    internal static bool TryMigrateToCurrent(JsonObject payload, int fromVersion, out string diagnostic)
    {
        diagnostic = string.Empty;

        for (var version = fromVersion; version < SettingsExportFormat.CurrentFormatVersion; version++)
        {
            if (!TryApplyStep(payload, version, fromVersion, out diagnostic))
            {
                return false;
            }
        }

        return true;
    }

    // ONE version's worth of upgrade. Split out from the loop only so that a ladder whose steps all return
    // (which is the state while there are none) does not make the loop's increment unreachable code — with
    // TreatWarningsAsErrors that is a build failure, and shaping the ladder around it beats suppressing it.
    private static bool TryApplyStep(JsonObject payload, int version, int declaredVersion, out string diagnostic)
    {
        diagnostic = string.Empty;

        switch (version)
        {
            // Future steps go here, one per version, each independent of the others. Template:
            // case 1:
            //     Migrate_1_2(payload);
            //     return true;

            default:
                // No registered step — see the class remarks for why this refuses rather than stamping.
                diagnostic = string.Format(
                    CultureInfo.InvariantCulture,
                    "This settings export declares format version {0}, which this build has no migration step "
                    + "for. It cannot be imported.",
                    declaredVersion);
                return false;
        }
    }
}
