The prompt box (`SlashInputReader.cs`) originally scrolled horizontally on long input, cutting off
earlier characters. Reworked to wrap instead, and to accept real line breaks.

## Auto-wrap

Text wraps across up to 6 rows (`MaxInputLines`) inside the box instead of scrolling sideways. Past
that cap it falls back to scrolling vertically (anchored to the cursor), the same idea as the old
horizontal scroll just rotated 90°. `BuildRows` splits the buffer on explicit `\n` breaks and then
further wraps each line to the available width, producing one flat list of `(start, length)` row
slices; hard breaks and soft (width-driven) breaks share the same list so cursor placement
(`LocateCursor`) doesn't need to special-case which kind of break it's near.

## Real line breaks: paste vs manual

The buffer can hold literal `\n` characters, inserted two ways:

- **Multi-line paste** - a terminal delivers a paste as a burst of synthetic keystrokes, including
  one `Enter` key event per line break in the clipboard text. There's no built-in way to tell "the
  user pressed Enter" from "the clipboard contained a newline" other than: a real keypress has
  nothing queued up right behind it, a paste does. Detected via `Console.KeyAvailable` being `true`
  immediately after the `Enter` event.
- **Shift+Enter / Alt+Enter** - the deliberate manual equivalent, for typing a line break outright
  without pasting.

A plain, unmodified `Enter` with nothing queued behind it still submits, as before.

## Border rendering bug (found and fixed)

After simplifying the box to plain top/bottom horizontal lines (no corners, no side borders), a
stray `╯` character stayed stuck near the bottom of the screen. Root cause was two bugs stacking:

1. `ClearRow` deliberately blanks only `WindowWidth - 1` columns (avoiding the terminal's last
   column, to sidestep auto-wrap-on-last-cell quirks) - so it can never clear whatever was drawn
   *in* that last column.
2. The box's initial row-reservation code still called the *old* cornered-box drawing helpers
   (`ConsoleTheme.WriteBoxTop`/`WriteBoxBottom`), whose corner glyphs land exactly on that last
   column. `Render()` overwrites almost everything on its first call, but never that one column -
   so the leftover corner from the very first reservation write was never cleared again for the
   rest of the session.

Fixed by reserving rows as plain blank lines (nothing corner-shaped ever gets written there), and
keeping all drawn borders one column short of the edge (`width - 1`), consistent with what
`ClearRow` actually reaches.

## Related

[[Index]] · [[Architecture]]
