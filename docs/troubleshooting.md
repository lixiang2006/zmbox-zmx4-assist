# Troubleshooting

## The countdown rejects my window

Click the Zmbox window before the countdown reaches zero. The process must be named `造梦盒子` and its title must contain “造梦盒子”; a browser, launcher overlay, or another game window is deliberately rejected.

## Playback stops after a layout change

Keep the recorded display DPI and resolution. Position and size may vary by up to eight physical pixels; beyond that the app pauses and tries to recover for five seconds before safely stopping.

## Background playback has no effect

Use foreground system input. The background option is an ordinary Windows message experiment and is not a compatibility promise.

## Automatic restart session will not start

It is intentionally disabled unless you choose a different, valid preparation
macro and the tool has remembered an existing absolute `造梦盒子.exe` path from a
locked target window. The preparation macro and primary macro must have
compatible display/DPI/window layouts. If normal close does not finish within
30 seconds, handle the game's own confirmation dialog yourself; the tool does
not force-close it or click an unknown dialog.

## Restarted Zmbox does not resume the macro

The tool only binds the PID it started itself. It waits up to 120 seconds for a
visible Zmbox window, restores the original physical position and size only
when display resolution/DPI are unchanged, then runs the preparation macro
once. A failure in any of those checks stops safely and leaves input released.

## Old macros are missing

Check `%LOCALAPPDATA%\GameMacro\macros` and `%LOCALAPPDATA%\ZmboxZmx4Assist\macros`. The first launch copies legacy data; it never deletes the old files. A malformed JSON macro is left in place and reported by the application.

## An unexpected-error dialog appears

The application logs unexpected UI-thread exceptions to `%LOCALAPPDATA%\ZmboxZmx4Assist\crash.log`, releases any held keys/buttons, and keeps running. Attach that file to a bug report.

A damaged `settings.json` (for example a shortcut written as a string instead of a number) is ignored and the default F8/F9/F12 hotkeys are used; a damaged legacy `profiles.json` is left untouched and the single supported target is used instead. Repair or delete the affected file and restart the application.
