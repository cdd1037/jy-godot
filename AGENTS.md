# Project instructions

- Communicate in Chinese. Use English Conventional Commit messages with a scope.
- Keep Game.Core, Game.Content and Game.Importing independent of Godot. Godot nodes do not own game rules or authoritative state.
- Preserve the one-way project references: Game.Importing -> Game.Content -> Game.Core. Content owns runtime documents, loading, validation and save adapters; DOS decoding, compilation and atlas writing belong to Importing. Never reference Importing from Content or Core.
- Godot retains its import UI and therefore references Importing directly. This does not make import code part of normal library loading.
- Keep Battle and Events as folders and partial classes inside Core, and MOD documents in Content; do not add separate Battle, Event or Mod projects without a concrete dependency boundary.
- Organize Godot scripts under App, Battle, Views and Diagnostics. Preserve script UIDs and scene bindings when moving scripts; diagnostics must not own production rules.
- Do not introduce Lua, dynamic code plugins, or a second event runtime.
- Treat OpenLegend as a read-only reference. Keep imported original assets and generated files out of Git.
- Run `./build.ps1` to build and test. Run the Godot smoke test after changing rendering or input.
- If the user explicitly defers validation, report the omitted checks. Historical passing results do not validate later changes or file moves.
- Format and compatibility decisions must identify their OpenLegend reference and distinguish tested fixtures from original assets.
