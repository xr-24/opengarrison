Gang Garrison 2 C# Port

Contents
- GG2.Client: desktop client
- GG2.Server: dedicated/local server
- Content: MonoGame content
- Assets: bundled runtime and reference assets from the original project tree
- config: editable JSON settings and sample map rotation
- Maps: custom map drop location (future-facing)

Quick start
1. Run GG2.Client.
2. To host a local server from the client, use Host Game.
3. To run a dedicated server directly, launch GG2.Server.

Config files
- config/client.settings.json
- config/input.bindings.json
- config/server.settings.json
- config/sampleMapRotation.txt

Notes
- The packaged build is self-contained with respect to required runtime assets. It should not need a full source checkout beside it.
- The packaged `Assets/Source/gg2` folder includes the full legacy GG2 source tree for reference, including scripts, objects, maps, sprites, sounds, and related metadata.
- The port is intentionally modernized around a new networking layer while staying close to the original GG2 code and content structure.
