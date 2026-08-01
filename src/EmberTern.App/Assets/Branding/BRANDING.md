# EmberTern branding assets

Everything the application's visual identity is made of, and the complete list of places that consume it.
Written so the artwork can be replaced without hunting for references.

Sibling document: [`../Icons/ICONS.md`](../Icons/ICONS.md) — the *UI* icon system (stroked 24×24 geometries).
This file is about the *product mark*, which is a different thing and follows none of those rules.

---

## The files

Current artwork: the forged-steel database cylinder with the ember wing, adopted 2026-08-01.

## ⭐ TWO masters, on purpose — the OS icon and the About mark are DIFFERENT artwork

This is the first thing to understand, because the obvious assumption is wrong and acting on it would
silently change a surface nobody asked you to touch.

The two shipped files are **not** rendered from one source. The icon and the in-app mark are judged at
completely different sizes — 16–32px in a taskbar versus 128px on a quiet window — and a rendition that
reads well at one does not automatically read well at the other. On 2026-08-01 the user replaced the OS
icon *only*, after seeing the first version in the Windows taskbar, and kept the About mark unchanged.

**So: changing one does not change the other, and that is the design.** If you are asked to "update the
logo", ask which one.

| File | Ships? | What it is | Rendered from | Who consumes it |
|---|---|---|---|---|
| `Masters/logo.png` | **no** | Source artwork, 673×673 RGBA, already transparent. | — | Nobody. |
| `Masters/icon-source.png` | **no** | Source artwork, 1254×1254, **opaque near-black background**. | — | Nobody. |
| `EmberTern.ico` | yes | The **OS icon**. 16/24/32/48/64/256, every entry 32-bit RGBA PNG-compressed. | `Masters/icon-source.png` | `<ApplicationIcon>` in the csproj **and** the one `Window` style — see below. |
| `EmberTern_logo.png` | yes | The **UI mark**, 256×256 transparent. | `Masters/logo.png` | `AboutWindow.axaml`, at 128px. The only place a logo appears inside the application. |

⚠ Everything under `Masters/` is excluded from `AvaloniaResource` **as a folder**, so a new master dropped
in there never ships by accident. Never reference a master from XAML: it is un-padded, arbitrarily sized and
may have an opaque background.

⚠ The two masters need **different treatment**, which is why the two pipelines below are not one:
`logo.png` is already transparent and gets a 5% pad; `icon-source.png` has an opaque background that must be
cut away and is then cropped tight with **no** pad.

---

## Where the identity is consumed — the complete list

Three places, each with a distinct job. Changing the artwork means regenerating the two shipped files; none
of these paths needs to move.

1. **`src/EmberTern.App/EmberTern.App.csproj` → `<ApplicationIcon>Assets\Branding\EmberTern.ico`**
   Embeds the icon in `EmberTern.exe`. This is what Explorer shows, what a shortcut shows, and what the
   taskbar shows *before* the application has opened a window. It is a build-time embed — Avalonia never
   sees it.

2. **`src/EmberTern.App/Themes/ControlStyles.axaml` → `<Style Selector="Window">` → `Icon`**
   ⭐ **The one runtime owner.** `Window.Icon` is a styled property and defaults to `null`, which the OS
   draws as a blank slot in the title bar and in Alt+Tab; Avalonia has no application-level equivalent.
   One setter reaches all 26 windows *and every window added later*, which a per-window assignment cannot.
   **Do not set `Icon` on an individual window** — a local value outranks a style setter, so it would
   quietly create a second source of truth for exactly one window. (That is what the code-behind
   assignment in `MainWindow` used to be; it was removed on 2026-08-01.)

3. **`src/EmberTern.App/Views/AboutWindow.axaml` → `<Image Source=…EmberTern_logo.png>`**
   The brand mark as the subject of a window. The **only** logo inside the running UI — deliberately, since
   the titlebar mark was removed the same day: a working surface's chrome belongs to the document, and the
   identity belongs in About.

Pinned by `BrandingPresentationTests`: every window (including a bare `new Window()`) receives an icon, and
the main window's titlebar contains no bitmap mark.

---

## Replacing the artwork

Decide **which** asset first (see the two-masters section above), drop the new file in `Masters/`, regenerate
just that asset, rebuild. No path, XAML or code changes either way.

Then check it at the size it is actually judged at: the icon at **16 and 32px, against a real taskbar**, the
About mark at **128px**. A mark that reads at 256 and turns to mud at 16 is the usual failure — and it is
exactly what happened here, which is why the two assets have separate masters at all.

