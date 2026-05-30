<div align="center">

# PKM Universe Seed Finder

**Find a seed for any shiny Tera Raid Pokémon — then host it on Raid-SV with one click.**

[![Live site](https://img.shields.io/badge/live-seeds.pkm--universe.com-d4af37)](https://seeds.pkm-universe.com)
[![PKM Universe](https://img.shields.io/badge/web-creator.pkm--universe.com-d4af37)](https://creator.pkm-universe.com)
[![Discord](https://img.shields.io/badge/discord-PKM%20Universe%20Reborn-5865f2)](https://discord.gg/pkm-universe-reborn)

</div>

---

The public-facing companion to [PKM-Universe-SVRaidBot](https://github.com/PokemonLover8888/PKM-Universe-SVRaidBot). Reuses the **exact same RaidCrawler engine** the bot uses, so every seed it returns is guaranteed to produce the same Pokémon when the bot hosts it.

Live at **https://seeds.pkm-universe.com**.

## What it does

Type filters (or talk to it, or paste a screenshot) → get accurate Tera Raid seeds back as premium-looking cards → click **🚀 Host on Raid-SV** to queue them on the real bot.

### Core
- **Full SV Tera Raid seed search** — Game · Map · Story Progress · Stars · Species · Shiny · Tera type · Min Perfect IVs
- **Reverse + batch lookup** — paste bare seeds or full `!ra` commands to identify them
- **Species pool filtered to actual raid bosses** for the chosen map/stars/progress (24 species at Kitakami 6★, not the whole National Dex)

### Premium UI
- Holo foil + cursor-tracking parallax tilt on every result card
- IV radar (per-stat polygon, gold dots on 31s)
- Animated sprite + cry + sparkle burst on click
- Card flip → base stats + raid moves + ability
- Tera-colored ambient glow + watermark
- Discord embed preview modal + bulk copy `!ra` commands + confetti

### Discord + bot integration
- **Discord OAuth2 login with Member-role gate** for one-click host
- **🚀 Host on Raid-SV** button queues the seed directly on your running bot
- **OBS Browser-Source overlay** at `/overlay.html` for streamers
- **Real-time SSE push**: Now Hosting widget, Up Next queue, live activity feed (joins / completions / errors)

### 📱 PWA
- Installable as a phone app (manifest, icons, service worker)
- Caches app shell; never caches dynamic API calls

### 👤 Personal
- **My Hosted Raids** history (server-side, per Discord user)
- **⭐ Wishlist** (localStorage, per browser)
- **🏆 Community Leaderboard** — top hosts by week / month / all-time with podium UI

### 🤖 AI suite (Gemini)
- **🗣 Conversational chat** with multi-turn session memory
- **🎙 Voice search** (Web Speech API)
- **📷 Vision search** — drop / paste / upload a Pokémon screenshot, Gemini identifies it + searches
- **🧠 Raid Coach** — counter team + best moves + tera advice + pre-raid tip per result
- **✨ AI Announcer** — auto-generates a hype Discord post when you click Host
- **💡 Smart typeahead** — query completions as you type
- **📊 Result summary banner** — Gemini one-liner above results
- **🎲 Surprise me** — themed roll based on your history

### 🚀 Power features
- **🎮 3D Pokémon viewer** — Three.js scene with billboard sprite, tera-colored point light, glowing rings, 180 floating particles, drag-to-rotate, scroll-to-zoom, click-for-cry
- **🪧 AI Raid Party Builder** — 6-Pokémon counter team with item / ability / nature / tera / 4 moves / reasoning + Showdown-paste export + reroll
- **🔗 Permalink share** — every card has a 🔗 button that copies a shareable URL like `/r/02AEAC78?stars=6&story=6&loc=Kitakami`; recipients open the page with the seed pre-loaded as the hero card

## Architecture

```
Browser  ─▶  Static frontend (index.html, ~3000 lines: holo cards, AI, 3D, SSE)
                │
                ▼
        ASP.NET Core 9 minimal API  (Program.cs)
                │
                ├─▶  RaidCrawler.Core + PKHeX.Core   (seed → encounter, same as the bot)
                ├─▶  Gemini API                       (chat / vision / coach / party / etc.)
                ├─▶  Discord OAuth2                   (login + Member-role gate)
                └─▶  Raid-SV bot HTTP API             (/api/raid/now, /api/raid/queue, /api/raid/add)
```

The frontend talks to the same .NET backend that the bot exposes — so a seed found here will produce the exact same Pokémon when the bot hosts it. No re-implementing the RNG.

## Self-host

Requires Docker + a Cloudflare tunnel (or any HTTPS-fronting reverse proxy).

```bash
# 1. Build
docker build -t pkmu-seedfinder .

# 2. Configure (.env)
cat > .env <<EOF
DISCORD_CLIENT_ID=<your-discord-app-client-id>
DISCORD_CLIENT_SECRET=<your-discord-app-client-secret>
DISCORD_GUILD_ID=<your-server-id>
DISCORD_VERIFIED_ROLE_ID=<role-id-that-can-host>
DISCORD_REDIRECT=https://your-domain/api/auth/callback
PUBLIC_BASE=https://your-domain
GEMINI_API_KEY=<your-gemini-key>          # optional — disables AI features if absent
BOT_URL=http://host.docker.internal:9090  # where the Raid-SV bot HTTP API lives
EOF

# 3. Run
docker run -d --name pkmu-seedfinder --restart unless-stopped \
  --add-host=host.docker.internal:host-gateway \
  --env-file .env \
  -p 8090:8080 \
  pkmu-seedfinder
```

Point your tunnel/proxy at `localhost:8090`.

## Companion bot

This is half of the system. The other half — the actual Tera Raid host — is **[PKM-Universe-SVRaidBot](https://github.com/PokemonLover8888/PKM-Universe-SVRaidBot)**. The bot exposes a small HTTP API the Seed Finder talks to for "Now Hosting", "Up Next", and one-click host requests. Both are designed to be run on the same machine.

## Credits

Built on top of:
- **[RaidCrawler.Core](https://github.com/LegoFigure11/RaidCrawler)** — the SV raid seed RNG
- **[PKHeX.Core](https://github.com/kwsch/PKHeX)** — Pokémon data + legality
- **[PokeAPI sprites](https://github.com/PokeAPI/sprites)** + **[PokeAPI cries](https://github.com/PokeAPI/cries)**
- **[Three.js](https://threejs.org)** for the 3D viewer
- **Google Gemini** for the AI suite

---

PKM Universe Reborn · seeds work in `!ra <seed> <stars> <progress>` · [creator.pkm-universe.com](https://creator.pkm-universe.com)
