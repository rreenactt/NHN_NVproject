---
name: unity-mcp-ops
description: >
  Core workflow for driving the CoderGamester mcp-unity (v2.6.0) MCP server to build and
  modify Unity scenes: creating/wiring GameObjects, attaching C# scripts as components,
  editing materials, running play-mode checks, and diagnosing console errors. This is the
  foundation skill — read it before using any fps-* skill, because those describe WHAT to
  build and this describes HOW to make the MCP server actually do it. Trigger for ANY Unity
  MCP task even when not explicitly asked: "유니티", "씬 구성해줘", "컴포넌트 붙여줘",
  "프리팹 만들어", "스크립트 붙여", scene setup, GameObject creation, wiring references,
  material editing, or "왜 콘솔에 에러 나?".
---

# Unity MCP Operations (mcp-unity 2.6.0)

You are controlling a running Unity Editor through the CoderGamester **mcp-unity** server.
You do NOT touch Unity directly — every change goes through a tool call, and the change only
"lands" once Unity has processed it. Treat the Editor as a live, stateful system: read before
you write, and verify after you write.

## Tool map (capability → tool)

| You want to… | Tool |
|---|---|
| Read the scene tree before editing | `get_hierarchy` (resource `unity://hierarchy`) |
| Create / rename / re-parent / set transform, tag, layer, active | `update_gameobject` |
| Add a component OR set fields on an existing one | `update_component` |
| Select an object (so a later menu item acts on it) | `select_gameobject` (by `objectPath` or `instanceId`) |
| Run any Editor command not covered by a tool | `execute_menu_item` (`menuPath`, e.g. `GameObject/Light/Directional Light`) |
| Discover valid menu paths | `get_menu_items` (resource `unity://menu-items`) |
| Instantiate a prefab/asset into the scene | `add_asset_to_scene` |
| Turn a scene object into a reusable prefab | `create_prefab` |
| Edit / inspect a material | `update_material` / `get_material_info` |
| Install a package (URP, Input System, Cinemachine…) | `add_package` |
| Enter/exit/step play mode | `set_play_mode_status` (`play`/`pause`/`stop`/`step`) |
| Read errors & warnings | `get_console_logs` (resource `unity://console-logs`) |
| Run edit/play-mode tests | `run_tests` |
| Do many ops atomically in one round-trip | `batch_execute` |

Parameter names can differ by patch. If a call is rejected, inspect the tool's schema or run
`get_menu_items` rather than guessing repeatedly.

## The golden workflow: script → compile → attach

mcp-unity has **no script-authoring tool**. C# comes from YOUR file tools, not from Unity.
The type must compile before it exists as a component. So the loop is always:

1. **Write** the `.cs` file into the project's `Assets/` tree with your normal file-editing
   tools (e.g. `Assets/Scripts/FPS/PlayerController.cs`). One MonoBehaviour per file, class
   name == file name.
2. **Compile** — Unity auto-recompiles on focus. Nudge it if needed via
   `execute_menu_item` → `Assets/Refresh`, then **wait**.
3. **Verify compilation** with `get_console_logs`. If there are compile errors, the component
   type does not exist yet — fix the script and recompile before step 4. Never try to attach a
   type that failed to compile; the call will fail with a confusing "type not found".
4. **Attach & wire** with `update_component` (adds the component if missing, then sets serialized
   fields — object references included).
5. **Prove it works**: `set_play_mode_status: play`, watch `get_console_logs`, then `stop`.

Skipping the console check between 3 and 4 is the #1 cause of wasted turns.

## Read-before-write, verify-after-write

- Before restructuring, call `get_hierarchy` so paths in later calls are correct.
- Tags and layers must **already exist** before you assign them. Create them via
  `execute_menu_item` → `Edit/Project Settings...` (Tags and Layers) or add them in a script's
  `[InitializeOnLoad]` editor helper. Assigning a non-existent layer silently no-ops.
- After a batch of edits, call `get_console_logs` once. Cheap insurance.

## batch_execute — use it, but keep batches coherent

Group operations that belong together (build a hierarchy, add several components to one object)
into a single `batch_execute` with rollback. This cuts round-trips dramatically. Don't batch
across a compile boundary — anything that needs a freshly-written script to exist must run
*after* the compile check, in a later call.

## Verifying visually

You can't "see" the Game view directly. To confirm layout, either read transforms back via
`get_hierarchy`/`update_component` queries, or drive `set_play_mode_status: play` + `step` and
read console diagnostics you deliberately log. When something looks wrong, add a temporary
`Debug.Log` in the script, replay, read `get_console_logs`, then remove it.

## Coding conventions to honor (this project)

These are the user's standing Unity conventions — bake them into every script you generate:

- **Meaningful variable names.** No `x`, `tmp`, `obj2`.
- **Avoid heavy real-time work in `Update()`.** Poll input in `Update`, but do physics/movement
  in `FixedUpdate`, cache expensive lookups (`GetComponent`, `Camera.main`) in `Awake`/`Start`,
  and prefer event-driven or coroutine-based logic over per-frame recomputation.
- **Don't use Rigidbody for throwing.** Thrown objects (grenades, etc.) use a manually integrated
  trajectory, not `Rigidbody.AddForce`. (See fps-weapon-system for the pattern.)
- Comment non-obvious logic so future edits are safe.

## Common failure modes

- **"Component type not found"** → the script didn't compile. Check `get_console_logs`.
- **Reference field stays null after `update_component`** → the target object path was wrong or
  the referenced object didn't exist yet. Create referenced objects first, then wire.
- **Menu item "does nothing"** → wrong object selected. `select_gameobject` first, then
  `execute_menu_item`.
- **Changes vanish after play mode** → you edited during play mode. Edit in edit mode; play mode
  changes are not persisted.
