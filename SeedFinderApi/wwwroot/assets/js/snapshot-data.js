/* ============================================================
   PKM UNIVERSE · SEED CONSOLE — bundled data snapshot
   ------------------------------------------------------------
   This file is the OFFLINE FALLBACK. When the site is served
   from *.pkm-universe.com it ignores all of this and pulls live
   data from /api/rotations, /api/leaderboard, /reviews.json and
   /api/search. Opened locally (file:// or localhost) it renders
   from this snapshot so the page always looks alive.
   Captured from production: 2026-08-07
   ============================================================ */

window.PKMU_SNAPSHOT = {
  capturedAt: "2026-08-07",

  /* ---- /api/rotations ------------------------------------- */
  rotations: [
    {
      map: "Paldea", online: true,
      now: {
        active: true, species: "Scovillain", form: 0, stars: 4, shiny: true,
        tera: "flying", sprite: "https://creator.pkm-universe.com/rs/spr/imgs/combat.png",
        meta: "4-star · Lv 45 · Impish", timer: -1, timerTotal: -1,
        joined: 0, capacity: 4, completed: 1640, wins: 1012, losses: 628,
        uptimeMin: 43545, switchOnline: true, discordOnline: true
      },
      queue: [
        { species: 847, name: "Barraskewda", shiny: true, stars: 5, requested: false },
        { species: 454, name: "Toxicroak",   shiny: true, stars: 5, requested: false },
        { species: 0,   name: "Mystery Shiny Raid", shiny: true, stars: 6, requested: true },
        { species: 0,   name: "Mystery Shiny Raid", shiny: true, stars: 3, requested: true }
      ]
    },
    {
      map: "Kitakami", online: true,
      now: {
        active: true, species: "Golem", form: 0, stars: 6, shiny: true,
        tera: "steel", sprite: "https://creator.pkm-universe.com/rs/home/128x128/poke_capture_0076_000_mf_n_00000000_f_r.png",
        meta: "6-star · Lv 75 · Impish", timer: -1, timerTotal: -1,
        joined: 0, capacity: 4, completed: 1123, wins: 700, losses: 423,
        uptimeMin: 39272, switchOnline: true, discordOnline: true
      },
      queue: [
        { species: 783, name: "Hakamoo",  shiny: true, stars: 5, requested: false },
        { species: 541, name: "Swadloon", shiny: true, stars: 5, requested: false },
        { species: 76,  name: "Golem",    shiny: true, stars: 6, requested: true },
        { species: 0,   name: "Mystery Shiny Raid", shiny: true, stars: 4, requested: true }
      ]
    },
    {
      map: "Blueberry", online: true,
      now: {
        active: true, species: "Electivire", form: 0, stars: 6, shiny: true,
        tera: "ghost", sprite: "https://creator.pkm-universe.com/rs/home/128x128/poke_capture_0466_000_mf_n_00000000_f_r.png",
        meta: "6-star · Lv 75 · Naive", timer: -1, timerTotal: -1,
        joined: 0, capacity: 4, completed: 7, wins: 4, losses: 2,
        uptimeMin: 1165, switchOnline: true, discordOnline: true
      },
      queue: [
        { species: 523, name: "Zebstrika",  shiny: true, stars: 5, requested: false },
        { species: 466, name: "Electivire", shiny: true, stars: 6, requested: true },
        { species: 233, name: "Porygon2",   shiny: true, stars: 5, requested: false }
      ]
    }
  ],

  /* ---- /reviews.json --------------------------------------
     Intentionally empty: no real reviews yet. The page renders
     the "founding reviews open" state until genuine quotes are
     added to reviews.json (project root / server). Never ship
     invented reviews here. ----------------------------------- */
  reviews: [],

  /* ---- /api/search preview pool ----------------------------
     Real seeds pulled from the production engine, used only when
     the live API is unreachable (local preview). -------------- */
  seeds: [
    { location: "Paldea", seed: "01575D88", species: 706, speciesName: "Goodra",      shiny: true, stars: 6, teraName: "Poison",  flawlessIVs: 6, iVs: [31,31,31,31,31,31], nature: "Mild",    gender: "Male",   scale: 73,  raCommand: "!ra 01575D88 6 6", ability: "Gooey",            hidden: true,  moves: [{name:"Dragon Pulse",type:"Dragon"},{name:"Surf",type:"Water"},{name:"Sludge Bomb",type:"Poison"},{name:"Power Whip",type:"Grass"}], rewards: [{name:"Ability Patch",qty:1},{name:"Exp. Candy L",qty:10},{name:"Exp. Candy XL",qty:1},{name:"Goomy Goo",qty:10}] },
    { location: "Paldea", seed: "0039440C", species: 128, speciesName: "Tauros",      shiny: true, stars: 6, teraName: "Ghost",   flawlessIVs: 5, iVs: [31,31,5,31,31,31],  nature: "Bashful", gender: "Male",   scale: 28,  raCommand: "!ra 0039440C 6 6", ability: "Cud Chew",         hidden: true,  moves: [{name:"Flare Blitz",type:"Fire"},{name:"Close Combat",type:"Fighting"},{name:"Flamethrower",type:"Fire"},{name:"Headbutt",type:"Normal"}], rewards: [{name:"Ability Capsule",qty:1},{name:"Bottle Cap",qty:1},{name:"Exp. Candy L",qty:5},{name:"Exp. Candy XL",qty:1}] },
    { location: "Paldea", seed: "00E4EB57", species: 873, speciesName: "Frosmoth",    shiny: true, stars: 6, teraName: "Dark",    flawlessIVs: 5, iVs: [31,20,31,31,31,31], nature: "Jolly",   gender: "Male",   scale: 224, raCommand: "!ra 00E4EB57 6 6", ability: "Ice Scales",       hidden: true,  moves: [{name:"Blizzard",type:"Ice"},{name:"Bug Buzz",type:"Bug"},{name:"Hurricane",type:"Flying"},{name:"Snowscape",type:"Ice"}], rewards: [{name:"Exp. Candy L",qty:6},{name:"Exp. Candy XL",qty:1},{name:"Snom Thread",qty:12},{name:"Clever Feather",qty:3}] },
    { location: "Paldea", seed: "023C3478", species: 959, speciesName: "Tinkaton",    shiny: true, stars: 6, teraName: "Ghost",   flawlessIVs: 5, iVs: [31,22,31,31,31,31], nature: "Quiet",   gender: "Female", scale: 166, raCommand: "!ra 023C3478 6 6", ability: "Pickpocket",       hidden: false, moves: [{name:"Gigaton Hammer",type:"Steel"},{name:"Play Rough",type:"Fairy"},{name:"Knock Off",type:"Dark"},{name:"Thunder Wave",type:"Electric"}], rewards: [{name:"Sour Herba Mystica",qty:2},{name:"Bottle Cap",qty:1},{name:"Exp. Candy L",qty:5},{name:"Exp. Candy XL",qty:1}] },
    { location: "Paldea", seed: "01C9CAC2", species: 998, speciesName: "Baxcalibur",  shiny: true, stars: 6, teraName: "Steel",   flawlessIVs: 5, iVs: [31,31,31,31,31,4],  nature: "Impish",  gender: "Male",   scale: 118, raCommand: "!ra 01C9CAC2 6 6", ability: "Thermal Exchange", hidden: false, moves: [{name:"Icicle Spear",type:"Ice"},{name:"Dragon Rush",type:"Dragon"},{name:"Snowscape",type:"Ice"},{name:"Body Press",type:"Fighting"}], rewards: [{name:"Exp. Candy L",qty:6},{name:"Exp. Candy XL",qty:2},{name:"Frigibax Scales",qty:10},{name:"Muscle Feather",qty:3}] },
    { location: "Paldea", seed: "00728130", species: 923, speciesName: "Pawmot",      shiny: true, stars: 6, teraName: "Grass",   flawlessIVs: 5, iVs: [31,31,31,31,31,5],  nature: "Serious", gender: "Female", scale: 41,  raCommand: "!ra 00728130 6 6", ability: "Iron Fist",        hidden: true,  moves: [{name:"Wild Charge",type:"Electric"},{name:"Close Combat",type:"Fighting"},{name:"Double Shock",type:"Electric"},{name:"Nuzzle",type:"Electric"}], rewards: [{name:"Ability Capsule",qty:2},{name:"Exp. Candy L",qty:5},{name:"Exp. Candy XL",qty:1},{name:"Pawmi Fur",qty:12}] },
    { location: "Paldea", seed: "03212669", species: 941, speciesName: "Kilowattrel", shiny: true, stars: 6, teraName: "Ground",  flawlessIVs: 5, iVs: [31,31,31,31,12,31], nature: "Mild",    gender: "Male",   scale: 206, raCommand: "!ra 03212669 6 6", ability: "Competitive",      hidden: true,  moves: [{name:"Hurricane",type:"Flying"},{name:"Thunder",type:"Electric"},{name:"Uproar",type:"Normal"},{name:"Scary Face",type:"Normal"}], rewards: [{name:"Sweet Herba Mystica",qty:2},{name:"Exp. Candy L",qty:6},{name:"Exp. Candy XL",qty:1},{name:"Wattrel Feather",qty:14}] },
    { location: "Paldea", seed: "00E4F030", species: 691, speciesName: "Dragalge",    shiny: true, stars: 6, teraName: "Grass",   flawlessIVs: 5, iVs: [0,31,31,31,31,31],  nature: "Hasty",   gender: "Female", scale: 149, raCommand: "!ra 00E4F030 6 6", ability: "Adaptability",     hidden: true,  moves: [{name:"Dragon Pulse",type:"Dragon"},{name:"Sludge Bomb",type:"Poison"},{name:"Water Pulse",type:"Water"},{name:"Toxic",type:"Poison"}], rewards: [{name:"Bottle Cap",qty:1},{name:"Exp. Candy L",qty:4},{name:"Exp. Candy XL",qty:1},{name:"Skrelp Kelp",qty:10}] },
    { location: "Kitakami", seed: "02758BD8", species: 36,  speciesName: "Clefable", shiny: true, stars: 4, teraName: "Ghost",  flawlessIVs: 3, iVs: [9,31,7,31,31,7],   nature: "Rash",    gender: "Female", scale: 2,   raCommand: "!ra 02758BD8 4 6", ability: "Magic Guard", hidden: false, moves: [{name:"Dazzling Gleam",type:"Fairy"},{name:"Psychic",type:"Psychic"},{name:"Misty Terrain",type:"Fairy"},{name:"Meteor Mash",type:"Steel"}], rewards: [{name:"Exp. Candy M",qty:3},{name:"Exp. Candy L",qty:1},{name:"Cleffa Fur",qty:6},{name:"Ghost Tera Shard",qty:3}] },
    { location: "Kitakami", seed: "02AEE849", species: 75,  speciesName: "Graveler", shiny: true, stars: 4, teraName: "Ground", flawlessIVs: 3, iVs: [7,24,17,31,31,31], nature: "Adamant", gender: "Female", scale: 249, raCommand: "!ra 02AEE849 4 6", ability: "Sturdy",      hidden: false, moves: [{name:"Stomping Tantrum",type:"Ground"},{name:"Rock Slide",type:"Rock"},{name:"Rock Tomb",type:"Rock"},{name:"Take Down",type:"Normal"}], rewards: [{name:"Exp. Candy M",qty:5},{name:"Exp. Candy L",qty:1},{name:"Geodude Fragment",qty:6},{name:"Ground Tera Shard",qty:3}] },
    { location: "Kitakami", seed: "00ABE3C5", species: 164, speciesName: "Noctowl",  shiny: true, stars: 4, teraName: "Normal", flawlessIVs: 3, iVs: [31,20,31,31,10,26],nature: "Jolly",   gender: "Female", scale: 192, raCommand: "!ra 00ABE3C5 4 6", ability: "Insomnia",    hidden: false, moves: [{name:"Air Slash",type:"Flying"},{name:"Extrasensory",type:"Psychic"},{name:"Moonblast",type:"Fairy"},{name:"Reflect",type:"Psychic"}], rewards: [{name:"Exp. Candy M",qty:2},{name:"Exp. Candy L",qty:1},{name:"Hoothoot Feather",qty:6},{name:"Normal Tera Shard",qty:4}] },
    { location: "Blueberry", seed: "011E381B", species: 164, speciesName: "Noctowl", shiny: true, stars: 4, teraName: "Ground", flawlessIVs: 3, iVs: [13,15,12,31,31,31],nature: "Naive",   gender: "Male",   scale: 98,  raCommand: "!ra 011E381B 4 6", ability: "Tinted Lens", hidden: true,  moves: [{name:"Air Slash",type:"Flying"},{name:"Extrasensory",type:"Psychic"},{name:"Moonblast",type:"Fairy"},{name:"Reflect",type:"Psychic"}], rewards: [{name:"Exp. Candy M",qty:2},{name:"Exp. Candy L",qty:1},{name:"Hoothoot Feather",qty:6},{name:"Ground Tera Shard",qty:3}] }
  ]
};
