# Gameplay Loop

> Every minute of play must feel like a tangible step forward.

## Core loop (60 seconds)

```
[0s]  Spawn / resume in zone
   │
   ▼
[10s] First sprint, speed bar fills, XP ticks up
   │
   ▼
[25s] Level up → +Speed → unlocks new path
   │
   ▼
[40s] Pickup or pet bonus → XP multiplier
   │
   ▼
[55s] Speed milestone → cosmetic flair (FOV, trail intensity)
   │
   ▼
[60s] Hook: "next level in 8s" / "race starts in 12s"
```

## Meta loop (1 session)

```
Enter zone → Sprint → Level up → Buy upgrade → Unlock zone → Race → Win reward → Rebirth
```

## Long-term loop (across sessions)

```
Daily reward → Login bonus → Quest progress → Battle pass tier → Event currency → Limited cosmetic
```

## Feedback budget (per second of high-speed play)

| Channel  | Target |
| -------- | ------ |
| Visual   | FOV pulse · trail intensity · screen-edge speedlines · UI tick |
| Audio    | wind layer · footstep pitch · level-up jingle on threshold |
| Haptic   | optional gamepad rumble on dash / boost  |
| UI       | speedometer · XP bar · level pip · next-unlock teaser |

## Onboarding (first 5 minutes)

1. **00:00** — Spawn in Spawn City, free movement enabled
2. **00:15** — First sprint tutorial popup: "Hold SHIFT to sprint"
3. **00:40** — First level up (designed to hit fast)
4. **01:00** — Speed gate opens, "New area unlocked"
5. **02:30** — First pet egg granted (free)
6. **04:00** — First race invite
7. **05:00** — First rebirth teaser ("3% to first rebirth")

## Retention hooks

- **Idle/AFK pads** in every zone — passive XP while logged off (capped, scales with rebirth)
- **Daily login streak** — accelerating rewards
- **Weekly race** — leaderboard reset
- **Seasonal event** — limited cosmetics rotated every 4-6 weeks
- **Rebirth ladder** — exponential power curve, soft caps that demand rebirth
