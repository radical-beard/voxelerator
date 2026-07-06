# Voxelerator

**A palette-constrained voxel editor whose native format is a stack of PNGs** — one image
per horizontal layer, one pixel per voxel. Draw slices top-down like pixel art, flip to a
3D voxel view (orthographic or perspective) to check the shape, and export a folder your
game meshes directly. LLMs are first-class co-artists via a built-in MCP server.

> Status: under construction — core format library first, editor + MCP server on top.

## Why

1. **The PNG stack IS the model.** No opaque binary format: every layer is an ordinary
   image any tool (or LLM, or git diff) can read. Export = copy. Import = drop a folder in.
2. **The palette is law.** A model binds to a named palette (≤ 16 colors) and the editor
   refuses colors outside it — cohesion by construction.
3. **Agent co-authoring is native.** The MCP server reads layers as text grids, edits
   voxels, and renders the model back as an image — the full see-it/fix-it loop.

## Layout

| Path | What |
|---|---|
| `core/Voxelerator.Core` | zero-dependency .NET 8 library: format codec, model, greedy mesher, edit ops, registry, software renderer |
| `core/Voxelerator.Core.Tests` | xunit suite: round-trip, byte-determinism, validation, mesher, ops |
| `mcp/Voxelerator.Mcp` | `voxelerator-mcp` — stdio MCP server over the core |
| `app/` | Godot 4.6 (C#) editor app — UI built in code, EvaLuate-hosted live panels |

Format spec, editor docs, and MCP setup land here as they ship.
