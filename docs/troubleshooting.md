# Troubleshooting

## The countdown rejects my window

Click the Zmbox window before the countdown reaches zero. The process must be named `造梦盒子` and its title must contain “造梦盒子”; a browser, launcher overlay, or another game window is deliberately rejected.

## Playback stops after a layout change

Keep the recorded display DPI and resolution. Position and size may vary by up to eight physical pixels; beyond that the app pauses and tries to recover for five seconds before safely stopping.

## Background playback is rejected or has no effect

Background playback posts ordinary window messages instead of injecting system input, so Windows applies integrity-based access control (UIPI): a process running at a *lower* integrity level than the game cannot post messages to its window. A common cause is that the program was started from a folder that carries a `Low Mandatory Level` label — the exe inherits the label, and the process then always runs at low integrity, even when started by an administrator.

Before a background playback starts, the application probes the target window once. When Windows refuses the post, it reports both integrity levels plus the exact command to fix it. To relabel the files, run in a terminal (adjust the folder):

    icacls "<program folder>" /setintegritylevel (OI)(CI)Medium

then restart the application — a running process keeps the integrity level it started with. Running the application as administrator also works, because a high-integrity process may post to lower ones. `scripts/publish.ps1` resets the published folder and zip to medium integrity, so a release package built from a low-integrity folder cannot make background playback fail for everyone.

Some games also ignore posted messages outright; if relabelling and a restart still leave background playback without effect, use the foreground channel for that game — foreground playback steals the keyboard and mouse, which is why the background channel exists in the first place.

## Hotkey recording starts while a macro is replaying

F8 and F9 are deliberately ignored while a playback is running, and playback is rejected while a recording is running, so a hotkey cannot start a conflicting session. Stop the current session with F12 first.

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

A damaged `settings.json` (for example a shortcut written as a string instead of a number) is ignored and the default F8/F9/F12 hotkeys are used; a damaged `target.json` falls back to defaults in memory and is rewritten the next time target settings are saved. A legacy `%LOCALAPPDATA%\GameMacro\profiles.json` is only read once, when legacy data is migrated, and is never written by this program. Repair or delete the affected file and restart the application.
