# RUNNER — Roadmap

> Live document. Update as scope shifts. Each milestone has a definition of done.

## M0 — Foundations  *(in progress)*

**Goal:** repo is real, anyone can clone & run, CI works.

- [x] Git repo + branch protection on `main`
- [x] `RUNNER.sbproj`
- [x] Folder scaffold (`Code/Game/*`)
- [x] CI: build, format check, tests
- [x] Architecture & networking docs
- [ ] Empty scene loads in S&box
- [ ] `Game.cs` boot path resolves services

## M1 — Movement Vertical Slice

**Goal:** one player, one map, the act of running *feels* great.

- [ ] `PlayerPawn` with input → velocity pipeline
- [ ] Acceleration curve from `SpeedCurve` config
- [ ] Camera FOV / shake / trails scale with speed
- [ ] Slope handling, air control, dash
- [ ] Client prediction + server reconciliation
- [ ] Anti-speedhack: max delta per tick

## M2 — Persistence & Progression

**Goal:** XP, levels, profile survives disconnect.

- [ ] `PlayerProfile` schema versioned
- [ ] `IProfileRepository` (local file impl + cloud-ready interface)
- [ ] XP grant + level-up event
- [ ] First zone unlock gated on level
- [ ] Save on dirty flag, async, throttled

## M3 — Multiplayer Races

**Goal:** queue → race → reward → leaderboard.

- [ ] Matchmaking lobby
- [ ] Race state machine (Lobby → Countdown → Running → Finished)
- [ ] Server-authoritative checkpoints
- [ ] Per-race leaderboard
- [ ] Latency compensation for finish line

## M4 — Economy, Pets, Equipment, Rebirth

**Goal:** the addictive loop is closed.

- [ ] Coins / Gems / Tokens currencies
- [ ] Shop UI + server-validated purchases
- [ ] Egg hatching + pet inventory
- [ ] Equipment slots (boots, gloves, trail, aura, gadget)
- [ ] Rarity rolls (Common → Secret)
- [ ] Rebirth flow + multipliers

## M5 — Live-Service Ops

**Goal:** we can ship content without code changes.

- [ ] Data-driven events (`Code/Game/Data/Events/`)
- [ ] Battle pass tracks
- [ ] Promo codes
- [ ] Daily rewards
- [ ] Admin panel + analytics funnel
- [ ] Telemetry: DAU, retention, conversion

## M6 — **Public Release** 🚀

**Goal:** sbox.game listing live, marketing-ready.

- [ ] Performance budget met (target 144 fps on mid-rig)
- [ ] All zones polished
- [ ] Anti-cheat hardened
- [ ] Trailer + screenshots
- [ ] Launch event in-game
