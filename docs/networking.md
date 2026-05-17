# Networking

> RUNNER is **server-authoritative**. The server simulates; clients predict and reconcile.

## Tick & timing

- Server tick: **64 Hz** (configurable in `RUNNER.sbproj` → `GameSettings.Tickrate`)
- Client predicts at render rate, snapshots state per server tick
- All gameplay-affecting math runs on the server's fixed timestep

## Channels

Channels group replicated state by interest + frequency.

| Channel        | Frequency | Scope              | Examples                                  |
| -------------- | --------- | ------------------ | ----------------------------------------- |
| `Movement`     | 64 Hz     | per-player (own + nearby) | position, velocity, dash cooldown   |
| `Race`         | 16 Hz     | race participants  | checkpoint hits, finish times             |
| `Profile`     | on-change | owner only         | XP, level, currencies, inventory          |
| `World`        | 8 Hz      | zone-bound         | collectibles, AFK pad state               |
| `Cosmetic`     | on-change | broadcast          | trails, auras, equipped pets              |

Defined in `Code/Game/Networking/NetworkChannels.cs`.

## Prediction & Reconciliation

```
Frame N (client)
  ├─ sample input              ──┐
  ├─ apply locally (predict)     │  ──► send InputCmd { seq, input } to server
  └─ render                      │
                                 ▼
                          Server tick N+latency
                          ├─ apply InputCmd (authoritative)
                          ├─ broadcast snapshot { seq_ack, state }
                          ▼
Frame N+rtt (client)
  ├─ receive snapshot
  ├─ if local != server → reconcile:
  │     · rewind to seq_ack
  │     · replay InputCmd[seq_ack+1 .. now]
  │     · smooth visual delta over ~100 ms
  └─ continue
```

## RPC patterns

- **Owner → Server**: input commands, intent ("buy item", "equip pet")
- **Server → Owner**: confirmations, error codes, reward notifications
- **Server → Broadcast**: world events (race countdown, leaderboard updates)
- **Server → Scope**: VFX triggers (only nearby clients see your sonic boom)

## Interest management

- Players only receive `Movement` snapshots for entities within their **interest radius**
  (currently 4096 units, tuned per zone)
- Race participants always replicate to each other regardless of distance
- Pet meshes use **proximity LOD**: full at < 512 u, simplified < 2048 u, hidden beyond

## Bandwidth budget

Target: **< 32 KB/s per client** at 64 ticks with 16 visible players.

Levers when over budget:
1. Reduce snapshot rate for distant entities (12 Hz instead of 64 Hz)
2. Quantize positions (8.8 fixed-point within zones)
3. Delta-compress velocity (rare → omit)
4. Move cosmetic state to `on-change` only

## Anti-cheat

- Server rejects movement deltas > `MaxAllowedSpeedPerTick` (derived from `SpeedCurve`)
- Currency mutations only happen server-side; client requests, server validates
- Race finishes require all checkpoints in order, server-stamped timestamps
- Disconnect during race → server retains state for 30 s reconnect window
