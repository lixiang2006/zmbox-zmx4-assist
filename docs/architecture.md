# Architecture

`Presentation` is the WPF shell. `Services` coordinate recording, target
validation, persistence, playback, tray notifications, lock highlighting, and
the optional restart session. `Interop` is the sole Win32/DWM/SendInput
boundary. `Domain` contains persisted models and pure state. The application
only accepts a foreground window whose process name and title identify 造梦盒子;
no launcher code or game resource is loaded, modified, injected, or
redistributed.

There is exactly one supported target, described by `ZmboxTargetSettings` and
stored in `target.json`; macros carry its fixed `TargetProfileId` so a macro
recorded against a different target is still recognisable. Legacy multi-profile
data is only a migration source.

All WPF windows live under `src/ZmboxZmx4Assist/Views/`, and the macro list item
view-model under `ViewModels/`. The shell is split by feature: `MainWindow.xaml.cs`
holds only service composition, shared window state and the
status/control-refresh helpers, while `MainWindow.Recording.cs`,
`MainWindow.Playback.cs`, `MainWindow.RestartSession.cs`,
`MainWindow.TargetWindow.cs`, `MainWindow.Macros.cs` and
`MainWindow.Hotkeys.cs` carry one feature each. Adding behaviour to a feature
belongs in its own partial file, not in the core.

Playback talks to the desktop through two narrow seams: `IInputSink` delivers
one recorded event (`NativeInputSink` for Windows, a recording sink in tests)
and `IPlaybackTargetGuard` answers the readiness question (`TargetWindowGuard`
for the live window). In production that guard is wrapped by
`TargetStateMonitor`, which keeps the expensive full answer — process identity,
layout, DPI, foreground — for one 100 ms interval and answers the per-event
question from a cheap handle/foreground probe; a dead handle, a lost foreground
or a background binding that no longer matches the target still pauses
immediately. The golden playback tests drive the real timeline through both
seams, so the dispatch sequence is pinned without touching a desktop.

The playback timeline runs on a pool thread, not the Dispatcher: the final
milliseconds of every gap are a busy spin and each event ends in an input call,
so keeping them on the UI thread starved the window's own rendering and input
handling. Status and state events marshal themselves back. Waiting is a hybrid
sleep/spin, and when a wake-up overshoots the spin margin — a GC pause, a busy
machine, a slow frame — the timeline reference absorbs that overshoot, so the
remaining recorded intervals stay intact instead of collapsing into a burst.

`RestartSessionService` owns one cancellation token and orchestrates a primary
macro as discrete safe iterations. At a configured session-uptime boundary it
delegates normal close, exact-PID launch/window binding, layout restore and a
single preparation macro to narrow interfaces. `PlaybackService` remains the
only input dispatcher and exposes a one-iteration operation for that purpose;
the orchestrator never recursively starts its normal loop. `ZmboxProcessLifecycle`
never calls a force-termination API and never searches for arbitrary same-named
windows after restarting.
