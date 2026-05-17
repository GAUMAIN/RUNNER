# Economy

> All economy logic is **server-authoritative**. Clients propose; server commits.

## Currencies

| Currency        | Source                                        | Sink                                          |
| --------------- | --------------------------------------------- | --------------------------------------------- |
| **Coins**       | running, races, quests                        | basic upgrades, eggs (basic)                  |
| **Gems**        | level-up milestones, premium, achievements    | premium upgrades, eggs (premium), rebirth boosts |
| **Tokens**      | rebirth (granted on each rebirth)             | rebirth shop: permanent multipliers           |
| **Event Currency** | active events only                         | event shop: limited cosmetics                 |

## Faucets vs Sinks (target ratios)

For a healthy economy, **net inflation per player must trend toward 0** over a session.

| Tier of player  | Coins/min in | Coins/min out | Notes |
| --------------- | ------------ | ------------- | ----- |
| Fresh           | ~150         | ~100          | gentle accumulation, builds excitement |
| Mid-progression | ~2,000       | ~1,800        | upgrades scale to consume nearly all income |
| Late / Rebirth  | ~50,000      | ~50,000       | rebirth resets faucet, sinks scale with prestige |

Track via analytics events `currency_grant` / `currency_spend` with tags.

## Shops

- **Upgrade Shop** — permanent stat boosts, exponential cost curve
- **Egg Shop** — pet eggs (Coin tier and Gem tier)
- **Cosmetic Shop** — trails, auras (rotating)
- **Rebirth Shop** — Token-only, permanent multipliers
- **Event Shop** — limited; Event Currency only

## Pricing curve

Default: `cost(n) = base * growth^n` with `growth = 1.15` for upgrades, `1.08` for cosmetics.

Tuned per item in `Code/Game/Data/Shop/*.json`.

## Gachas (eggs)

Server rolls. Client never sees probabilities except in the publicly displayed odds table.

| Rarity     | Basic Egg | Premium Egg |
| ---------- | --------- | ----------- |
| Common     | 60%       | 30%         |
| Rare       | 25%       | 35%         |
| Epic       | 10%       | 20%         |
| Legendary  | 4%        | 10%         |
| Mythic     | 0.9%      | 4.5%        |
| Secret     | 0.1%      | 0.5%        |

Odds published publicly (regulatory + trust).

## Battle Pass

- Free track + Premium track
- 50 tiers per season (~6 weeks)
- XP from any activity contributes; race wins boost
- End-of-season exclusive cosmetic on premium track

## Daily Rewards

| Day | Reward |
| --- | ------ |
| 1   | 500 Coins |
| 2   | 1,000 Coins |
| 3   | 5 Gems |
| 4   | Basic Egg |
| 5   | 10 Gems |
| 6   | 2× XP Boost (15 min) |
| 7   | Premium Egg |

Streak resets after 48h of no login.

## Anti-abuse

- AFK rewards capped at **8 h offline accumulation**
- AFK pads have diminishing returns past 90 min continuous use
- Currency grants logged with reason code → economy debugger can audit
- Suspicious patterns (e.g., 10× rebirth in 10 min) → soft-flag for review
