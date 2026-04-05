# Traydio App

Traydio now runs as a tray-first internet radio app with DI and LibVLC playback.

## Behavior

- Starts with no main window.
- Creates a tray icon with controls for play/pause, station selection, volume, and exit.
- Opens the station manager window only when requested from tray menu.
- Persists stations/settings to `%LocalAppData%\\Traydio\\settings.json`.

## Command System

Commands are represented by `AppCommand` + `AppCommandKind` and dispatched via `IAppCommandDispatcher`.

A text parser (`ICommandTextRouter`) is included so future integrations (for example Stream Deck plugin, named pipe, or protocol handler) can call into the same command pipeline.

## Inter-process Communication

- Traydio runs as a single-instance app using a mutex gate service.
- Secondary launches act as temporary relay instances that forward command text to the main instance and then exit.
- Named pipe transport is implemented via services (`ICommandRelayClient` / `ICommandRelayServer`).
- Communication methods are extensible by adding additional relay client/server implementations and registering them in DI.

Examples:

- Launching the app a second time (no args) relays `open` to the running instance.
- Launching with `--cmd "pause"` relays a pause command.

## Persistence

Settings use `System.Text.Json` source generation via `RadioSettingsJsonContext`.

