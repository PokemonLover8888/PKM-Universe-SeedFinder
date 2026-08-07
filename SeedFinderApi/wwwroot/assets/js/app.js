/* ============================================================
   PKM UNIVERSE · SEED CONSOLE — flagship page logic
   ------------------------------------------------------------
   Data strategy (in order):
     1. same-origin  /api/*  + /reviews.json   (deployed on the
        real domain → everything is fully live)
     2. cross-origin https://seeds.pkm-universe.com/*  (works if
        CORS allows; free live data even on localhost)
     3. window.PKMU_SNAPSHOT                     (bundled — page
        always renders, even from file://)
   Real-time: EventSource on /api/stream (SSE) drives the
   floating NOW HOSTING widget + new-raid toasts; 30 s polling
   remains as the universal fallback.
   ============================================================ */
(() => {
  "use strict";

  const ORIGIN = "https://seeds.pkm-universe.com";
  const SNAP = window.PKMU_SNAPSHOT || { rotations: [], reviews: [], seeds: [] };
  const onBrandDomain = /(^|\.)pkm-universe\.com$/.test(location.hostname);
  const $ = (s, r) => (r || document).querySelector(s);
  const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
  const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const finePointer = matchMedia("(hover: hover) and (pointer: fine)").matches;

  const TERA_COLORS = {
    normal: "#a8a77a", fire: "#ee8130", water: "#6390f0", electric: "#f7d02c",
    grass: "#7ac74c", ice: "#96d9d6", fighting: "#c22e28", poison: "#a33ea1",
    ground: "#e2bf65", flying: "#a98ff3", psychic: "#f95587", bug: "#a6b91a",
    rock: "#b6a136", ghost: "#8873b5", dragon: "#7d5cff", dark: "#8a7566",
    steel: "#b7b7ce", fairy: "#d685ad"
  };
  const TERA_TYPES = ["Normal","Fighting","Flying","Poison","Ground","Rock","Bug","Ghost","Steel","Fire","Water","Grass","Electric","Psychic","Ice","Dragon","Dark","Fairy"];
  const IV_LBLS = ["HP", "ATK", "DEF", "SPA", "SPD", "SPE"];

  /* ---------- tiny utils ---------- */
  const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  // allow only <b> from reviews.json (a file the site owner authors)
  const safeRich = (s) => esc(s).replace(/&lt;b&gt;/g, "<b>").replace(/&lt;\/b&gt;/g, "</b>");
  const stars = (n) => "★".repeat(Math.max(0, Math.min(6, n | 0)));
  const fmtInt = (n) => Number(n || 0).toLocaleString("en-US");
  const teraColor = (t) => TERA_COLORS[String(t || "").toLowerCase()] || "#d4af37";
  const artURL = (id) => `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/shiny/${id}.png`;
  const iconURL = (id) => `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/${id}.png`;
  // console app entry: same-origin file when deployed on the brand domain,
  // absolute root-hash otherwise (the deployed landing bounces those through)
  const APP = (route) => onBrandDomain ? `/console.html#/${route}` : `${ORIGIN}/#/${route}`;

  function toast(msg, ms) {
    const t = $("#toast");
    t.textContent = msg;
    t.classList.add("show");
    clearTimeout(toast._t);
    toast._t = setTimeout(() => t.classList.remove("show"), ms || 2400);
  }

  async function copyText(txt, label) {
    try {
      await navigator.clipboard.writeText(txt);
      toast(label || "COPIED — PASTE IT IN DISCORD");
    } catch {
      const ta = document.createElement("textarea");
      ta.value = txt; document.body.appendChild(ta); ta.select();
      try { document.execCommand("copy"); toast(label || "COPIED — PASTE IT IN DISCORD"); } catch { toast("COPY FAILED — LONG-PRESS TO COPY"); }
      ta.remove();
    }
  }

  /* ---------- data cascade ---------- */
  async function getJSON(path, init) {
    const tryFetch = async (base) => {
      const r = await fetch(base + path, init);
      if (!r.ok) throw new Error(r.status);
      return r.json();
    };
    if (onBrandDomain) {
      try { return { data: await tryFetch(""), live: true }; } catch { /* fall through */ }
    } else {
      try { return { data: await tryFetch(""), live: false, local: true }; } catch { /* fall through */ }
      try { return { data: await tryFetch(ORIGIN), live: true }; } catch { /* fall through */ }
    }
    return null;
  }

  /* ============================================================
     RENDERERS
     ============================================================ */
  let usingSnapshotRotations = true;

  function renderTicker(rots) {
    const bits = [];
    for (const r of rots) {
      const n = r.now || {};
      bits.push(`<span><i class="dot">●</i> LIVE · ${esc(r.map).toUpperCase()} <span class="dim">—</span> ${n.shiny ? "✦ SHINY " : ""}${esc(n.species || "?").toUpperCase()} ${stars(n.stars)}</span>`);
    }
    bits.push(`<span><i class="dot">◆</i> SAME ENGINE AS THE BOT <span class="dim">— 100% ACCURATE SEEDS</span></span>`);
    bits.push(`<span><i class="dot">✦</i> 4,294,967,296 SEEDS INDEXED <span class="dim">— THE FULL 2³² SPACE</span></span>`);
    bits.push(`<span><i class="dot">🚀</i> ONE-CLICK HOSTING <span class="dim">— STRAIGHT ONTO RAID-SV</span></span>`);
    bits.push(`<span><i class="dot">№</i> IND-2026 <span class="dim">— EST. INDIANAPOLIS</span></span>`);
    const html = bits.join("");
    $("#tickerTrack").innerHTML = html + html; // doubled for a seamless loop
  }

  function renderHeroLive(rots) {
    const live = rots.filter((r) => r.online);
    $("#heroBotCount").textContent = String(live.length || rots.length || 3);
    const pick = live.find((r) => r.now && r.now.active) || live[0];
    if (pick && pick.now) {
      const n = pick.now;
      $("#heroLiveTxt").textContent =
        `LIVE · ${pick.map.toUpperCase()} — ${n.shiny ? "✦ SHINY " : ""}${(n.species || "?").toUpperCase()} ${stars(n.stars)}`;
    }
  }

  function renderStats(rots) {
    let raids = 0, wins = 0, upMin = 0;
    for (const r of rots) {
      const n = r.now || {};
      raids += n.completed || 0; wins += n.wins || 0; upMin += n.uptimeMin || 0;
    }
    $("#statRaids").dataset.final = String(raids);
    $("#statWins").dataset.final = String(wins);
    $("#statUptime").dataset.final = String(Math.round(upMin / 1440));
    // odometers already on screen: retarget them
    $$(".stat-num").forEach((el) => { if (el.dataset.built) odoSet(el, fmtCounter(el, Number(el.dataset.final || 0))); });
  }

  function renderArenas(rots) {
    const grid = $("#arenaGrid");
    grid.innerHTML = rots.map((r) => {
      const n = r.now || {};
      const winPct = n.completed ? Math.round((n.wins / n.completed) * 100) : 0;
      const art = n.sprite
        ? `<img src="${esc(n.sprite)}" alt="${esc(n.species || "")}" referrerpolicy="no-referrer" onerror="this.outerHTML='<span class=art-fallback>✦</span>'">`
        : `<span class="art-fallback">✦</span>`;
      const queue = (r.queue || []).slice(0, 4).map((q) =>
        `<span class="q-pill${q.requested ? " req" : ""}"><i>${q.shiny ? "✦" : "·"}</i> ${esc(q.name)} ${stars(q.stars)}</span>`
      ).join("");
      return `
      <article class="arena reveal in">
        <div class="arena-top">
          <span class="arena-map">${esc(r.map).toUpperCase()}</span>
          <span class="arena-status${r.online ? "" : " off"}"><span class="live-dot"></span>${r.online ? "ONLINE" : "OFFLINE"}</span>
        </div>
        <div class="arena-now">
          <div class="arena-art">${art}</div>
          <div class="arena-species">
            ${n.shiny ? '<span class="shiny-tag">✦ SHINY</span>' : ""}
            <h3>${esc(n.species || "STANDBY")}</h3>
            <span class="arena-stars">${stars(n.stars)}</span>
            <div class="arena-meta">${esc(n.meta || "")}</div>
            ${n.tera ? `<span class="tera-chip" style="--tc:${teraColor(n.tera)}">${esc(n.tera)} TERA</span>` : ""}
          </div>
        </div>
        <div class="wl">
          <div class="wl-bar"><b style="width:${winPct}%"></b></div>
          <div class="wl-txt"><span><b>${fmtInt(n.completed)}</b> RAIDS</span><span><b>${fmtInt(n.wins)}</b> WINS · ${winPct}%</span></div>
        </div>
        <div class="arena-queue">
          <b class="q-label">NEXT UP</b>
          <div class="q-pills">${queue || '<span class="q-pill">rotation loading…</span>'}</div>
        </div>
        <div class="arena-cta"><a class="btn btn-ghost" href="${APP("live")}">⚔️ WATCH ${esc(r.map).toUpperCase()} LIVE</a></div>
      </article>`;
    }).join("");
  }

  /* ---------- reviews: real ones render; none yet → founding slots ---------- */
  function renderReviews(reviews) {
    const plaque = $("#ratingPlaque");
    const founding = $("#founding");
    const rowA = $("#reviewRowA"), rowB = $("#reviewRowB");

    if (!reviews || !reviews.length) {
      // honest empty state — no invented quotes, no fake score
      plaque.classList.add("open-season");
      $("#rpScore").textContent = "SEASON ONE";
      $("#rpStars").textContent = "★★★★★";
      $("#rpSub").innerHTML = `FOUNDING REVIEWS <b>NOW OPEN</b> · BE THE FIRST ON THE PLAQUE`;
      rowA.hidden = true; rowB.hidden = true;
      founding.hidden = false;
      let slots = "";
      for (let i = 1; i <= 11; i++) {
        slots += `
        <div class="f-slot">
          <span class="f-num">${String(i).padStart(2, "0")}</span>
          <span class="f-stars">★★★★★</span>
          <span class="f-lbl">RESERVED FOR A FOUNDING TRAINER</span>
        </div>`;
      }
      slots += `
        <a class="f-slot f-cta" href="https://discord.gg/pkm-universe-reborn" target="_blank" rel="noopener">
          <b>CLAIM SLOT 12</b>
          <span class="f-lbl">DROP YOUR REVIEW IN DISCORD</span>
        </a>`;
      founding.innerHTML = slots;
      return;
    }

    plaque.classList.remove("open-season");
    founding.hidden = true;
    rowA.hidden = false; rowB.hidden = reviews.length < 4;
    const sum = reviews.reduce((a, r) => a + (r.stars || 0), 0);
    const avg = sum / reviews.length;
    $("#rpScore").textContent = (Math.round(avg * 10) / 10).toFixed(1);
    $("#rpStars").textContent = "★★★★★";
    $("#rpSub").innerHTML = `<b>${reviews.length}</b> TRAINER REVIEW${reviews.length === 1 ? "" : "S"} · PKM UNIVERSE REBORN`;
    const card = (r) => `
      <article class="review">
        <span class="rv-stars">${"★".repeat(r.stars || 5)}${"☆".repeat(Math.max(0, 5 - (r.stars || 5)))}</span>
        <p>“${safeRich(r.text)}”</p>
        <div class="rv-who">
          <span class="rv-badge">${esc((r.name || "?").charAt(0).toUpperCase())}</span>
          <span><span class="rv-name">${esc(r.name)}</span><br><span class="rv-role">${esc(r.role || "Trainer")}</span></span>
          <span class="rv-when">${esc(r.when || "")}</span>
        </div>
      </article>`;
    const a = reviews.filter((_, i) => i % 2 === 0).map(card).join("");
    const b = reviews.filter((_, i) => i % 2 === 1).map(card).join("");
    $("#reviewRowA .rm-track").innerHTML = a + a;
    if (b) $("#reviewRowB .rm-track").innerHTML = b + b;
  }

  /* ---------- seed search ---------- */
  function seedCard(x, i) {
    const tc = teraColor(x.teraName);
    const ivs = (x.iVs || []).map((v, k) => `
      <span class="iv">
        <span class="iv-bar${v === 31 ? " max" : ""}"><b style="height:${Math.round((v / 31) * 100)}%"></b></span>
        <span class="iv-lbl">${IV_LBLS[k]}</span>
        <span class="iv-val">${v}</span>
      </span>`).join("");
    const moves = (x.moves || []).slice(0, 4).map((m) =>
      `<span class="mv" style="--tc:${teraColor(m.type)}">${esc(m.name)}</span>`).join("");
    return `
    <article class="seed-card" style="animation-delay:${i * 70}ms">
      <div class="sc-top">
        <div class="sc-art"><img src="${artURL(x.species)}" alt="shiny ${esc(x.speciesName)}" loading="lazy" onerror="this.outerHTML='<span class=art-fallback>✦</span>'"></div>
        <div class="sc-name">
          ${x.shiny ? '<span class="shiny-tag">✦ SHINY</span>' : ""}
          <h4>${esc(x.speciesName)}</h4>
          <span class="arena-stars">${stars(x.stars)}</span>
          <span class="tera-chip" style="--tc:${tc}">${esc(x.teraName)} TERA</span>
        </div>
      </div>
      <div class="sc-meta">
        <b>${x.flawlessIVs}× 31 IV</b> · ${esc(x.nature)} · ${esc(x.gender)} · scale ${x.scale}<br>
        ${esc(x.ability)}${x.hidden ? " <b>(HA)</b>" : ""}
      </div>
      <div class="ivs">${ivs}</div>
      <div class="mv-row">${moves}</div>
      <div class="sc-seedrow">
        <div>
          <span class="sc-seed">${esc(x.seed)}</span><br>
          <span class="sc-ra">host: <b>${esc(x.raCommand)}</b></span>
        </div>
        <button class="sc-copy" data-copy="${esc(x.raCommand)}">COPY !RA</button>
      </div>
    </article>`;
  }

  async function runSearch() {
    const grid = $("#seedGrid");
    const note = $("#seedNote");
    const mode = $("#consoleMode");
    const body = {
      game: "Scarlet",
      location: $("#qMap").value,
      storyProgress: 6,
      stars: Number($("#qStars").value),
      species: null, shiny: true,
      teraType: $("#qTera").value || null,
      minFlawlessIVs: $("#qIVs").value ? Number($("#qIVs").value) : null,
      maxResults: 6,
      nature: null, gender: null, hiddenAbility: null, scale: null,
      rewardItem: null, rewardMinQty: null, speciesList: null, bestOf: null, startSeed: null
    };
    grid.innerHTML = `<div class="seed-loading">CONSULTING THE ENGINE — SWEEPING 4,294,967,296 SEEDS…<span class="scan"></span></div>`;
    note.hidden = true;
    mode.textContent = "◈ SEARCHING…"; mode.classList.remove("live");

    const res = await getJSON("/api/search", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });

    let results, live = false;
    if (res && res.data && Array.isArray(res.data.results)) {
      results = res.data.results.slice(0, 6);
      live = true;
    } else {
      // cached preview: filter the bundled real-seed pool
      results = SNAP.seeds.filter((s) =>
        s.location === body.location &&
        (!body.stars || s.stars === body.stars) &&
        (!body.teraType || s.teraName === body.teraType) &&
        (!body.minFlawlessIVs || s.flawlessIVs >= body.minFlawlessIVs)
      ).slice(0, 6);
    }

    mode.textContent = live ? "● LIVE ENGINE" : "◌ CACHED PREVIEW";
    mode.classList.toggle("live", live);
    if (!live) {
      note.textContent = "◌ Offline preview — showing real seeds from a cached pool. Deploy on pkm-universe.com (or open the live console) for the full engine.";
      note.hidden = false;
    }

    if (!results.length) {
      grid.innerHTML = `<div class="seed-empty">NO SEEDS IN THIS SLICE OF THE 4.29B SPACE — WIDEN THE FILTERS AND SWEEP AGAIN.<br><br><a class="btn btn-ghost btn-sm" href="${APP("hunt")}">TRY THE FULL CHAMBER ➜</a></div>`;
      return;
    }
    grid.innerHTML = results.map(seedCard).join("");
  }

  const PRESETS = {
    perfect: { map: "Paldea", stars: "6", tera: "", ivs: "6" },
    fiveiv: { map: "Paldea", stars: "6", tera: "", ivs: "5" },
    kita: { map: "Kitakami", stars: "4", tera: "", ivs: "" }
  };

  /* ---------- species pool shelf (live only; hides itself offline) ---------- */
  const poolCache = {};
  async function loadPool() {
    const map = $("#qMap").value;
    const wrap = $("#pool");
    let list = poolCache[map];
    if (!list) {
      const res = await getJSON(`/api/species?map=${encodeURIComponent(map)}&progress=6`);
      if (res && Array.isArray(res.data)) list = poolCache[map] = res.data;
    }
    if (!list || !list.length) { wrap.hidden = true; return; }
    wrap.hidden = false;
    $("#poolCount").textContent = `${fmtInt(list.length)} SPECIES IN THE ${map.toUpperCase()} POST-GAME POOL`;
    const shown = list.slice(0, 42);
    $("#poolShelf").innerHTML =
      shown.map((s) => `<span class="pool-mon" title="${esc(s.name)}"><img src="${iconURL(s.id)}" alt="${esc(s.name)}" loading="lazy" onerror="this.parentNode.remove()"></span>`).join("") +
      (list.length > shown.length
        ? `<a class="pool-more" href="${APP("dex")}">+${fmtInt(list.length - shown.length)} MORE<br>IN THE DEX ➜</a>`
        : "");
  }

  /* ============================================================
     EFFECTS & INTERACTIONS
     ============================================================ */
  function initStarfield() {
    const sf = $("#starfield");
    const n = Math.min(30, Math.max(14, Math.floor(innerWidth / 46)));
    let html = "";
    for (let i = 0; i < n; i++) {
      const glyph = Math.random() > 0.5 ? "✦" : "✧";
      html += `<i style="left:${(Math.random() * 100).toFixed(2)}%;top:${(Math.random() * 100).toFixed(2)}%;font-size:${(6 + Math.random() * 9).toFixed(1)}px;animation-delay:${(Math.random() * 6).toFixed(2)}s;animation-duration:${(4.5 + Math.random() * 5).toFixed(2)}s">${glyph}</i>`;
    }
    sf.innerHTML = html;
  }

  /* hero headline — split into rising letters */
  function initLetters() {
    const h1 = $("#heroTitle");
    if (!h1 || reducedMotion) return;
    let i = 0;
    const wrap = (node) => {
      [...node.childNodes].forEach((n) => {
        if (n.nodeType === 3) {
          const frag = document.createDocumentFragment();
          for (const ch of n.textContent) {
            if (/\s/.test(ch)) { frag.appendChild(document.createTextNode(ch)); continue; }
            const s = document.createElement("i");
            s.className = "hh"; s.style.setProperty("--i", i++); s.textContent = ch;
            frag.appendChild(s);
          }
          n.replaceWith(frag);
        } else if (n.nodeType === 1 && n.classList.contains("foil")) {
          n.classList.add("hh"); n.style.setProperty("--i", i); i += 3;
        }
      });
    };
    wrap(h1);
  }

  /* odometer counters */
  function fmtCounter(el, v) {
    const fmt = el.dataset.fmt;
    if (fmt === "big") return v >= 1e9 ? (v / 1e9).toFixed(2) + "B" : fmtInt(v);
    if (fmt === "days") return fmtInt(v);
    if (fmt === "plus") return fmtInt(v) + "+";
    return fmtInt(v);
  }
  function odoBuild(el, str) {
    const digitsOnly = str.replace(/[^0-9]/g, "").length;
    let d = 0, html = '<span class="odo">';
    for (const ch of str) {
      if (/[0-9]/.test(ch)) {
        // right-most digits roll last for the classic odometer feel
        const delay = (digitsOnly - 1 - d) * 70;
        html += `<span class="odo-d"><span class="odo-strip" style="--dd:${delay}ms">` +
          "0123456789".split("").map((n) => `<i>${n}</i>`).join("") +
          "</span></span>";
        d++;
      } else {
        html += `<span class="odo-c">${esc(ch)}</span>`;
      }
    }
    el.innerHTML = html + "</span>";
    el.dataset.built = str.replace(/[0-9]/g, "#");
  }
  function odoSet(el, str) {
    const pattern = str.replace(/[0-9]/g, "#");
    if (el.dataset.built !== pattern) {
      odoBuild(el, str);
      // let the zeros paint once, then roll to the target
      setTimeout(() => odoSet(el, str), 60);
      return;
    }
    const strips = $$(".odo-strip", el);
    const digits = str.replace(/[^0-9]/g, "");
    strips.forEach((s, i) => { s.style.transform = `translateY(-${Number(digits[i])}em)`; });
    el.dataset.done = "1";
  }

  function initObservers() {
    const io = new IntersectionObserver((es) => {
      es.forEach((e) => { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } });
    }, { threshold: 0.12 });
    $$(".reveal").forEach((el) => io.observe(el));

    const co = new IntersectionObserver((es) => {
      es.forEach((e) => {
        if (e.isIntersecting) {
          const el = e.target;
          odoSet(el, fmtCounter(el, Number(el.dataset.final || 0)));
          co.unobserve(el);
        }
      });
    }, { threshold: 0.4 });
    $$(".stat-num").forEach((el) => co.observe(el));

    // active nav / dock highlighting
    const sections = ["live", "summon", "arsenal", "engine", "acclaim", "guide"].map((id) => document.getElementById(id)).filter(Boolean);
    const so = new IntersectionObserver((es) => {
      es.forEach((e) => {
        if (!e.isIntersecting) return;
        const id = e.target.id;
        $$(".mainnav a, .dock a").forEach((a) => a.classList.toggle("active", a.getAttribute("href") === "#" + id));
      });
    }, { rootMargin: "-40% 0px -55% 0px" });
    sections.forEach((s) => so.observe(s));
  }

  /* terminal typing loop */
  const TERM_LINES = [
    ["$ ", "seed.verify ", ["tg", "01575D88"]],
    ["→ species ", ["tg", "GOODRA"], "  ", ["tg", "★★★★★★"], "  shiny ", ["tk", "YES"], "  tera ", ["tc", "POISON"]],
    ["→ IVs ", ["tg", "31/31/31/31/31/31"], " · nature MILD · ", ["tg", "GOOEY"]],
    ["→ engine RaidCrawler core · PKHeX.Core data"],
    ["→ parity check vs Raid-SV host … ", ["tk", "MATCH 100.000%"]],
    ["✓ ", ["tk", "VERIFIED"], " — what the console shows is what the den spawns"]
  ];
  function termHTML(upTo, partialLen) {
    let out = "";
    for (let i = 0; i <= upTo && i < TERM_LINES.length; i++) {
      let line = "";
      let budget = i === upTo ? partialLen : Infinity;
      for (const seg of TERM_LINES[i]) {
        const [cls, txt] = Array.isArray(seg) ? seg : [null, seg];
        if (budget <= 0) break;
        const t = txt.slice(0, budget);
        budget -= t.length;
        line += cls ? `<span class="${cls}">${esc(t)}</span>` : esc(t);
      }
      out += line + (i < upTo ? "\n" : "");
    }
    return out;
  }
  function initTerminal() {
    const el = $("#termBody");
    if (!el) return;
    const lineLen = (i) => TERM_LINES[i].reduce((a, s) => a + (Array.isArray(s) ? s[1] : s).length, 0);
    if (reducedMotion) { el.innerHTML = termHTML(TERM_LINES.length - 1, Infinity); return; }
    let line = 0, chars = 0, started = false;
    const step = () => {
      if (line >= TERM_LINES.length) {
        el.innerHTML = termHTML(TERM_LINES.length - 1, Infinity) + '<span class="caret"></span>';
        setTimeout(() => { line = 0; chars = 0; step(); }, 5200);
        return;
      }
      chars += 2;
      el.innerHTML = termHTML(line, chars) + '<span class="caret"></span>';
      if (chars >= lineLen(line)) { line++; chars = 0; setTimeout(step, line === 1 ? 420 : 240); }
      else setTimeout(step, 18);
    };
    const io = new IntersectionObserver((es) => {
      if (es.some((e) => e.isIntersecting) && !started) { started = true; step(); io.disconnect(); }
    }, { threshold: 0.3 });
    io.observe(el);
  }

  /* crystal → sparkle burst + SHINY MODE */
  function burst(x, y) {
    for (let i = 0; i < 16; i++) {
      const s = document.createElement("span");
      s.className = "burst";
      s.textContent = Math.random() > 0.4 ? "✦" : "✧";
      const ang = (Math.PI * 2 * i) / 16 + Math.random() * 0.6;
      const dist = 60 + Math.random() * 110;
      s.style.left = x + "px"; s.style.top = y + "px";
      s.style.fontSize = 10 + Math.random() * 14 + "px";
      s.style.setProperty("--bx", Math.cos(ang) * dist + "px");
      s.style.setProperty("--by", Math.sin(ang) * dist + "px");
      document.body.appendChild(s);
      setTimeout(() => s.remove(), 1100);
    }
  }
  function setShiny(on, quiet) {
    document.documentElement.classList.toggle("shiny", on);
    try { localStorage.setItem("pkmu-shiny", on ? "1" : "0"); } catch {}
    if (!quiet) toast(on ? "✦ SHINY MODE ACTIVATED — 1 IN 4,096 ENERGY" : "◆ GOLD STANDARD RESTORED");
  }
  function initCrystal() {
    const c = $("#crystal");
    c.addEventListener("click", () => {
      const r = c.getBoundingClientRect();
      burst(r.left + r.width / 2, r.top + r.height / 2);
      setShiny(!document.documentElement.classList.contains("shiny"));
    });
    document.addEventListener("keydown", (e) => {
      if (e.key.toLowerCase() === "s" && !/input|select|textarea/i.test(e.target.tagName)) {
        setShiny(!document.documentElement.classList.contains("shiny"));
      }
    });
    try { if (localStorage.getItem("pkmu-shiny") === "1") setShiny(true, true); } catch {}
  }

  /* ---------- pointer FX: tilt + spotlight + cursor orb + magnetic CTAs ---------- */
  function initPointerFX() {
    if (!finePointer || reducedMotion) return;
    const TILT_SEL = ".arena,.feat,.seed-card,.review,.step";
    const orb = $("#cursorOrb");
    let mx = innerWidth / 2, my = innerHeight / 2, ox = mx, oy = my;
    let orbOn = false, raf = null;

    const loop = () => {
      ox += (mx - ox) * 0.22; oy += (my - oy) * 0.22;
      orb.style.transform = `translate(${ox}px, ${oy}px)${orb.classList.contains("hot") ? " scale(2.1)" : ""}`;
      if (Math.abs(mx - ox) + Math.abs(my - oy) > 0.4) raf = requestAnimationFrame(loop);
      else raf = null;
    };

    document.addEventListener("mousemove", (e) => {
      mx = e.clientX; my = e.clientY;
      if (!orbOn) { orb.classList.add("on"); orbOn = true; }
      if (!raf) raf = requestAnimationFrame(loop);
      orb.classList.toggle("hot", !!e.target.closest("a,button,select"));

      // card tilt + spotlight
      const card = e.target.closest(TILT_SEL);
      if (card) {
        const r = card.getBoundingClientRect();
        const px = (e.clientX - r.left) / r.width, py = (e.clientY - r.top) / r.height;
        card.style.setProperty("--ry", ((px - 0.5) * 5).toFixed(2) + "deg");
        card.style.setProperty("--rx", ((0.5 - py) * 5).toFixed(2) + "deg");
        card.style.setProperty("--mx", (px * 100).toFixed(1) + "%");
        card.style.setProperty("--my", (py * 100).toFixed(1) + "%");
      }

      // magnetic pull on primary CTAs
      for (const b of $$(".cta-row .btn, .console-actions .btn-gold, .nw-go")) {
        const r = b.getBoundingClientRect();
        if (!r.width) continue;
        const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
        const dx = e.clientX - cx, dy = e.clientY - cy;
        const d = Math.hypot(dx, dy);
        const reach = 110;
        if (d < reach) {
          const f = (1 - d / reach) * 7;
          b.style.setProperty("--magx", (dx / d * f || 0).toFixed(1) + "px");
          b.style.setProperty("--magy", (dy / d * f || 0).toFixed(1) + "px");
        } else if (b.style.getPropertyValue("--magx")) {
          b.style.setProperty("--magx", "0px"); b.style.setProperty("--magy", "0px");
        }
      }
    }, { passive: true });

    document.addEventListener("mouseout", (e) => {
      const card = e.target.closest && e.target.closest(TILT_SEL);
      if (card && !card.contains(e.relatedTarget)) {
        card.style.setProperty("--rx", "0deg"); card.style.setProperty("--ry", "0deg");
      }
    }, { passive: true });

    document.addEventListener("mouseleave", () => { orb.classList.remove("on"); orbOn = false; });

    // crystal parallax toward the cursor
    const stage = $(".crystal-orbit");
    document.addEventListener("mousemove", (e) => {
      const r = stage.getBoundingClientRect();
      if (!r.width || r.bottom < 0 || r.top > innerHeight) return;
      const dx = (e.clientX - (r.left + r.width / 2)) / innerWidth;
      const dy = (e.clientY - (r.top + r.height / 2)) / innerHeight;
      stage.style.transform = `translate(${(dx * 14).toFixed(1)}px, ${(dy * 10).toFixed(1)}px)`;
    }, { passive: true });
  }

  /* magnetic transform hook (composes with keycap press) */
  function initMagneticCSS() {
    if (!finePointer || reducedMotion) return;
    const st = document.createElement("style");
    st.textContent = `.btn{transform:translate(var(--magx,0px),var(--magy,0px));}.btn:active{transform:translate(var(--magx,0px),calc(var(--magy,0px) + 2px));}`;
    document.head.appendChild(st);
  }

  /* ---------- gold dust canvas (desktop hero) ---------- */
  function initDust() {
    const cv = $("#dust");
    if (!cv || reducedMotion || !finePointer || innerWidth < 941) { if (cv) cv.remove(); return; }
    const ctx = cv.getContext("2d");
    const dpr = Math.min(2, devicePixelRatio || 1);
    let W, H, parts = [], running = false;
    const size = () => {
      const r = cv.parentElement.getBoundingClientRect();
      W = cv.width = Math.floor(r.width * dpr); H = cv.height = Math.floor(r.height * dpr);
      cv.style.width = r.width + "px"; cv.style.height = r.height + "px";
    };
    const spawn = () => {
      parts = Array.from({ length: 46 }, () => ({
        x: Math.random() * W, y: Math.random() * H,
        r: (0.6 + Math.random() * 1.7) * dpr,
        vy: (0.12 + Math.random() * 0.3) * dpr,
        sway: Math.random() * Math.PI * 2,
        sp: 0.003 + Math.random() * 0.008,
        a: 0.12 + Math.random() * 0.4
      }));
    };
    const gold = () => document.documentElement.classList.contains("shiny") ? "200,140,255" : "230,195,90";
    const frame = () => {
      if (!running) return;
      ctx.clearRect(0, 0, W, H);
      const g = gold();
      for (const p of parts) {
        p.y -= p.vy; p.sway += p.sp;
        p.x += Math.sin(p.sway) * 0.35 * dpr;
        if (p.y < -8) { p.y = H + 8; p.x = Math.random() * W; }
        ctx.beginPath();
        ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
        ctx.fillStyle = `rgba(${g},${p.a})`;
        ctx.shadowColor = `rgba(${g},.8)`; ctx.shadowBlur = 6 * dpr;
        ctx.fill();
      }
      requestAnimationFrame(frame);
    };
    size(); spawn();
    addEventListener("resize", () => { size(); spawn(); }, { passive: true });
    const io = new IntersectionObserver((es) => {
      const vis = es.some((e) => e.isIntersecting);
      if (vis && !running) { running = true; requestAnimationFrame(frame); }
      else if (!vis) running = false;
    });
    io.observe(cv);
  }

  /* ---------- hero parallax on scroll ---------- */
  function initParallax() {
    if (reducedMotion || !finePointer) return;
    const copy = $(".hero-copy"), stage = $(".hero-stage"), hero = $("#hero");
    let ticking = false;
    addEventListener("scroll", () => {
      if (ticking) return;
      ticking = true;
      requestAnimationFrame(() => {
        ticking = false;
        const y = Math.min(scrollY, hero.offsetHeight);
        copy.style.transform = `translateY(${(y * 0.16).toFixed(1)}px)`;
        copy.style.opacity = String(Math.max(0, 1 - y / (hero.offsetHeight * 0.9)));
        stage.style.marginTop = (y * 0.06).toFixed(1) + "px";
      });
    }, { passive: true });
  }

  /* ---------- NOW HOSTING widget + SSE real-time feed ---------- */
  const NW = { lastKey: "", dismissed: false };
  function renderNow(state, src) {
    if (NW.dismissed || !state || !state.species) return;
    const w = $("#nowWidget");
    $("#nwName").textContent = (state.shiny ? "✦ " : "") + state.species;
    $("#nwStars").textContent = stars(state.stars);
    $("#nwMeta").textContent = state.meta || "";
    $("#nwSrc").textContent = src === "sse" ? "· REAL-TIME" : "";
    const img = $("#nwArt");
    if (state.sprite && img.src !== state.sprite) { img.src = state.sprite; img.alt = state.species; }
    w.hidden = false;
    const key = state.species + "|" + state.stars + "|" + (state.tera || "");
    if (NW.lastKey && NW.lastKey !== key) {
      toast(`🔴 NEW RAID LIVE — ${state.shiny ? "✦ SHINY " : ""}${state.species.toUpperCase()} ${stars(state.stars)}`, 3200);
      if (src === "sse") loadRotations(); // keep arenas in sync with reality
    }
    NW.lastKey = key;
  }
  function initStream() {
    try { NW.dismissed = sessionStorage.getItem("pkmu-nw-off") === "1"; } catch {}
    $("#nwClose").addEventListener("click", () => {
      $("#nowWidget").hidden = true;
      NW.dismissed = true;
      try { sessionStorage.setItem("pkmu-nw-off", "1"); } catch {}
    });
    if (NW.dismissed) return;
    const url = (onBrandDomain ? "" : ORIGIN) + "/api/stream";
    try {
      const es = new EventSource(url);
      es.addEventListener("state", (ev) => {
        try { renderNow(JSON.parse(ev.data), "sse"); } catch {}
      });
      es.onerror = () => { es.close(); }; // polling still feeds the widget
    } catch { /* EventSource unavailable — polling covers it */ }
  }

  /* HQ clock — Indianapolis time */
  function initClock() {
    const fmt = new Intl.DateTimeFormat("en-US", { timeZone: "America/Indiana/Indianapolis", hour12: false, hour: "2-digit", minute: "2-digit", second: "2-digit" });
    const tick = () => {
      const t = fmt.format(new Date());
      const a = $("#hqClock"), b = $("#hqClockFoot");
      if (a) a.textContent = t + " HQ";
      if (b) b.textContent = t;
    };
    tick(); setInterval(tick, 1000);
  }

  /* ---------- service worker ---------- */
  function initSW() {
    if (!("serviceWorker" in navigator)) return;
    const isLocal = /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
    if (location.protocol !== "https:" && !isLocal) return;
    // on localhost only register when asked (?sw=1) so dev reloads never fight the cache
    if (isLocal && !location.search.includes("sw=1")) return;
    addEventListener("load", () => navigator.serviceWorker.register("sw.js").catch(() => {}));
  }

  /* ============================================================
     BOOT
     ============================================================ */
  async function loadRotations() {
    const res = await getJSON("/api/rotations");
    const rots = res && Array.isArray(res.data) && res.data.length ? res.data : SNAP.rotations;
    usingSnapshotRotations = !(res && res.data && res.data.length);
    $("#arenaNote").hidden = !usingSnapshotRotations;
    renderTicker(rots); renderHeroLive(rots); renderStats(rots); renderArenas(rots);
    // feed the widget from polling when SSE isn't running
    const featured = rots.find((r) => r.online && r.now && r.now.active);
    if (featured) renderNow(featured.now, "poll");
  }

  async function loadReviews() {
    // unique query defeats any stale service-worker cache of the old reviews file
    const res = await getJSON("/reviews.json?ts=" + Date.now(), { cache: "no-store" });
    const reviews = res && res.data && Array.isArray(res.data.reviews) ? res.data.reviews : SNAP.reviews;
    renderReviews(reviews);
  }

  function initUI() {
    // point every console link at the right entry for this origin
    $$("[data-app-link]").forEach((a) => { a.href = APP(a.dataset.appLink); });
    // topbar
    const tb = $("#topbar");
    addEventListener("scroll", () => tb.classList.toggle("scrolled", scrollY > 10), { passive: true });
    // mobile nav
    const nav = $("#mainnav"), tog = $("#navToggle");
    tog.addEventListener("click", () => {
      const open = nav.classList.toggle("open");
      tog.setAttribute("aria-expanded", String(open));
    });
    nav.addEventListener("click", (e) => { if (e.target.tagName === "A") { nav.classList.remove("open"); tog.setAttribute("aria-expanded", "false"); } });
    // console
    $("#qGo").addEventListener("click", runSearch);
    $$(".preset").forEach((p) => p.addEventListener("click", () => {
      const cfg = PRESETS[p.dataset.preset];
      if (!cfg) return;
      $("#qMap").value = cfg.map; $("#qStars").value = cfg.stars;
      $("#qTera").value = cfg.tera; $("#qIVs").value = cfg.ivs;
      loadPool();
      runSearch();
    }));
    $("#qMap").addEventListener("change", loadPool);
    // tera options
    const sel = $("#qTera");
    TERA_TYPES.forEach((t) => { const o = document.createElement("option"); o.value = t; o.textContent = t; sel.appendChild(o); });
    // copy buttons (delegated)
    document.addEventListener("click", (e) => {
      const b = e.target.closest("[data-copy]");
      if (b) copyText(b.dataset.copy, "!RA COMMAND COPIED — PASTE IT IN DISCORD");
    });
  }

  document.addEventListener("DOMContentLoaded", () => {
    initStarfield(); initLetters(); initObservers(); initTerminal(); initCrystal(); initClock();
    initUI(); initPointerFX(); initMagneticCSS(); initDust(); initParallax(); initStream(); initSW();
    loadRotations(); loadReviews(); loadPool();
    // refresh live data while the tab is visible
    setInterval(() => { if (!document.hidden) loadRotations(); }, 30000);
  });
})();
