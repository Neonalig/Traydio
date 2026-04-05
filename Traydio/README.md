# Traydio App

Traydio now runs as a tray-first internet radio app with DI and LibVLC playback.

## Behavior

- Starts with no main window.
- Creates a tray icon with controls for play/pause, station selection, volume, and exit.
- Opens the station manager window only when requested from tray menu.
- Includes a dedicated station search window for provider-based discovery.
- Persists stations/settings to `%LocalAppData%\\Traydio\\settings.json`.

## Station Discovery Plugins

- Built-in provider plugins ship as external class libraries:
  - `Traydio.Plugin.FmStreamOrg`
  - `Traydio.Plugin.StreamUrlLink`
- Providers implement `IRadioStationProviderPlugin` from `Traydio.Common`.
- Runtime plugin management is supported:
  - Add plugin by DLL path from the search window.
  - Remove plugin at runtime (disabled in settings).
  - Plugin folder changes are detected while the app is running.
- Search window allows query/filter and adding discovered stations directly to your local station list.

## Command System

Commands are represented by `AppCommand` + `AppCommandKind` and dispatched via `IAppCommandDispatcher`.

A text parser (`ICommandTextRouter`) is included so future integrations (for example Stream Deck plugin, named pipe, or protocol handler) can call into the same command pipeline.

## Inter-process Communication

- Traydio runs as a single-instance app using a mutex gate service.
- Secondary launches act as temporary relay instances that forward command text to the main instance and then exit.
- Named pipe and loopback transports are implemented via services (`ICommandRelayClient` / `ICommandRelayServer`).
- Protocol URL startup bridge is implemented via `IStartupCommandBridge`.
- Communication methods are extensible by adding additional bridge implementations and registering them in DI.

Examples:

- Launching the app a second time (no args) relays `open` to the running instance.
- Launching with `--cmd "pause"` relays a pause command.
- Launching with `traydio://station/{id}` relays a station-switch command when protocol bridge is enabled.

## Persistence

Settings use `System.Text.Json` source generation via `RadioSettingsJsonContext`.

`settings.json` includes `Communication` options:

- `EnableNamedPipeRelay`
- `EnableLoopbackRelay`
- `LoopbackHost`
- `LoopbackPort`
- `EnableProtocolUrlRelay`
- `ProtocolScheme`

`settings.json` also includes station discovery plugin settings (`StationDiscoveryPlugins`) for plugin directory and disabled provider IDs.

The station manager window includes a Communication Settings section where you can save bridge options and install/uninstall the protocol URL handler for the configured scheme.
