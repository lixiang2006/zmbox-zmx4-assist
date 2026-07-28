# Verification checklist

## Automated

- `dotnet test ZmboxZmx4Assist.sln` verifies click normalization, drag preservation, timeline validation, macro/profile persistence, rename stability, damaged-macro isolation, safe-write cleanup, configured-hotkey filtering, and X1/X2 translation.
- `dotnet build ZmboxZmx4Assist.sln` verifies the WPF app and test project compile without warnings.

## Manual acceptance before game use

1. Record a two-second `W` hold in a disposable test situation; replay it five times and compare the observed travel distance.
2. Record a single click with a small hand tremor; confirm replay clicks rather than drags the map. Record a deliberate drag and confirm it remains a drag.
3. Press F12 while a key is held; confirm the key is released and no next loop begins.
4. Move/resize the launcher or switch focus during playback; confirm playback stops before a new event is sent.
5. Only after a harmless manual test with 360 in the background should the profile be marked as supporting experimental background mode.
6. Double-click a macro name, press Esc to cancel, then rename it with Enter; reopen the app and confirm its ID, event count, and target profile are unchanged.
7. Make a disposable malformed JSON file under `%LOCALAPPDATA%\GameMacro\macros`; confirm its filename is reported while normal macros still load, and delete that test file manually afterward.
8. Change record/play hotkeys to two unused keys; confirm they control the recorder but are absent from the saved macro, and confirm a collision reports an error instead of silently losing F12 protection.
