# Assets

Source 2 / S&box content. Compiled artifacts (`*_c`) are gitignored.

```
Assets/
├── models/      ← .vmdl + source .fbx/.glb
├── materials/   ← .vmat + textures
├── sounds/      ← .vsnd + .wav/.ogg
├── particles/   ← .vpcf-equivalents
├── scenes/      ← .scene files
├── prefabs/     ← reusable component bundles
└── ui/          ← Razor + scss
```

Naming: `snake_case`. Group by feature (`player_run_anim.vmdl`, not `anim_001.vmdl`).
