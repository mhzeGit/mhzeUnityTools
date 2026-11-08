# Phases 8 and 9 - textured assets into Unity, collision, player, walk tests

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

All editor work goes through **in-memory C#** compiled inside the running editor by `tools/unity/ucs.py` over the Unity MCP HTTP endpoint (`mcp.py`). Nothing is added to the Unity project's `Assets/`
(AGENTS.md forbids editor scripts that change scenes on their own); the C# lives in `tools/unity/cs/` and is filled with the `@@TOKENS@@` of `env.json` just before each compile.

## 8.0 How the bridge works (know it, it explains every error message)
* `mcp.py`: JSON-RPC client for `POST http://127.0.0.1:8080/mcp` (`initialize` -> `Mcp-Session-Id` -> `notifications/initialized` -> `tools/call`). The session id is cached in `.utmp/<slug>/mcp_session.txt`.
* `ucs.py <source.cs> <Namespace.Type.Method> [arg | @argfile] [--emit]`: wraps your file in a bootstrap method body sent to the MCP tool `execute_code`; the bootstrap compiles the file with **CodeDom (C# 6 only)**
  using the editor's loaded assemblies via a response file, loads the DLL, calls `public static string Method(string arg)` with the JSON argument and returns its string. Inspect generated code and preserve the connector's safety checks. If an operation is rejected, use a narrower supported operation rather than disabling safeguards. `--emit` prints the bootstrap for any other MCP client's `execute_code`.
* Writing C# for it: C# 6 (no `out var`, no `is` patterns, no `?.` on events, no tuples); write `UnityEngine.Object` (not `Object`: ambiguous with `System.Object`); protected members of Unity types are not reachable;
  put `\n` inside C# strings through Python with `chr(92)`; JsonUtility does not fill arrays of custom classes in dynamically loaded assemblies (split by hand, or use string arrays).
* `bo.py` merges `cs/EnvOps*.cs` into `.utmp/<slug>/unity/_EnvAll.cs`, opens the environment scene **additively**, makes it active only while the op runs, then restores the previous active scene and closes it (re-saving only to clear a dirty flag it caused itself).
  Ops abort in Play Mode and while compiling/importing; `bo.retry` waits and retries. `status` always answers.

## 8.1 Textured assets -> Unity (`tex.py`)
```
python tools/unity/tex.py all <group> [<group> ...]    # = tex, mats, models, apply
```
* `tex`: `.utmp/<slug>/gmat/<group>/albedo.png` -> `<textures dir>/<prefix><group>_Albedo.png` (import settings: sRGB colour, mipmaps, **Clamp** wrap because UVs are atlas-packed, trilinear, anisotropic 8, max 2048, CompressedHQ, no alpha).
* `mats`: URP/Lit material `<materials dir>/<prefix><group>.mat` (white base colour x `tint`, smoothness `smooth` ~0.08-0.12, metallic 0, **environment reflections OFF** keyword `_ENVIRONMENTREFLECTIONS_OFF`)
  and for groups that own a **Glow** region a second material `<prefix><group>_Glow.mat`: black base colour + HDR emission (emission colour = (g, 0.44 g, 0.18 g): orange so the tonemapper does not wash the glow out to yellow-white; `glow` multiplier g: furnace window 3.9, lantern 3.0 in the case study; the real light source sits inside the glass). Look-dev numbers live in `<env>/unity/materials.json`.
* `models`: `fbx_uv/<Asset>_uv.fbx` -> `<models dir>/<Asset>.fbx` (re-imports over the clay FBX: same asset path, same prefab links).
* `apply`: rebuilds each `<prefix><Asset>.prefab` from the re-imported model and assigns the group's materials per renderer (renderer/mesh name `<Region>__<Part>`; Glow parts get the `_Glow` material).
The scene instances of the prefabs update by themselves; **no scene is regenerated**. `all` includes prefab regeneration: use it only for generated, untouched prefabs. For existing hand-authored prefabs, import maps/models separately and make scoped material/collider edits.
Clean leftovers afterwards: clay materials folder, empty `Models N` folders, `_Test` models (check with `bo.py op Audit`: it prints any `Clay_` material still in use, missing scripts, light and triangle counts).

## 8.2 Colliders (`bo.py op PrefabColliders`, data in `<env>/unity/colliders.json`)
Colliders live in the **asset prefabs** so every instance carries its own collision. `box` assets get one BoxCollider from local bounds; `cylinder` entries (`Asset:MeshChild`) get a convex cylinder mesh collider fitted to that child (boilers, plinths,
pipes: a cylinder, not the riveted mesh); `stairs` entries (`Asset:MeshChild:treadDepth`) get a solid wedge under the nose line of the steps so the CharacterController walks up a ramp. Small decorative parts get none. Re-run after any prefab rebuild.

## 8.3 Outline / project renderer features (project specific)
If the project has a screen-space outline renderer feature keyed by scene name (the Blah Blah Family one is), `bo.py op Outline "text=color=0.07,0.04,0.025;depth=0.5,1.1;normal=1.3,1.0"` writes `<prefix>Outline.asset`
(a copy of the house outline settings) and adds a per-scene entry to the renderer asset (`renderer_asset` in env.json). The shared renderer asset belongs to the project: touch only your entry and say so in the commit description.
Posterize/SSAO features are project-wide; do not retune them for one room.

## 9. Player, collision and walk tests
1. `bo.py op Player f0=0 f1=0 f2=-2.9 i0=0`: copies the first-person rig `FPC_Character` from `player_source_scene` into the scene (once) and places it at the spawn (f0,f1,f2 = x,y,z; i0 = yaw degrees). The rig is a CharacterController
   (height 1.8, radius 0.3, step 0.2). Do not add a NavMeshAgent.
2. `bo.py op WalkTest f0=<x> f1=0 f2=<z> "text=leg_name:dx,dz;leg2:dx,dz"`: moves the CharacterController through relative legs in 4 cm steps with edit-mode physics and prints end position, max height and grounded.
   Walk: (a) the whole perimeter just inside the walls, (b) every aisle (between boilers, along the trench, to the door), (c) up the stairs to the landing and along it, (d) through the entrance door both ways, (e) into every corner behind props.
   Expected: end positions match the plan, `maxY` rises by the landing height on stairs, no leg ends early. A leg that stops early = a collider gap or a step above the step height: fix the collider or the asset.
3. `bo.py op Cast f0=.. f1=.. f2=.. "text=dx,dy,dz;..."` sphere-casts (r 0.3) to see what blocks a route; `bo.py op Nudge "text=Name:+dx,dy,dz"` moves placed instances (fix a prop that blocks a route; remember to save).
4. Edit-mode physics tests are diagnostics; if Play Mode is unavailable, report runtime traversal as unverified. In BlahBlahFamilyV6 do not enter editor Play Mode (AGENTS.md): walk it in your own standalone player (`Docs/rules/TESTING.md`), batched with the rest of the task's checks, and also with `walk`
   (`probe`, `move`, `teleport`). **Check for a human first** (`Mouse.current.lastUpdateTime`): never fight the user's input. Say in the report which of the two you ran.
5. Reachability beyond the floor: headroom under the landing, door widths >= 0.9 m, no gaps between floor pieces and walls the capsule can fall into, no collider sticking into aisles.

## Hierarchy and naming conventions
`<Name>` root -> `Floor`, `Walls/North...`, `Frame`, `Entrance`, `Ceiling`, `Stairs`, `Lanterns`, `Boilers`, `Pipes/<run>`, `Props`; scene-level objects: `PostProcess` (global Volume, priority 10), `Lighting` (fill), `FPC_Character`.
One module = one root; static flags on environment instances; no `GameObject.Find` in game code; no markers in the shared story tree (see `Docs/GAMEPLAY_MODULES.md` of this repo when the environment hosts gameplay).

## GATE 8/9
- [ ] hero + 8 angles with real textures captured (`bo.py shots`), described, compared with the concept; fix list closed
- [ ] `bo.py op Audit`: no Clay_ materials, no missing scripts, counts recorded
- [ ] WalkTest logs for perimeter, aisles, stairs, door, corners all reached; blockers fixed
