# EmberTern License Manager — branding assets

The License Manager's **own** OS icon, and the complete list of places that consume it.

Sibling document: [`../../../EmberTern.App/Assets/Branding/BRANDING.md`](../../../EmberTern.App/Assets/Branding/BRANDING.md)
— the PRODUCT's identity. **Read it first**: the pipeline below is the one it documents, run for a second
source, and everything it says about verifying a `.ico` applies here unchanged.

---

## ⭐⭐ Why this application has an icon of its own

Until 2026-08-22 it did not. It referenced `EmberTern.ico` across the project boundary, on the reasoning
that the License Manager is the same product's admin side rather than a second brand.

**That reasoning was sound and the result was wrong.** Two icons that are byte-identical are two
applications the operator cannot tell apart — in the taskbar, in Alt+Tab, in Explorer, in a pinned
shortcut. And these two sit open side by side doing very different things, one of them holding the
signing key. The user asked for the change after living with it.

⭐ **Same family, distinct mark.** The shield carries the ember wing the product's icon carries; the
padlock is what says which of the two applications this is.
⛔ **The product's icon is untouched**, and `LicenseManagerThemeTests` now fails the build if this project
can reach it again, or if the two files ever become byte-identical.

---

## The files

| File | Ships? | What it is | Rendered from |
|---|---|---|---|
| `Masters/license-manager-icon-source.png` | **no** | Source artwork, 1254×1254, **opaque near-black background**. | — |
| `EmberTernLicenseManager.ico` | yes | The **OS icon**. 16/24/32/48/64/128/256, every entry 32-bit RGBA PNG-compressed. | the master |

⚠ `Masters/` is removed from `AvaloniaResource` **as a folder** in the csproj, so a new master dropped in
there never ships by accident. ⛔ Never reference a master from XAML: it is un-cropped and its background
is opaque, so it renders as a black square.

⚠ There is **no UI mark here**. The About window shows the PRODUCT's `EmberTern_logo.png`, which is
deliberate — the About window is about EmberTern, and the License Manager is not a separate product.

## Where it is consumed — the complete list

1. **`EmberTern.LicenseManager.csproj` → `<ApplicationIcon>`**
   Embeds the icon in `EmberTern.LicenseManager.exe`. Explorer, the file's properties, a shortcut, and the
   taskbar button *before* a window exists. A build-time Win32 embed — Avalonia never sees it.

2. **`Themes/LicenseManagerStyles.axaml` → `<Style Selector="Window">` → `Icon`**
   ⭐ **The one runtime owner.** `Window.Icon` defaults to `null`, which the OS draws as a blank slot in the
   title bar and in Alt+Tab. One setter reaches every window *and every window added later*.
   ⛔ **Do not set `Icon` on an individual window** — a local value outranks a style setter, so it would
   quietly create a second source of truth for exactly one window.

Both are needed and neither replaces the other.

---

## Regenerating it

The procedure is the product's **"The OS icon"** pipeline, unchanged in shape:
opaque source → background cut by **flood fill** from the border → 1 px rim feathered → **tight crop, no
pad** → square canvas → the shipped sizes → a hand-assembled `.ico`.

Two numbers differ from the product's run, and both were **measured for this source** rather than reused.

### ⚠⚠ The flood-fill tolerance here is **4**, not 12

| | product source | this source |
|---|---|---|
| background median | `rgb(14,15,19)` | **`rgb(12,13,15)`** |
| border noise (max Chebyshev) | ±4 | **4** |
| distance to the artwork's darkest pixel | 19 | **5** |
| tolerance used | 12 | **4** |

The rule is the product doc's: **above the background's own noise, strictly below the distance to the
artwork's darkest pixel.** Here that window is 4–5, because this artwork's shield interior is near-black
almost to the background's own value. ⛔ **Copying 12 from the product's run floods straight through the
shield and eats the middle out of the mark.** Measure both numbers again for any new source.

### ⚠⚠ Isolated specks must be dropped before the bounding box is taken

This source carries a handful of **single pixels** of compression noise that sit just outside the
tolerance. They are invisible and they **inflate the bounding box** — measured, to rows 2–1210 and columns
88–1200, when the mark itself occupies 131–1136 × 195–1044. Cropping to that box wastes ~30 % of the icon
on nothing, which is exactly the shrunken-icon failure the tight crop exists to prevent.

⭐ Remove them by **connected component** (anything under ~100 px that is not attached to the mark), ⛔ never
by a row/column density threshold: a threshold also clips the shield's own apex, which is a few pixels wide
at its topmost row. A speck is small **and disconnected**; the apex is small and attached to everything.

This is the same failure the product doc records one level over for the About mark, where a faint glow at
alpha 1–31 inflates the box — different cause, identical consequence.

### Verifying

⚠ Composite the cut-out over **white and magenta**, never over the dark it came from: a leak, a hole or a
residual dark halo is invisible against near-black and obvious against those two.

⚠⚠ To inspect the `.ico`, **walk the `ICONDIRENTRY` table and read the payloads** — the product doc's two
GDI+ traps (`Icon.ToBitmap()` returning colour noise, `new Icon(path, new Size(256,256))` handing back the
64 px frame) make a perfectly good file look broken. `EveryIconEntryIsAWellFormedFrameOfItsDeclaredSize`
does exactly this and needs no image decoder at all.

⭐ The 256 px entry declares its width and height as **0** — the spec's encoding of 256. Writing 256 there
produces a file Windows silently ignores.

Then look at it at the size it is judged at: **16 and 32 px, against a real taskbar**, beside the product's
icon. The question is not "is it pretty" but "can I tell these two apart at a glance".
