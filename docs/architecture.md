# Architecture

`Presentation` is the WPF shell. `Services` coordinate recording, target
validation, persistence, playback, tray notifications, lock highlighting, and
the optional restart session. `Interop` is the sole Win32/DWM/SendInput
boundary. `Domain` contains persisted models and pure state. The application
only accepts a foreground window whose process name and title identify 造梦盒子;
no launcher code or game resource is loaded, modified, injected, or
redistributed.

The shell is split by feature: `MainWindow.xaml.cs` holds only service
composition, shared window state and the status/control-refresh helpers, while
`MainWindow.Recording.cs`, `MainWindow.Playback.cs`,
`MainWindow.RestartSession.cs`, `MainWindow.TargetWindow.cs`,
`MainWindow.Macros.cs` and `MainWindow.Hotkeys.cs` carry one feature each.
Adding behaviour to a feature belongs in its own partial file, not in the core.

Playback talks to the desktop through two narrow seams: `IInputSink` delivers
one recorded event (`NativeInputSink` for Windows, a recording sink in tests)
and `IPlaybackTargetGuard` answers the readiness question (`TargetWindowGuard`
for the live window). The golden playback tests drive the real timeline through
both, so the dispatch sequence is pinned without touching a desktop.

`RestartSessionService` owns one cancellation token and orchestrates a primary
macro as discrete safe iterations. At a configured session-uptime boundary it
delegates normal close, exact-PID launch/window binding, layout restore and a
single preparation macro to narrow interfaces. `PlaybackService` remains the
only input dispatcher and exposes a one-iteration operation for that purpose;
the orchestrator never recursively starts its normal loop. `ZmboxProcessLifecycle`
never calls a force-termination API and never searches for arbitrary same-named
windows after restarting.
