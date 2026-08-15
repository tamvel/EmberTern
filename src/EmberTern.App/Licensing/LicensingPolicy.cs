namespace EmberTern.App.Licensing;

/// <summary>
/// ⭐⭐ <b>The ONE place in the application where the build configuration changes behaviour.</b>
///
/// <para><b>The rule, stated precisely (design §16.5, decision D15): <c>Debug</c> disables the BLOCK, not
/// the LICENSING.</b> Verification runs identically in both configurations — the file is resolved, the
/// signature checked, the verdict computed, displayed and logged the same way. The only difference is
/// whether an absent or invalid verdict prevents the application from being used.</para>
///
/// <para>⭐ <b>The bypass is in the gate, never in the verifier</b>, and that is load-bearing rather than
/// tidy: the test suite runs in <c>Debug</c>, so a bypass inside <c>EmberTern.Licensing</c> would make the
/// entire tamper corpus vacuous — every licensing test would pass while proving nothing.</para>
///
/// <para>⛔ <b>No configuration switch exists in a <c>Release</c> build.</b> Not a setting, not an
/// environment variable, not a command-line argument, not a file. The only input is which configuration
/// was compiled. ⛔ <c>Debugger.IsAttached</c> is never consulted: it is a *runtime* fact an attacker
/// controls, and it would also make a <c>Release</c> build behave differently under a profiler.</para>
///
/// <para>⭐ <b>Because <see cref="GateEnabled"/> is a <c>const</c>, the compiler folds every
/// <c>if (LicensingPolicy.GateEnabled)</c> and eliminates the dead arm.</b> A <c>Release</c> binary
/// therefore contains no bypass code to patch back on — there is nothing there.</para>
///
/// <para>⚠ Four guard tests hold this shape: the runtime pair (which only a <c>Release</c> run can prove
/// for the <c>Release</c> arm), the source structure, the absence of any runtime switch, and — the
/// cheapest and least obvious — that no project file smuggles <c>DEBUG</c> into a non-<c>Debug</c>
/// configuration. Without that fourth one the other three stay green while the bypass ships.</para>
/// </summary>
internal static class LicensingPolicy
{
#if DEBUG
    /// <summary>Whether an unusable licence verdict may block the application.</summary>
    internal const bool GateEnabled = false;
#else
    /// <summary>Whether an unusable licence verdict may block the application.</summary>
    internal const bool GateEnabled = true;
#endif
}
