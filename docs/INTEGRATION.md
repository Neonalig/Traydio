# Traydio Integration Spec

This document is the integration-facing feature spec for controlling a running Traydio instance from external tooling.

**Primary audience:**

- Stream Deck plugin authors
- Script/tool developers
- Launcher/hotkey automation integrations

## Scope

This spec describes:

- Plain-text command grammar routed by `ICommandTextRouter`
- Startup command bridges (`--cmd`, protocol URL)
- Relay transports used between secondary and primary instances

## Command text protocol

Command text is whitespace-separated and case-insensitive for the command keyword.

### Supported commands

- `play`
- `pause`
- `toggle`
- `open`
- `search`
- `plugins`
- `settings`
- `volup`
- `voldown`
- `volume <0-100>`
- `station <stationId>`

![Commands window example](docs/images/commands.png)

### Semantics

- `play` - play active station, or first station if no active station is set.
- `pause` - pause playback.
- `toggle` - toggle play/pause.
- `open` - open station manager window.
- `search` - open station search window.
- `plugins` - open plugin manager window.
- `settings` - open settings window.
- `volup` - increase volume by 5.
- `voldown` - decrease volume by 5.
- `volume <n>` - set volume to `n` (clamped by app to 0-100).
- `station <stationId>` - switch to/play a specific station by stored station id.

Unknown commands are ignored and return a failed dispatch (`false`) at the router layer.

## Startup command bridges

Traydio resolves startup commands through `IStartupCommandBridge` implementations.

Only a single instance of the app is allowed to run at a time (enforced by `IInstanceGate`), so startup command bridges are designed to relay command text to the primary instance when launched in a secondary instance context.

### Command-line bridge

Arguments:

- `--cmd "<command text>"`

Example:

```powershell
Traydio.exe --cmd "pause"
Traydio.exe --cmd "station 3f6e7f1d9f9a4b4f8d2d9f2f15b9a5c1"
```

### Protocol URL bridge

Enabled by communication settings (`EnableProtocolUrlRelay`) and scheme match (`ProtocolScheme`, default `traydio`).

Accepted forms:

- `traydio://play`
- `traydio://pause`
- `traydio://toggle`
- `traydio://open`
- `traydio://station/<stationId>`
- `traydio://volume/<0-100>`

Also accepted when passed explicitly as:

- `Traydio.exe --url "traydio://play"`

## Relay transports (secondary -> primary instance)

When another instance is launched and a primary instance already holds the instance gate, the secondary instance attempts to relay the command text to the primary and exits.

### Named pipe relay

- Client/server type: Windows named pipe
- Pipe name: `Traydio.CommandRelay.v1`
- Payload: single UTF-8 text line (`ReadLine`/`WriteLine`)
- Settings gate: `Communication.EnableNamedPipeRelay`

### Loopback TCP relay

- Client/server type: TCP socket
- Host: `Communication.LoopbackHost` (default `127.0.0.1`)
- Port: `Communication.LoopbackPort` (default `38473`)
- Payload: single UTF-8 text line (`ReadLine`/`WriteLine`)
- Settings gate: `Communication.EnableLoopbackRelay`

## Integration guidance

### Recommended strategy for external tools

1. Send command text via launching Traydio with `--cmd`.
2. Optionally use protocol URLs for clickable links and URI handlers.
3. Keep commands short and single-line.
4. For station control, persist station IDs from Traydio settings and pass `station <id>`.

### Reliability notes

- Relay is best effort; tools should tolerate failures and retry user actions when needed.
- If both relay mechanisms are disabled in settings, external command relay will fail.
- If protocol handling is disabled or unregistered, URI launch-based control will fail.

## Configuration surface

Communication settings are persisted in `%LocalAppData%\Traydio\settings.json` under `Communication`:

- `EnableNamedPipeRelay`
- `EnableLoopbackRelay`
- `LoopbackHost`
- `LoopbackPort`
- `EnableProtocolUrlRelay`
- `ProtocolScheme`

![Example settings.json snippet](docs/images/settings-json-communication.png)

## Implementation references

- Command parsing: `Traydio/Commands/CommandTextRouter.cs`
- Command kind definitions: `Traydio/Commands/AppCommandKind.cs`
- Command dispatching: `Traydio/Commands/AppCommandDispatcher.cs`
- Command-line bridge: `Traydio/Services/Implementations/CommandLineStartupCommandBridge.cs`
- Protocol bridge: `Traydio/Services/Implementations/ProtocolUrlStartupCommandBridge.cs`
- Named pipe relay: `Traydio/Services/Implementations/NamedPipeCommandRelayClient.cs`, `Traydio/Services/Implementations/NamedPipeCommandRelayServer.cs`
- Loopback relay: `Traydio/Services/Implementations/LoopbackCommandRelayClient.cs`, `Traydio/Services/Implementations/LoopbackCommandRelayServer.cs`

