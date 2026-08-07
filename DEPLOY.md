# PKM Universe Seed Finder — Deploy Notes

A branded Tera Raid seed finder for creator.pkm-universe.com. Reuses the **same**
`RaidCrawler.Core` / `PKHeX.Core` engine the Raid-SV bot uses, so any seed it returns
produces the identical Pokémon when the bot hosts it.

## What's here
- `SeedFinderApi/` — .NET 9 minimal API + static front-end (`wwwroot/index.html`)
  - `GET  /api/health`
  - `GET  /api/species` — full species list for the autocomplete
  - `POST /api/search`  — body: `{game, location, storyProgress, stars, species, shiny, teraType, minFlawlessIVs, maxResults}`
  - `/` serves the branded finder page
- `SeedFinderApi/libs/` — the engine DLLs (PKHeX.Core, RaidCrawler.Core, FlatSharp.Runtime, Google.FlatBuffers, pkNX.Structures.FlatBuffers)
- `Dockerfile` — multi-stage build, listens on **:8080**

## Run locally
```
cd SeedFinderApi
dotnet run -c Release          # http://localhost:5000 (or set ASPNETCORE_URLS)
```

## Docker — current production setup (seeds.pkm-universe.com via cloudflared)
```
docker build -t pkmu-seedfinder .
docker stop pkmu-seedfinder && docker rm pkmu-seedfinder
docker run -d --name pkmu-seedfinder --restart unless-stopped ^
  --network pkm-universe_pkm-network ^
  --add-host host.docker.internal:host-gateway ^
  --env-file .env pkmu-seedfinder
```
- **`--add-host host.docker.internal:host-gateway` is required** — without it the container
  cannot resolve the raid bots on the host (rotations show all maps offline/null). Same
  convention as `pkmu-raidhub`.
- No published ports: the `pkm-universe-tunnel` cloudflared container reaches it at
  `pkmu-seedfinder:8080` over the `pkm-universe_pkm-network` network.
- Bots: Paldea `host:9100`, Kitakami `host:9090`, Blueberry `host:9110`
  (override with `BOT_URL_PALDEA` / `BOT_URL_KITAKAMI` / `BOT_URL_BLUEBERRY`).

## Front-end layout (since 2026-08-07)
- `wwwroot/index.html` — the flagship **landing** (source of truth:
  https://github.com/PokemonLover8888/seeds-pkm-universe — copy its `index.html`,
  `assets/`, `reviews.json`, `icon.svg` here on updates)
- `wwwroot/console.html` — the **Seed Console SPA** (the old index.html; permalinks
  `/r/{seed}` + `/list/{id}` fall back to it, and the landing bounces `#/route` deep
  links + `?login=` OAuth returns to it before paint)
- `wwwroot/reviews.json` — ships **empty** until real reviews exist; the landing shows
  the "Season One" founding state and switches to review cards automatically
- `wwwroot/sw.js` — **bump the `pkmu-seeds-vNN` cache version on every deploy**
  (network-first covers `.html`, `/assets/`, `reviews.json`; all `/api/` bypasses cache)

## Updating for new game versions
When Scarlet/Violet updates, refresh the engine DLLs in `SeedFinderApi/libs/` from the
Raid-SV bot's deps (`SysBot.Pokemon/deps/`) so the finder stays in lockstep with the bot.
See `feedback_pkhex_automod_lockstep`.