### The About mark — `Masters/logo.png` → `EmberTern_logo.png`

Transparent source. Alpha bounding box → **5% pad** → square canvas → 256×256. The pad is what keeps the mark
from touching the edges of a large, quiet presentation slot.

1. Find the alpha bounding box (`LockBits` + a byte-array scan — a PowerShell pixel loop is far too slow on
   ~1M pixels).
2. Content rule: `alpha >= 32 AND NOT (R>=240 AND G>=240 AND B>=240)`.
   ⚠ **The `alpha >= 32` cutoff is load-bearing** — a source can carry thousands of pixels at alpha 1–31 (a
   faint glow) that otherwise inflate the box to the whole canvas, and the mark then renders tiny inside its
   own square. The near-white test is defence in depth for a source that is not actually transparent.
3. Pad by 5% of the larger dimension, centre on a transparent **square** canvas.
4. `HighQualityBicubic` to 256, with high-quality smoothing / pixel-offset / compositing.

### The OS icon — `Masters/icon-source.png` → `EmberTern.ico`

Opaque source. Background cut by **flood fill** → **tight crop, no pad** → square canvas → 6 sizes.

1. **Background colour = the median of the 1px border**, not a corner sample — a median is unaffected by a
   stray bright pixel on the frame.
2. **Cut the background by FLOOD FILL from the border**, never by a global colour threshold.
   ⚠⚠ **This is the one step that must not be simplified, and the reason is arithmetic.** The current source's
   background is `rgb(14,15,19)` while the artwork itself contains **pure black** — Chebyshev distance 19. A
   global "remove pixels near the background colour" therefore punches holes straight through the cylinder,
   and a flood fill with a tolerance **at or above 19** walks through those black pixels into the interior and
   eats a wedge out of the middle of the logo. Both were observed. **Tolerance must sit above the background's
   own noise (±4 here) and strictly below the distance to the artwork's darkest pixel; 12 was used.** Measure
   both numbers for a new source rather than reusing 12.
3. **Feather only the 1px rim** that touches the removed region, scaling alpha by the pixel's distance from
   the background colour. Feathering globally would make the artwork's own near-black interior semi-transparent
   — the same trap as step 2, one level subtler.
4. **Crop tight to the content and centre on a square canvas with NO padding**, so the mark fills the icon
   slot to the edge on its longer axis. An icon is drawn small in dense chrome; the empty margin that flatters
   a 128px presentation just makes a 16px icon look shrunken.
5. Render 16/24/32/48/64/256 with `HighQualityBicubic`, then assemble the `.ico`.
6. Assemble by hand: a 6-byte `ICONDIR`, then N × 16-byte `ICONDIRENTRY`, then the concatenated PNG payloads.
   ⚠ The width and height bytes are **0** for the 256px entry — that is the .ico spec's encoding of 256, and
   writing 256 there produces a file Windows silently ignores.

⚠ Verify the cut-out by compositing it over **white and magenta**, not over the dark background it came from:
a leak, a hole or a residual dark halo is invisible against near-black and obvious against those two.

### ⚠⚠ Verifying the `.ico` — two GDI+ traps that make a GOOD file look broken

Both were hit while adopting the current artwork, and both are properties of the *inspection tool*, not of
the file. The previous, known-good icon reproduces both identically — **which is the check to run before
concluding anything: compare against the shipped file rather than against your expectations.**

- **`System.Drawing.Icon.ToBitmap()` returns colour noise** for a PNG-compressed entry. It decodes the frame
  as a DIB, so the PNG bytes are read as raw pixels. A magnified strip built this way looks like static at
  every size and reads as a catastrophically corrupt icon.
- **`new Icon(path, new Size(256,256))` hands back the 64px frame.** GDI+ does not select PNG-compressed
  256px entries at all, so this looks like the largest entry is missing or malformed.

**Inspect the payloads directly instead** — walk the `ICONDIRENTRY` table, slice each frame by its offset and
length, and load it with `Image.FromStream`. A correct file then shows, for every entry: the declared size
equal to the decoded size, an `0x89 0x50` PNG signature at the offset, and `Format32bppArgb`. That is the
assertion worth making; neither of the two APIs above can express it.

Note also that neither trap affects the application: Avalonia decodes the icon with Skia, and Windows'
shell decodes it itself. Only the .NET `System.Drawing` inspection path is affected.

Both pipelines were written in-session as PowerShell + `System.Drawing` (no Python on this machine).
