# RUNNER — Architecture Overview

> This document is the **single source of truth** for how systems fit together.
> Update it whenever you add a system, change a dependency, or move a folder.

## Guiding Principles

1. **Server-authoritative**. The server is the simulation. Clients predict and reconcile.
2. **Data-driven**. Tuning lives in `Code/Game/Data/*` configs, not in code.
3. **Composition over inheritance**. Components and services, not deep class trees.
4. **Event-driven**. Systems communicate via an event bus, not direct references.
5. **Pool what you spawn**. Allocations during gameplay are a bug.
6. **One responsibility per type**. SRP enforced via small files.
7. **Async by default**. Long-running work is `async`. Frame work is sync.

## Folder Map

```
Code/Game/
├── Core/         ← Bootstrapping, Game class, ServiceLocator, base interfaces
├── Gameplay/     ← Movement, races, rebirth, AFK training (the actual loops)
├── Networking/   ← Channel definitions, RPC helpers, replication contracts
├── UI/           ← HUD, menus, shops, race UI (Razor)
├── Systems/      ← Cross-cutting: EventBus, ObjectPool, TimeService
├── Economy/      ← Currencies, shops, gachas, battle pass, daily rewards
├── Player/       ← PlayerPawn, PlayerProfile, PlayerStats, Inventory
├── World/        ← Zones, streaming, collectibles, AFK pads
├── Audio/        ← Sound dispatch, music director
├── VFX/          ← Speed trails, sonic booms, auras, hit effects
├── Data/         ← Configs (GameResource): SpeedCurve, XPCurve, RarityTable...
├── Backend/      ← ICloudSave, ILeaderboardClient, IAnalyticsClient
├── Tools/        ← Admin panel, console commands, debug overlays
├── Events/       ← Strongly-typed event records (PlayerLeveledUp, RaceFinished)
├── Analytics/    ← Telemetry hooks, funnel events
└── Config/       ← GameConfig, FeatureFlags, BuildInfo
```

## Layered Dependencies

```
        ┌────────────────────────────────┐
        │             UI                 │
        └────────────────┬───────────────┘
                         ▼
        ┌────────────────────────────────┐
        │          Gameplay              │  ← Movement, Races, Rebirth, AFK
        └────────────────┬───────────────┘
                         ▼
   ┌──────────┐  ┌───────────────┐  ┌────────────┐
   │ Economy  │  │   Player      │  │   World    │
   └────┬─────┘  └───────┬───────┘  └─────┬──────┘
        └────────────────┼────────────────┘
                         ▼
        ┌────────────────────────────────┐
        │  Systems  (EventBus, Pool, …)  │
        └────────────────┬───────────────┘
                         ▼
        ┌────────────────────────────────┐
        │  Core  (ServiceLocator, Game)  │
        └────────────────┬───────────────┘
                         ▼
        ┌────────────────────────────────┐
        │   Backend  (Save, Analytics)   │
        └────────────────────────────────┘

  Cross-cutting (depended on by all): Networking, Data, Config, Events, Analytics
```

**Rule:** a layer may only import from layers strictly below it (plus cross-cutting). Enforced by review.

## Server vs Client

| Concern                  | Server | Client |
| ------------------------ | :----: | :----: |
| Movement simulation      |   ✅   |   🟡 (prediction) |
| Race state / checkpoints |   ✅   |   ❌   |
| Economy mutations        |   ✅   |   ❌   |
| XP / level grants        |   ✅   |   ❌   |
| Pet spawn / equip        |   ✅   |   ❌   |
| HUD rendering            |   ❌   |   ✅   |
| VFX trigger              |   🟡 (event) |   ✅   |
| Input sampling           |   ❌   |   ✅   |

🟡 = partial / shared

## Patterns Used

- **Service Locator** (`Core.ServiceLocator`) for cross-system access without singletons.
- **Event Bus** (`Systems.EventBus`) for fire-and-forget notifications between systems.
- **Strategy** for movement modes (Walking / Sprinting / Dashing / Airborne).
- **State Machine** for races (Lobby → Countdown → Running → Finished).
- **Object Pool** for particles, pets, projectiles.
- **Factory** for equipment generation (rarity rolls).
- **Repository** for persistence (`IProfileRepository`).
- **Mediator** for input handling (client) → command (server).

## Data Flow: Player Earns XP

```
Player crosses checkpoint
        │
        ▼  (server-side)
RaceSystem.OnCheckpoint(player, checkpointId)
        │
        ▼
XPSystem.Grant(player, amount * profile.XPMultiplier)
        │
        ├──► PlayerProfile.XP += amount
        │            │
        │            ▼
        │     if LeveledUp → EventBus.Publish(new PlayerLeveledUp(...))
        │                              │
        │                              ├──► UI.HUD.ShowLevelUp()
        │                              ├──► Audio.Play("levelup")
        │                              ├──► Analytics.Track("level_up", {...})
        │                              └──► QuestSystem.OnLevelUp()
        │
        └──► Profile flagged dirty → next tick → IProfileRepository.SaveAsync()
```

## See Also

- [docs/networking.md](docs/networking.md) — replication, channels, prediction
- [docs/gameplay-loop.md](docs/gameplay-loop.md) — the core loop in detail
- [docs/economy.md](docs/economy.md) — sinks, faucets, balancing
- [docs/style-guide.md](docs/style-guide.md) — C# conventions
