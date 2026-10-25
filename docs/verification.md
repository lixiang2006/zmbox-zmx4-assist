# Verification checklist

## Automated

- `dotnet test ZmboxZmx4Assist.sln` verifies click normalization, drag preservation, timeline validation, macro/profile persistence, rename stability, damaged-macro isolation, safe-write cleanup, configured-hotkey filtering, X1/X2 translation, virtual-desktop mouse coordinate mapping, extended-key/wheel message encoding, corrupted-settings fallback to default hotkeys, corrupted legacy-profile fallback without overwriting the file, rejection of macros written by a newer schema version, mouse-move sampling thresholds, the playback dispatch sequence behind the input seam, the target-state monitor's throttled readiness decisions (reused full check inside the interval, immediate pause on a dead handle or lost foreground, no caching of a failed check, revalidation after the interval or on a new handle), the playback clock's overshoot compensation threshold and microsecond-to-tick conversion, an emergency stop that arrives while the timeline is already shutting down staying harmless, integrity-level band mapping plus the background-channel rejection message, and the compact schema round-tripping while still reading the older object form.
- `dotnet build ZmboxZmx4Assist.sln` verifies the WPF app and test project compile without warnings.

## Manual acceptance before game use

1. Record a two-second `W` hold in a disposable test situation; replay it five times and compare the observed travel distance.
2. Record a single click with a small hand tremor; confirm replay clicks rather than drags the map. Record a deliberate drag and confirm it remains a drag.
3. Press F12 while a key is held; confirm the key is released and no next loop begins.
4. Move/resize the launcher or switch focus during playback; confirm playback pauses within roughly 0.1 s and no further events are sent after the pause is reported.
5. Only after a harmless manual test with 360 in the background should the profile be marked as supporting experimental background mode.
6. Double-click a macro name, press Esc to cancel, then rename it with Enter; reopen the app and confirm its ID, event count, and target profile are unchanged.
7. Make a disposable malformed JSON file under `%LOCALAPPDATA%\GameMacro\macros`; confirm its filename is reported while normal macros still load, and delete that test file manually afterward.
8. Change record/play hotkeys to two unused keys; confirm they control the recorder but are absent from the saved macro, and confirm a collision reports an error instead of silently losing F12 protection.
9. Replay a dense macro for several minutes while scrolling or clicking elsewhere in the app window; confirm the window keeps redrawing and that no burst of back-to-back events follows a visible stall.
