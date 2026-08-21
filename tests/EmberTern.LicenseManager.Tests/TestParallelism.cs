using Xunit;

// ⭐⭐ THE SUITE RUNS ONE TEST CLASS AT A TIME, and it is not a performance decision.
//
// ⚠⚠ The localization mechanism is GLOBAL STATIC STATE by design: `Loc` holds the current catalog and
//    culture for the process, and `Loc.LanguageChanged` is a static event. That is correct for an
//    application with one window, and it means two test classes running CONCURRENTLY are two threads
//    mutating one language.
//
// ⚠⚠ Measured on the L8.4 run, and the flakiness is what gave it away — the same command reported 1
//    failure and then 5. Two distinct symptoms, one cause:
//      · a test asserting English read a swapped catalog, because another class had swapped it mid-run
//        (`LicenseBrowserTests.TheDefaultViewShowsEverythingAndSaysSo` saw a key where a sentence belonged);
//      · a view model subscribed by one class was notified by another class's language switch AFTER its
//        own fixture had been disposed, so a rebuild queried a closed SQLite connection.
//
// ⭐ L8.4 is what made the second one reachable: before it, no view model went to the database on a
//    language change. The hazard was always there; the stage merely stopped it being theoretical.
//
// ⛔ Do not "fix" this by making a refresh tolerate a closed register, and ⛔ do not chase it with
//    per-class collections: `Loc` is one object for the whole process, so any pair of classes where one
//    writes the language and the other reads a localized string is a race — and that is most of this
//    suite. EmberTern's own suite carries the identical finding (`IsolatesGlobalLanguageState`, and the
//    partition split that hid two defects for months); the answer there and here is to remove the
//    concurrency rather than to enumerate the pairs.
//
// ⚠ Cost, stated: the run is slower. A slow suite is worth more than a suite that is green four times
//   out of five.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
