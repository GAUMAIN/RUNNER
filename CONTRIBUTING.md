# Contributing to RUNNER

Thanks for wanting to make RUNNER faster, smoother, and more addictive. This document is the contract for working in this repo.

## TL;DR

1. Open / pick up an issue. Discuss approach before large work.
2. Branch from `develop`: `feat/short-slug`, `fix/short-slug`, `chore/short-slug`.
3. Commit using [Conventional Commits](https://www.conventionalcommits.org/).
4. Open a PR into `develop`. Fill the PR template. CI must be green.
5. One approval → squash & merge.

## Branching Model

```
main      ──●──────────────●──────●──    (production-ready, tagged releases)
              ╲           ╱     ╱
develop    ────●─●─●─●─●─●─●─●─●─       (integration branch)
                 ╲   ╲   ╲
feat/movement     ●───●   ●              (short-lived feature branches)
```

- `main` — only release-tagged commits. Protected.
- `develop` — integration. Always deployable to internal playtest.
- `feat/*`, `fix/*`, `chore/*` — short-lived (< 1 week).
- `release/x.y` — release stabilization branches.

## Commit Style

Conventional Commits. Scope = the system touched.

```
feat(movement): add momentum-preserving dash
fix(economy): clamp daily reward stack at 7
chore(ci): cache nuget restore
refactor(networking): extract channel registration
perf(vfx): pool sonic-boom particle instances
docs(architecture): add networking flow diagram
test(rebirth): cover exponential scaling edge cases
```

## Code Style

- **C# conventions**: PascalCase public, `_camelCase` private fields, tabs (see `.editorconfig`).
- **No `var` for built-in types** — `int x = 5;` not `var x = 5;`.
- **Server-authoritative everything**. Client predicts; server reconciles.
- **No magic numbers** — promote to `Code/Game/Config/` or a data asset.
- **Pool what you spawn** — particles, pets, projectiles all go through pools.
- **Async over coroutines** — use `async/await` with `GameTask` / `Task`.
- **One class per file**. File name == class name.

## Pull Request Checklist

- [ ] Branch is up to date with `develop`
- [ ] CI is green (build + tests + format)
- [ ] No new warnings introduced
- [ ] New code has tests (unit if pure logic, integration if multiplayer)
- [ ] Touched systems documented in `docs/` if behavior changed
- [ ] Performance impact considered (allocations, replication bandwidth)
- [ ] Anti-cheat implications considered for any movement/economy change

## Security

If you find a security issue (exploit, server crash, economy abuse), **do not open a public issue**. Email the maintainers privately. See SECURITY.md (TBD).

## License

By contributing, you agree your contributions are licensed under the MIT License (see [LICENSE](LICENSE)).
