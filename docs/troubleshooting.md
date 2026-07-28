# Troubleshooting

## The countdown rejects my window

Click the Zmbox window before the countdown reaches zero. The process must be named `造梦盒子` and its title must contain “造梦盒子”; a browser, launcher overlay, or another game window is deliberately rejected.

## Playback stops after a layout change

Keep the recorded display DPI and resolution. Position and size may vary by up to eight physical pixels; beyond that the app pauses and tries to recover for five seconds before safely stopping.

## Background playback has no effect

Use foreground system input. The background option is an ordinary Windows message experiment and is not a compatibility promise.

## Old macros are missing

Check `%LOCALAPPDATA%\GameMacro\macros` and `%LOCALAPPDATA%\ZmboxZmx4Assist\macros`. The first launch copies legacy data; it never deletes the old files. A malformed JSON macro is left in place and reported by the application.
