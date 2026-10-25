# Changelog

## 1.1.0

- Add an opt-in automatic restart session for 造梦盒子: after a safe primary-macro boundary, request normal close, launch the remembered executable, restore the locked physical layout, wait for startup, run one preparation macro, and continue remaining primary iterations.
- Keep restart state local to the target settings; default to disabled, require a distinct preparation macro and a verified executable path, and preserve the selected input channel.
- Add exact launched-PID binding, process-start identity checks, one bounded retry for a process that exits before showing its window, and safe failure paths that never force-kill or click unknown dialogs.
- Fix the crash when marking a target's background capability as passed or unsupported (profiles are now updated by id instead of a value comparison that could no longer match).
- Keep record and playback sessions mutually exclusive even when triggered from global hotkeys: F8 during playback and F9 during recording are ignored with a status message instead of starting a conflicting session.
- Treat damaged local JSON as recoverable: a malformed `settings.json` shortcut no longer aborts startup, and a damaged legacy `profiles.json` falls back in memory without overwriting the file.
- Skip macro files whose schema version is newer than this build supports and list them as skipped, instead of replaying data the program does not understand.
- Log unexpected UI-thread exceptions to `%LOCALAPPDATA%\ZmboxZmx4Assist\crash.log`, release held keys/buttons, and keep the session running.
- Replay clicks on the right monitor: absolute mouse coordinates are now normalised against the whole virtual desktop instead of only the primary screen.
- Send arrows, Home/End and right-hand modifiers with the extended-key flag, give posted key messages real scan codes, and post wheel messages with screen-space coordinates.
- Raise the system timer resolution only while playback runs and resolve the virtual desktop once per round, so dense macros no longer drift to the default ~15.6 ms timer tick.
- Split the main window into per-feature partial files (recording, playback, restart session, target window, macros, hotkeys); behaviour is unchanged, but new behaviour now lands in its feature file instead of one 850-line class.
- Add a single version source (`Directory.Build.props`) and `scripts/publish.ps1`, which stamps that version into the assembly and the self-contained win-x64 archive name; release builds no longer depend on a manually remembered number.
- Add a tag-triggered release-build workflow: a `v*` tag must match `Directory.Build.props`, the suite runs, and the self-contained zip is uploaded as a workflow artifact. Publishing a GitHub Release stays a separate, explicit authorization.

## 1.0.4

- Rename user-visible application, documentation, and release labels to 造梦盒子·造梦西游四辅助工具.
- Keep the repository slug, executable name, local-data path, and internal namespaces unchanged for compatibility.

## 1.0.3

- Correct the GPL-3.0-only license file so GitHub can recognize it.

## 1.0.2

- Chinese is the default application language and UI culture.
- Enforce formatting verification and warnings-as-errors in CI.
- Ship the complete GPL-3.0-only license text with source and release packages.

## 1.0.0

- First public source release of 造梦盒子·造梦西游四辅助工具.
- Local recording and playback for 造梦盒子《造梦西游四》.
- Front-ground system input, optional ordinary background window messages,
  configurable hotkeys, loop cooling, and safe locked-window recovery.

## Compatibility

- Windows 10 22H2 and Windows 11.
- Background window messages depend on the launcher accepting ordinary Windows
  messages; they are not guaranteed and never use injection or bypass methods.
