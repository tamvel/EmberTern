using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.CodeActions;

/// <summary>
/// One offer the IDE can make at a position: a title the user reads, and the edits applying it performs.
/// The <b>shared currency</b> of every producer — Quick Fixes today, safe local rename (which the App
/// applier serves from the same code path), and any future refactoring producer.
/// <para>
/// <b>Atomic.</b> <see cref="Edits"/> are applied all-or-nothing, as one undo unit. A partially-applied
/// action would leave code neither the user nor EmberTern authored.
/// </para>
/// <para>
/// <b>Why this is not an abstraction over "some action" (evaluated + ratified, Q1).</b> Every action that
/// exists — and every one on the v1 list — is a set of text edits, so an abstract base with a single
/// derived type would add a discriminator no caller could branch on: dead code that a later reader
/// mistakes for a regression (gotcha #233), and an interface below two implementations (rule #2). The
/// migration cost of waiting is small and known: a non-edit action (open a dialog, run an IDE command)
/// adds an optional member here or a sibling type, plus ONE branch where the App activates an action —
/// which is why activation is deliberately funnelled through a single point rather than reaching into
/// <see cref="Edits"/> from several places. An empty <see cref="Edits"/> list is already a valid "changes
/// no text" action, so nothing here blocks that step.
/// </para>
/// <para>Pure data — no Avalonia. See
/// <see href="../../docs/design/editor-quick-fixes.md">editor-quick-fixes.md</see> §3/§5.</para>
/// </summary>
/// <param name="Title">What the menu shows, e.g. <c>Qualify as 'k.nazwa'</c>. Terse, non-judgmental.</param>
/// <param name="Edits">The replacements this action performs. Never overlapping; order is irrelevant
/// (the applier sorts) — but they are one atomic unit.</param>
public sealed record CodeAction(string Title, IReadOnlyList<TextEdit> Edits);
