# Changelog

## 1.1.0

- Add an opt-in automatic restart session for 造梦盒子: after a safe primary-macro boundary, request normal close, launch the remembered executable, restore the locked physical layout, wait for startup, run one preparation macro, and continue remaining primary iterations.
- Keep restart state local to the target settings; default to disabled, require a distinct preparation macro and a verified executable path, and preserve the selected input channel.
- Add exact launched-PID binding, process-start identity checks, one bounded retry for a process that exits before showing its window, and safe failure paths that never force-kill or click unknown dialogs.

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
