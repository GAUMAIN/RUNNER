# RUNNER

> Live-service speed simulator built on **S&box / Source 2**. Run, rebirth, race, repeat.

[![CI](https://github.com/USER/RUNNER/actions/workflows/ci.yml/badge.svg)](../../actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

---

## Pitch

RUNNER is a multiplayer **speed simulator** where every minute of play feeds a satisfying progression loop:

```
Run  →  Gain XP  →  Increase Speed  →  Unlock Zones  →  Upgrade Gear
   ↑                                                           ↓
   └────── Rebirth (exponential) ←──── Win Races ←─────────────┘
```

Inspired by the best of speed sims, idle/progression games, and competitive multiplayer races — engineered as a **scalable live-service** project from day one.

## Tech Stack

| Layer           | Tech                                  |
| --------------- | ------------------------------------- |
| Engine          | **S&box** on Source 2                 |
| Language        | **C# 11+** (.NET 8 modern features)   |
| Networking      | S&box networking (server-authoritative) |
| Persistence     | `FileSystem.Data` + cloud-ready abstraction |
| UI              | S&box Razor UI                        |
| Build / CI      | GitHub Actions                        |

## Repository Layout

```
RUNNER/
├── Code/Game/                # All gameplay C# code (see ARCHITECTURE.md)
│   ├── Core/                 # Bootstrapping, service locator, interfaces
│   ├── Gameplay/             # Movement, races, rebirth, AFK training
│   ├── Networking/           # Channels, RPCs, replication helpers
│   ├── UI/                   # HUD, menus, shops, race UI
│   ├── Systems/              # Cross-cutting services (events, pooling)
│   ├── Economy/              # Currencies, shops, gachas, battle pass
│   ├── Player/               # Pawn, inventory, stats, profile
│   ├── World/                # Zones, streaming, collectibles
│   ├── Audio/                # SFX/music dispatch
│   ├── VFX/                  # Speed trails, sonic booms, auras
│   ├── Data/                 # ScriptableData configs, balancing tables
│   ├── Backend/              # Cloud save / analytics abstractions
│   ├── Tools/                # Admin panel, debug commands
│   ├── Events/               # Event bus
│   ├── Analytics/            # Hooks for telemetry
│   └── Config/               # Game-wide constants, feature flags
├── Assets/                   # Models, materials, sounds, scenes, UI
├── docs/                     # Architecture & design documents
├── tools/                    # Local dev tools / scripts
├── tests/                    # Unit + integration tests
├── .github/                  # CI workflows, issue & PR templates
└── RUNNER.sbproj             # S&box project manifest
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full breakdown.

## Getting Started

### Prerequisites

- **S&box** (latest dev branch) — https://sbox.game
- **.NET 8 SDK** — https://dotnet.microsoft.com/download
- **Git 2.40+** and (recommended) `gh` CLI
- An IDE: **Visual Studio 2022**, **Rider**, or **VS Code** with C# Dev Kit

### Clone & open

```bash
git clone https://github.com/USER/RUNNER.git
cd RUNNER
```

1. Launch S&box.
2. `Steam → Library → s&box → Tools → Addons → Add Existing Addon` and pick `RUNNER.sbproj`.
3. Hit **Play** in the editor to launch.

## Development Workflow

- **Branches**: `main` (stable) · `develop` (integration) · `feat/*` · `fix/*` · `chore/*`
- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/) — `feat(movement): add air control`
- **PRs**: Small, focused, must pass CI, must include a test plan
- **Reviews**: At least 1 approval before merge. See [CONTRIBUTING.md](CONTRIBUTING.md)

## Roadmap

See [ROADMAP.md](ROADMAP.md). High-level milestones:

- **M0** — Project scaffold, CI, core architecture
- **M1** — Movement vertical slice (single-player, predicted)
- **M2** — Persistence + XP/leveling + first zone
- **M3** — Multiplayer races + lobbies
- **M4** — Economy, pets, equipment, rebirth
- **M5** — Live-service ops: events, battle pass, analytics
- **M6** — **Public release**

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) and our [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE) — free to use, modify, and distribute.
