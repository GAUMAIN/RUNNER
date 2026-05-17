# C# Style Guide

> Enforced by `.editorconfig` + review. PRs that violate these will be sent back.

## Naming

| Element              | Convention      | Example                     |
| -------------------- | --------------- | --------------------------- |
| Class / struct       | `PascalCase`    | `PlayerProfile`             |
| Interface            | `IPascalCase`   | `IProfileRepository`        |
| Public field / prop  | `PascalCase`    | `MaxSpeed`                  |
| Private field        | `_camelCase`    | `_currentSpeed`             |
| Local variable       | `camelCase`     | `targetVelocity`            |
| Constant             | `PascalCase`    | `DefaultTickRate`           |
| Enum value           | `PascalCase`    | `RaceState.Countdown`       |
| File                 | matches type    | `PlayerProfile.cs`          |
| Namespace            | `Runner.System` | `Runner.Gameplay.Movement`  |

## Formatting

- **Tabs**, width 4.
- One class per file. File name = class name.
- Opening brace on new line (Allman).
- Always braces, even for one-liners.
- `using` directives at top, `System` first.
- Max line ~120 chars.

## Conventions

- **No magic numbers.** Promote to `Code/Game/Config/` const or a `GameResource`.
- **No `var` for primitives** (`int`, `float`, `bool`, `string`).
- **Use `var`** when the type is obvious from the right-hand side.
- **Expression-bodied** members for one-liners: `public bool IsAlive => _hp > 0;`
- **`readonly`** every field that isn't reassigned after construction.
- **`sealed`** every class that isn't designed for inheritance.
- **`record`** for immutable data carriers (events, configs).
- **`nameof()`** instead of string literals for member names.

## Networking-specific

- Networked properties: `[Sync]` or `[Net]` attributes (per S&box API).
- Server-only methods: `[Authority]` or explicit `if ( !Networking.IsHost ) return;`.
- Client RPCs: prefix `Rpc_` (e.g., `Rpc_ShowLevelUp`).
- Server commands from client: prefix `Cmd_` (e.g., `Cmd_BuyItem`).

## Async

- Use `async Task` (or `async GameTask`).
- Never `async void` except for event handlers.
- Cancellation: accept `CancellationToken` on long ops.
- Never `.Result` / `.Wait()` — deadlock risk.

## Logging

- Use `Log.Info` / `Log.Warning` / `Log.Error`.
- Include a system tag: `Log.Info( "[Economy] purchase ok: {Item}", item.Id );`
- No logs in tight loops (per-tick, per-particle).

## Comments

- **Default to none.** Names should carry meaning.
- When *why* is non-obvious, leave one short line.
- Never restate what the code does.
- No "TODO without an issue link" — file the issue and reference it.
