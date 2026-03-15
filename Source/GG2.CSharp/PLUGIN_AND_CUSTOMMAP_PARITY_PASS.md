## Goal

Make `GG2.CSharp` a transition-ready replacement for classic GG2 in two areas that are still materially behind source expectations:

1. Server-side extensibility/plugins.
2. Custom map parity, including legacy PNG map behavior and distribution workflow.

This pass is intentionally scoped around source compatibility and team workflow continuity, not original network interoperability.

## Source Review Summary

### Legacy server/admin/plugin signals

- The original source explicitly calls out `AdminMenu` as the place to add "additional plugins" in `Source/gg2/Objects/InGameElements/PlayerControl.events/Key space pressed.xml`.
- Legacy server management behavior is spread across:
  - `Source/gg2/Scripts/GameServer/GameServerCreate.gml`
  - `Source/gg2/Scripts/GameServer/GameServerBeginStep.gml`
  - `Source/gg2/Objects/Overlays/Admin_Menus/*`
- This is not a modern formal plugin API. It is an admin/server extension surface layered onto the host.

### Legacy custom map behavior

- The original custom map pipeline is centered around:
  - `Source/gg2/Scripts/CustomMaps/CustomMapInit.gml`
  - `Source/gg2/Scripts/CustomMaps/CustomMapProcessLevelData.gml`
  - `Source/gg2/Scripts/CustomMaps/CustomMapCreateEntitiesFromEntityData.gml`
  - `Source/gg2/Scripts/CustomMaps/CustomMapDownload.gml`
  - `Source/gg2/Scripts/CustomMaps/CustomMapGetMapMD5.gml`
  - `Source/gg2/Scripts/CustomMaps/CustomMapGetMapURL.gml`
- The current C# equivalents are primarily:
  - `Source/GG2.CSharp/src/GG2.Core/CustomMapPngImporter.cs`
  - `Source/GG2.CSharp/src/GG2.Core/SimpleLevelFactory.cs`
  - `Source/GG2.CSharp/src/GG2.Server/MapRotationManager.cs`
  - `Source/GG2.CSharp/src/GG2.Server/ServerHelpers.MapRotation.cs`
  - `Source/GG2.CSharp/src/GG2.Core/SimulationWorld.MapLifecycle.cs`

### Current C# insertion seams

- Server lifecycle and packet handling live in `Source/GG2.CSharp/src/GG2.Server/GameServer.cs`.
- Client/session auth and control commands live in `Source/GG2.CSharp/src/GG2.Server/ServerSessionManager.cs`.
- Admin command transport already exists via:
  - `Source/GG2.CSharp/src/GG2.Server/ServerConsoleCommandProcessor.cs`
  - `Source/GG2.CSharp/src/GG2.Server/HostedServerAdminPipeHost.cs`
- Map load/change flow lives in:
  - `Source/GG2.CSharp/src/GG2.Core/SimulationWorld.MapLifecycle.cs`
  - `Source/GG2.CSharp/src/GG2.Server/MapRotationManager.cs`

## Pass A: Server Plugin System

### Objective

Introduce a first-class server plugin system that replaces the ad hoc legacy admin/plugin extension model with a stable C# API, while preserving the kinds of customization classic GG2 server operators expect.

### Success Criteria

- Dedicated server can discover and load plugins from a `Plugins` directory.
- Plugins can register console/admin commands.
- Plugins can observe core server events without patching server internals.
- Plugins can influence common server behaviors through supported hooks.
- Hosted admin pipe and console commands can invoke plugin commands.
- Plugin failures are isolated and logged without crashing the server.

### Pass A1: Plugin Contract and Loader

- Add a new project:
  - `Source/GG2.CSharp/src/GG2.Server.Plugins.Abstractions`
- Add a runtime host inside `GG2.Server`:
  - `PluginLoader`
  - `PluginHost`
  - `PluginCommandRegistry`

#### Initial interfaces

- `IGg2ServerPlugin`
  - `string Id`
  - `string DisplayName`
  - `Version Version`
  - `void Initialize(IGg2ServerPluginContext context)`
  - `void Shutdown()`

- `IGg2ServerPluginContext`
  - logging
  - config directory
  - map directory
  - plugin command registration
  - readonly server state access
  - controlled admin operations

- `IGg2ServerCommand`
  - command name
  - help/usage
  - async execute method returning output lines

### Pass A2: Event Surface

Start with events that match actual legacy server-admin needs and are cheap to stabilize.

#### Required first-pass events

- Server lifecycle:
  - server starting
  - server started
  - server stopping
  - server stopped

- Client/session lifecycle:
  - hello received
  - client connected
  - password accepted
  - client disconnected

