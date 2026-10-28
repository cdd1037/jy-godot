# Project instructions

- Communicate in Chinese. Use English Conventional Commit messages with a scope.
- Keep Game.Core and Game.Content independent of Godot. Godot nodes do not own game rules or authoritative state.
- Do not introduce Lua, dynamic code plugins, or a second event runtime.
- Treat OpenLegend as a read-only reference. Keep imported original assets and generated files out of Git.
- Run `./build.ps1` to build and test. Run the Godot smoke test after changing rendering or input.
- Format and compatibility decisions must identify their OpenLegend reference and distinguish tested fixtures from original assets.
