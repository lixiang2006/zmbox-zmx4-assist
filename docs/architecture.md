# Architecture

`Presentation` is the WPF shell. `Services` coordinate recording, target
validation, persistence, playback, tray notifications, and lock highlighting.
`Interop` is the sole Win32/DWM/SendInput boundary. `Domain` contains persisted
models and pure state. The application only accepts a foreground window whose
process name and title identify 造梦盒子; no launcher code or game resource is
loaded, modified, injected, or redistributed.