- Gameplay/admin lifecycle:
  - chat received
  - map changing
  - map changed
  - round ended
  - player team changed
  - player class changed

#### Hook points to add

- `GameServer.cs`
  - before/after hello accept
  - before/after chat broadcast
  - before/after map change application
  - before shutdown

- `ServerSessionManager.cs`
  - after password accepted
  - after remove client
  - after team/class/spectate control commands succeed

- `SimulationWorld.MapLifecycle.cs`
  - after `TryLoadLevel`
  - after `ApplyPendingMapChange`

### Pass A3: Command Integration

Current admin commands are hardcoded in `GameServer.BuildConsoleCommandResponse`.

#### First-pass change

- Refactor built-in commands behind a command registry.
- Built-ins become registered commands, not special cases.
- Plugin commands are merged into the same registry.
- `ServerConsoleCommandProcessor` remains the front door for stdin.
- `HostedServerAdminPipeHost` continues forwarding strings, but now all commands resolve through the registry.

### Pass A4: Controlled Mutation API

Do not expose raw `SimulationWorld` mutation as the initial plugin API.

Expose supported operations instead:

- kick/disconnect session
- move to spectator
- set team/class
- force map change
- reload rotation
- broadcast chat/system notice
- query active players, slots, endpoints, match state, current level

Add deeper hooks only after the first plugin wave is proven stable.

### Pass A5: Loading, Isolation, and Config

- Load plugins from:
  - `Plugins/*.dll`
- Each plugin gets:
  - `config/plugins/<plugin-id>/`
- Each plugin load is wrapped in error handling.
- Failed plugins are skipped with a clear server log entry.

### Pass A6: Tests

Add server tests covering:

- plugin discovery from `Plugins`
- command registration and dispatch
- plugin command visibility through admin pipe
- event dispatch for connect/chat/map-change
- plugin load failure isolation
- plugin shutdown during server stop

Recommended test files:

- `Source/GG2.CSharp/src/GG2.Server.Tests/PluginLoaderTests.cs`
- `Source/GG2.CSharp/src/GG2.Server.Tests/PluginCommandRegistryTests.cs`
- `Source/GG2.CSharp/src/GG2.Server.Tests/PluginEventDispatchTests.cs`

## Pass B: Custom Map Parity

### Objective

Make legacy custom PNG maps behave close enough to classic GG2 that the existing custom-map ecosystem can move over with minimal friction.

### Success Criteria

- Legacy custom map entity naming variants load correctly.
- Legacy gate/objective objects behave correctly when imported from PNG metadata.
- Server can announce enough custom map metadata for clients to obtain the map.
- Client can validate, download, and cache missing custom maps.
- Rotation and map-change flow support custom maps cleanly.

### Pass B1: Entity and Alias Coverage

Current `CustomMapPngImporter.TryCreateRoomObject` is too narrow compared with `CustomMapCreateEntitiesFromEntityData.gml`.

#### Add support for legacy entity names and aliases

- team gates
  - `redteamgate`
  - `blueteamgate`
  - `redteamgate2`
  - `blueteamgate2`

- intel gates
  - `redintelgate`
  - `blueintelgate`
  - `redintelgate2`
  - `blueintelgate2`
  - `intelgatehorizontal`
  - `intelgatevertical`

- cabinet alias compatibility
  - `medCabinet`
  - `cabinets`
  - `healingcabinet`

- wall aliases
  - `playerwall_horizontal`
  - `bulletwall_horizontal`
  - existing camel/case-insensitive variants

- objective aliases
  - `CapturePoint`
  - `SetupGate`
  - `ArenaControlPoint`
  - `NextAreaO`
  - `PreviousAreaO`
  - `GeneratorRed`
  - `GeneratorBlue`

#### Supporting code changes

- Extend `RoomObjectType` and `GameMakerRoomMetadataImporter` only where behavior requires new marker kinds.
- For `PreviousAreaO`, preserve metadata even if first-pass gameplay only needs it for compatibility bookkeeping.

### Pass B2: Parity Tests for Custom Map Entity Parsing

Add a new test fixture set with embedded sample custom-map leveldata strings or test PNGs.

Required coverage:

- all supported legacy aliases parse into expected room markers
- team/intel gates preserve team ownership
- CP/arena/generator markers detect the right mode
- `NextAreaO` and `PreviousAreaO` preserve area metadata

Recommended test files:

- `Source/GG2.CSharp/src/GG2.Core.Tests/CustomMapImporterTests.cs`
- `Source/GG2.CSharp/src/GG2.Core.Tests/CustomMapParityTests.cs`

