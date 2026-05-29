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

## Docker (matches your tiktok-bridge / trade-bridge pattern)
```
docker build -t pkmu-seedfinder .
docker run -d --name pkmu-seedfinder --restart unless-stopped -p 8089:8080 pkmu-seedfinder
```

## nginx — expose at creator.pkm-universe.com/seeds
```nginx
location /seeds/ {
    proxy_pass http://127.0.0.1:8089/;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
}
```
(Or give it its own subdomain, e.g. `seeds.pkm-universe.com`, like your other tools.)

## Updating for new game versions
When Scarlet/Violet updates, refresh the engine DLLs in `SeedFinderApi/libs/` from the
Raid-SV bot's deps (`SysBot.Pokemon/deps/`) so the finder stays in lockstep with the bot.
See `feedback_pkhex_automod_lockstep`.