### Pass B3: Custom Map Distribution Workflow

Classic GG2 uses URL + MD5 + locator files. The current port has no equivalent end-to-end flow.

#### Required runtime behavior

- Server includes custom map metadata when the selected map is external:
  - map name
  - source URL if known
  - content hash

- Client behavior:
  - if map exists locally and hash matches, load it
  - if map exists and hash differs, prompt or auto-replace based on policy
  - if map does not exist and URL is available, download it
  - store source URL in a locator file for future hosting

#### Suggested implementation

- Add a `CustomMapDescriptor` model in `GG2.Core`
  - level name
  - local file path
  - source URL
  - content hash

- Add a `CustomMapLocatorStore`
  - read/write `.locator`

- Add a `CustomMapHashService`
  - compute SHA-256 for new code
  - optionally preserve MD5 read/write compatibility for legacy locator semantics

- Add a `CustomMapDownloadService` in client code
  - safe temp download
  - MIME/content-length sanity checks
  - atomic replace

### Pass B4: Protocol Support for Custom Map Metadata

The port does not need original wire compatibility, but it does need enough protocol to carry custom map metadata.

#### Add to server/client protocol

- include custom map descriptor in `WelcomeMessage` and/or a dedicated map-info message:
  - current map name
  - is custom
  - map area
  - source URL
  - content hash

- on map change, send updated map descriptor

This keeps the protocol purpose-built without trying to mimic original GML packets.

### Pass B5: Map Rotation and Hosting Parity

The server and launcher should support external maps naturally.

#### Required work

- permit stock rotation plus external-map entries
- preserve locator metadata when hosting a downloaded map
- show external/custom maps distinctly in host UI
- keep current area progression behavior for multi-area maps

### Pass B6: Compatibility Safety Checks

Add validation before loading:

- malformed leveldata
- missing walkmask section
- unsupported entity warnings
- impossible objective combinations
- invalid hash/download failures

These should fail gracefully and log clearly.

## Execution Order

1. Plugin command registry and plugin loader.
2. Plugin event surface for server/admin lifecycle.
3. Custom map entity alias parity.
4. Custom map importer tests and fixtures.
5. Custom map metadata protocol additions.
6. Client download/hash/locator workflow.
7. Host UI and rotation flow for custom maps.
8. Hardening and plugin sample implementation.

## First Deliverables

The first implementation pass should aim to land these concrete outcomes:

- server can load a sample plugin DLL and run a custom command
- plugin events fire for connect/chat/map change
- `CustomMapPngImporter` supports all legacy entity names used by `CustomMapCreateEntitiesFromEntityData.gml`
- tests prove that entity aliases and team gates import correctly
- protocol can carry custom-map metadata

## Risks

- Exposing too much mutable server state to plugins too early will make the API brittle.
- Custom map parity will expand test surface quickly; fixtures need to stay small and explicit.
- Downloaded custom map flow needs safe replacement logic to avoid corrupting local map files.
- Plugin loading needs version checks so server upgrades do not silently break third-party extensions.

## Ready-to-Start File Targets

### Server plugin pass

- `Source/GG2.CSharp/src/GG2.Server/GameServer.cs`
- `Source/GG2.CSharp/src/GG2.Server/ServerConsoleCommandProcessor.cs`
- `Source/GG2.CSharp/src/GG2.Server/HostedServerAdminPipeHost.cs`
- `Source/GG2.CSharp/src/GG2.Server/ServerSessionManager.cs`
- `Source/GG2.CSharp/src/GG2.Server/GG2.Server.csproj`
- new plugin abstractions/runtime files under `Source/GG2.CSharp/src/`

### Custom map parity pass

- `Source/GG2.CSharp/src/GG2.Core/CustomMapPngImporter.cs`
- `Source/GG2.CSharp/src/GG2.Core/GameMakerRoomMetadataImporter.cs`
- `Source/GG2.CSharp/src/GG2.Core/RoomObjectType.cs`
- `Source/GG2.CSharp/src/GG2.Core/SimpleLevelFactory.cs`
- `Source/GG2.CSharp/src/GG2.Protocol/*`
- `Source/GG2.CSharp/src/GG2.Client/*`
- `Source/GG2.CSharp/src/GG2.Server/*`

## Definition of Done for This Program

This pass is complete when:

- a legacy server operator can extend the dedicated server with supported plugin DLLs
- a legacy custom PNG map can be hosted, advertised, acquired by a client, validated, cached, and loaded
- official-map parity tests still pass
- new plugin and custom-map parity tests pass
- the GG2 dev team no longer needs the GML runtime for routine server extension or custom-map workflows
