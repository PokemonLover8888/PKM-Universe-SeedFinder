using System.Collections.Concurrent;
using System.Diagnostics;
using PKHeX.Core;
using RaidCrawler.Core.Structures;

// PKM Universe Seed Finder API
// Reuses the SAME RaidCrawler.Core / PKHeX.Core engine the Raid-SV bot uses, so any
// seed it returns is guaranteed to produce the same Pokémon when the bot hosts it.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RaidEngine>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
// Force no-cache on the service worker + HTML shell so deploys propagate immediately past
// Cloudflare. Sprites/JSON/etc. keep the long cache they already use.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        if (name == "sw.js" || name == "index.html" || name == "overlay.html" || name == "demo.html")
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    }
});

// Warm the engine on boot so the first request isn't slow.
app.Services.GetRequiredService<RaidEngine>().Warm();

app.MapGet("/api/health", () => Results.Ok(new { ok = true, service = "pkm-universe-seed-finder" }));

// Live "Now Hosting" — proxies the Raid-SV bot's read-only status (bot listens on the host
// at :9090). Browser can't reach the bot directly, so the finder relays it.
var botHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
var botBase = Environment.GetEnvironmentVariable("BOT_URL") ?? "http://host.docker.internal:9090";
app.MapGet("/api/nowhosting", async () =>
{
    try
    {
        var json = await botHttp.GetStringAsync($"{botBase}/api/raid/now");
        return Results.Content(json, "application/json");
    }
    catch { return Results.Json(new { active = false, offline = true }); }
});

app.MapGet("/api/queue", async () =>
{
    try { return Results.Content(await botHttp.GetStringAsync($"{botBase}/api/raid/queue"), "application/json"); }
    catch { return Results.Content("[]", "application/json"); }
});

// --- Server-Sent Events: real-time push of raid state + activity ticker ---
// The finder polls the bot ~once/sec and broadcasts changes to all connected browsers.
var sseSubs = new System.Collections.Concurrent.ConcurrentDictionary<Guid, System.Threading.Channels.Channel<string>>();

app.MapGet("/api/stream", async (HttpContext ctx) =>
{
    ctx.Response.Headers["Content-Type"]    = "text/event-stream";
    ctx.Response.Headers["Cache-Control"]   = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    var ch = System.Threading.Channels.Channel.CreateUnbounded<string>();
    var id = Guid.NewGuid();
    sseSubs[id] = ch;
    try
    {
        // immediate hello so the client knows it's connected
        await ctx.Response.WriteAsync("event: hello\ndata: {\"ok\":true}\n\n");
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        await foreach (var msg in ch.Reader.ReadAllAsync(ctx.RequestAborted))
        {
            await ctx.Response.WriteAsync(msg);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    catch { }
    finally { sseSubs.TryRemove(id, out _); }
});

void Broadcast(string evt, string json)
{
    var msg = $"event: {evt}\ndata: {json}\n\n";
    foreach (var kv in sseSubs) kv.Value.Writer.TryWrite(msg);
}

// Background poller: detects state changes and synthesizes a live activity feed.
_ = Task.Run(async () =>
{
    bool prevActive = false; int prevJoined = 0; int prevCompleted = 0; string prevSpecies = "";
    string prevQueueHash = "";
    while (true)
    {
        try
        {
            var nowJson = await botHttp.GetStringAsync($"{botBase}/api/raid/now");
            using var doc = System.Text.Json.JsonDocument.Parse(nowJson);
            var st = doc.RootElement;
            bool active = st.GetProperty("active").GetBoolean();
            int joined = st.GetProperty("joined").GetInt32();
            int completed = st.GetProperty("completed").GetInt32();
            string species = st.GetProperty("species").GetString() ?? "";
            bool shiny = st.GetProperty("shiny").GetBoolean();
            int stars = st.GetProperty("stars").GetInt32();
            string tera = st.GetProperty("tera").GetString() ?? "";

            // feed events on transitions
            if (active && !prevActive && species.Length > 0)
                Broadcast("feed", System.Text.Json.JsonSerializer.Serialize(new { icon = "🎯", text = $"New raid hosting: {(shiny ? "✦ Shiny " : "")}{species} · {stars}★ · {tera} Tera" }));
            else if (!active && prevActive)
                Broadcast("feed", System.Text.Json.JsonSerializer.Serialize(new { icon = "⏸", text = "Raid ended — preparing next…" }));
            if (joined > prevJoined && active)
                Broadcast("feed", System.Text.Json.JsonSerializer.Serialize(new { icon = "✦", text = $"Trainer joined — {joined}/{st.GetProperty("capacity").GetInt32()} in lobby" }));
            if (completed > prevCompleted)
                Broadcast("feed", System.Text.Json.JsonSerializer.Serialize(new { icon = "🏆", text = $"Raid completed — total: {completed}" }));

            // always broadcast latest state
            Broadcast("state", nowJson);

            // queue diff → push when changed
            try
            {
                var qJson = await botHttp.GetStringAsync($"{botBase}/api/raid/queue");
                var qHash = qJson.Length + ":" + qJson.GetHashCode().ToString();
                if (qHash != prevQueueHash) { Broadcast("queue", qJson); prevQueueHash = qHash; }
            }
            catch { }

            prevActive = active; prevJoined = joined; prevCompleted = completed; prevSpecies = species;
        }
        catch { }
        await Task.Delay(1000);
    }
});

// --- Discord OAuth gate (one-click Host, Member-role only) ---
var dClientId = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID") ?? "";
var dSecret   = Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET") ?? "";
var dGuild    = Environment.GetEnvironmentVariable("DISCORD_GUILD_ID") ?? "";
var dRole     = Environment.GetEnvironmentVariable("DISCORD_VERIFIED_ROLE_ID") ?? "";
var dRedirect = Environment.GetEnvironmentVariable("DISCORD_REDIRECT") ?? "https://seeds.pkm-universe.com/api/auth/callback";
var publicBase= Environment.GetEnvironmentVariable("PUBLIC_BASE") ?? "https://seeds.pkm-universe.com";
var sessions  = new System.Collections.Concurrent.ConcurrentDictionary<string, (string Id, string Name, bool Verified)>();
var dHttp     = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

app.MapGet("/api/auth/login", () =>
    Results.Redirect($"https://discord.com/oauth2/authorize?client_id={dClientId}&redirect_uri={Uri.EscapeDataString(dRedirect)}&response_type=code&scope=identify%20guilds.members.read"));

app.MapGet("/api/auth/callback", async (HttpContext ctx, string? code) =>
{
    if (string.IsNullOrEmpty(code)) return Results.Redirect(publicBase);
    try
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = dClientId, ["client_secret"] = dSecret, ["grant_type"] = "authorization_code",
            ["code"] = code, ["redirect_uri"] = dRedirect
        });
        var tokJson = await (await dHttp.PostAsync("https://discord.com/api/oauth2/token", form)).Content.ReadAsStringAsync();
        using var tokDoc = System.Text.Json.JsonDocument.Parse(tokJson);
        if (!tokDoc.RootElement.TryGetProperty("access_token", out var atEl)) return Results.Redirect(publicBase + "?login=failed");
        var at = atEl.GetString();

        var meReq = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
        meReq.Headers.Authorization = new("Bearer", at);
        using var meDoc = System.Text.Json.JsonDocument.Parse(await (await dHttp.SendAsync(meReq)).Content.ReadAsStringAsync());
        var uid = meDoc.RootElement.GetProperty("id").GetString();
        var uname = meDoc.RootElement.TryGetProperty("global_name", out var gn) && gn.ValueKind == System.Text.Json.JsonValueKind.String
            ? gn.GetString() : meDoc.RootElement.GetProperty("username").GetString();

        bool verified = false;
        var memReq = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/users/@me/guilds/{dGuild}/member");
        memReq.Headers.Authorization = new("Bearer", at);
        var memResp = await dHttp.SendAsync(memReq);
        if (memResp.IsSuccessStatusCode)
        {
            using var memDoc = System.Text.Json.JsonDocument.Parse(await memResp.Content.ReadAsStringAsync());
            if (memDoc.RootElement.TryGetProperty("roles", out var rolesEl) && rolesEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var r in rolesEl.EnumerateArray()) if (r.GetString() == dRole) { verified = true; break; }
        }

        var token = Guid.NewGuid().ToString("N");
        sessions[token] = (uid!, uname ?? "Trainer", verified);
        ctx.Response.Cookies.Append("pku_sess", token, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(7) });
        return Results.Redirect(publicBase + (verified ? "?login=ok" : "?login=notmember"));
    }
    catch { return Results.Redirect(publicBase + "?login=error"); }
});

app.MapGet("/api/auth/me", (HttpContext ctx) =>
    ctx.Request.Cookies.TryGetValue("pku_sess", out var t) && sessions.TryGetValue(t, out var u)
        ? Results.Json(new { loggedIn = true, name = u.Name, verified = u.Verified })
        : Results.Json(new { loggedIn = false }));

app.MapPost("/api/auth/logout", (HttpContext ctx) =>
{
    if (ctx.Request.Cookies.TryGetValue("pku_sess", out var t)) sessions.TryRemove(t, out _);
    ctx.Response.Cookies.Delete("pku_sess");
    return Results.Ok(new { ok = true });
});

// Per-user history of website-queued raids (in-memory; resets on container restart).
var userRaids = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentQueue<UserRaidEntry>>();
// Achievement state (mirrors what's declared below /api/achievements — declared here so /api/host can mutate)
var userAch = new System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, int>>();
var userTeraSet = new System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>>();
var userMapSet  = new System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>>();
var userWish = new System.Collections.Concurrent.ConcurrentDictionary<string, WishlistEntry[]>();

app.MapPost("/api/host", async (HttpContext ctx, HostRequest req) =>
{
    if (!ctx.Request.Cookies.TryGetValue("pku_sess", out var t) || !sessions.TryGetValue(t, out var u))
        return Results.Json(new { ok = false, error = "login" }, statusCode: 401);
    if (!u.Verified)
        return Results.Json(new { ok = false, error = "notmember" }, statusCode: 403);
    try
    {
        var url = $"{botBase}/api/raid/add?seed={Uri.EscapeDataString(req.Seed ?? "")}&stars={req.Stars}&progress={req.Progress}&location={Uri.EscapeDataString(req.Location ?? "Kitakami")}";
        var respText = await botHttp.GetStringAsync(url);
        // Record the queued raid against the logged-in user (My Raids history)
        try
        {
            using var rd = System.Text.Json.JsonDocument.Parse(respText);
            if (rd.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean())
            {
                var entry = new UserRaidEntry(
                    Seed: req.Seed ?? "",
                    Stars: req.Stars,
                    Species: rd.RootElement.TryGetProperty("species", out var sp) ? sp.GetString() ?? "?" : "?",
                    Shiny: rd.RootElement.TryGetProperty("shiny", out var sh) && sh.GetBoolean(),
                    Location: req.Location ?? "Kitakami",
                    When: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                );
                var q = userRaids.GetOrAdd(u.Id, _ => new System.Collections.Concurrent.ConcurrentQueue<UserRaidEntry>());
                q.Enqueue(entry);
                while (q.Count > 50 && q.TryDequeue(out _)) { }
                // Achievement counters
                var ac = userAch.GetOrAdd(u.Id, _ => new Dictionary<string,int>());
                lock (ac)
                {
                    ac["hosts"] = ac.TryGetValue("hosts", out var hv) ? hv + 1 : 1;
                    if (entry.Shiny) ac["shinies"] = ac.TryGetValue("shinies", out var sv) ? sv + 1 : 1;
                    if (entry.Stars >= 6) ac["six_stars"] = ac.TryGetValue("six_stars", out var sx) ? sx + 1 : 1;
                }
                userMapSet.GetOrAdd(u.Id, _ => new HashSet<string>()).Add(entry.Location.ToLowerInvariant());
                // wishlist match → wish_granted
                if (userWish.TryGetValue(u.Id, out var wl) && wl.Any(w => string.Equals(w.SpeciesName, entry.Species, StringComparison.OrdinalIgnoreCase)))
                {
                    lock (ac) { ac["wish_granted"] = ac.TryGetValue("wish_granted", out var wg) ? wg + 1 : 1; }
                }
            }
        }
        catch { }
        return Results.Content(respText, "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = "bot unreachable: " + ex.Message }); }
});

app.MapGet("/api/myraids", (HttpContext ctx) =>
{
    if (!ctx.Request.Cookies.TryGetValue("pku_sess", out var t) || !sessions.TryGetValue(t, out var u))
        return Results.Json(new { loggedIn = false, raids = Array.Empty<object>() });
    var raids = userRaids.TryGetValue(u.Id, out var q) ? q.Reverse().ToArray() : Array.Empty<UserRaidEntry>();
    return Results.Json(new { loggedIn = true, name = u.Name, verified = u.Verified, raids });
});

// AI natural-language search → Gemini parses the query into filter fields
var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
var filtersSchema = "species (English Pokémon name|null), shiny (true|false|null), stars (1-6|null), " +
    "teraType (Normal|Fighting|Flying|Poison|Ground|Rock|Bug|Ghost|Steel|Fire|Water|Grass|Electric|Psychic|Ice|Dragon|Dark|Fairy|null), " +
    "minFlawlessIVs (1-6|null), location (Paldea|Kitakami|Blueberry|null)";

// Dedicated client for AI calls (Gemini Coach/Vision responses can take >8s)
var aiHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

// Single Gemini-Flash call helper with 1 automatic retry on 429 (free-tier rate limit)
async Task<string> GeminiCallAsync(object requestBody)
{
    if (string.IsNullOrEmpty(geminiKey)) throw new Exception("AI not configured — add GEMINI_API_KEY to .env");
    var bodyJson = System.Text.Json.JsonSerializer.Serialize(requestBody);
    for (int attempt = 0; attempt < 2; attempt++)
    {
        var resp = await aiHttp.PostAsync($"https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key={geminiKey}",
            new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json"));
        var json = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        }
        if ((int)resp.StatusCode == 429 && attempt == 0)
        {
            // Extract suggested retry delay (e.g. "11.7s"); cap at 15s
            int waitSec = 12;
            try
            {
                using var ed = System.Text.Json.JsonDocument.Parse(json);
                foreach (var d in ed.RootElement.GetProperty("error").GetProperty("details").EnumerateArray())
                    if (d.TryGetProperty("retryDelay", out var rd))
                    {
                        var s = rd.GetString() ?? "10s";
                        if (double.TryParse(s.TrimEnd('s'), out var sec)) waitSec = Math.Min(15, (int)Math.Ceiling(sec) + 1);
                    }
            }
            catch { }
            await Task.Delay(waitSec * 1000);
            continue;
        }
        throw new Exception($"Gemini {(int)resp.StatusCode}: {json}");
    }
    throw new Exception("Gemini: exhausted retries");
}

app.MapPost("/api/ai/parse", async (AiParseRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey))
        return Results.Json(new { ok = false, error = "AI not configured — add GEMINI_API_KEY to .env" });
    if (string.IsNullOrWhiteSpace(req.Query))
        return Results.Json(new { ok = false, error = "empty query" });
    try
    {
        var prompt = "Parse this Tera Raid Pokémon search into a JSON object with ONLY these fields (use null when unspecified): " +
                     filtersSchema + ". Query: \"" + req.Query.Replace("\"", "\\\"") + "\". Return ONLY the JSON object, no markdown, no commentary.";
        var text = await GeminiCallAsync(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.1, responseMimeType = "application/json" }
        });
        return Results.Content("{\"ok\":true,\"filters\":" + text + "}", "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- AI Chat: multi-turn conversational mode with session memory + optional filter actions ---
var chatSessions = new System.Collections.Concurrent.ConcurrentDictionary<string, List<object>>();
app.MapPost("/api/ai/chat", async (AiChatRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false, error = "AI not configured" });
    if (string.IsNullOrWhiteSpace(req.Message)) return Results.Json(new { ok = false, error = "empty message" });
    var sid = string.IsNullOrWhiteSpace(req.SessionId) ? Guid.NewGuid().ToString("N") : req.SessionId!;
    var history = chatSessions.GetOrAdd(sid, _ => new List<object>());
    var sys = "You are the PKM Universe Reborn Tera Raid assistant. Help users find shiny/perfect Scarlet/Violet raid seeds, give raid coaching advice (best counter Pokémon, optimal moves, tera advice), and answer Pokémon questions. Be friendly, concise, use sparing emojis. " +
        "When the user wants to SEARCH for raids, ALWAYS end your reply with a fenced JSON block tagged ```filters with these fields (use null when unspecified): " + filtersSchema +
        ". Keep prose under 2 sentences when filters are included — the JSON does the heavy lifting. " +
        "When the user just asks a Pokémon question (no search), answer in prose only, no JSON block.";

    var contents = new List<object>
    {
        new { role = "user", parts = new[] { new { text = sys } } },
        new { role = "model", parts = new[] { new { text = "Got it — your PKM Universe raid partner. What are we hunting?" } } }
    };
    foreach (var t in history) contents.Add(t);
    contents.Add(new { role = "user", parts = new[] { new { text = req.Message } } });

    try
    {
        var reply = await GeminiCallAsync(new { contents = contents.ToArray(), generationConfig = new { temperature = 0.7 } });
        history.Add(new { role = "user", parts = new[] { new { text = req.Message } } });
        history.Add(new { role = "model", parts = new[] { new { text = reply } } });
        while (history.Count > 16) history.RemoveAt(0);

        string? filtersJson = null;
        var m = System.Text.RegularExpressions.Regex.Match(reply, @"```(?:filters|json)?\s*(\{[\s\S]*?\})\s*```");
        var clean = reply;
        if (m.Success)
        {
            filtersJson = m.Groups[1].Value.Trim();
            clean = System.Text.RegularExpressions.Regex.Replace(reply, @"```(?:filters|json)?[\s\S]*?```", "").Trim();
        }
        var payload = "{\"ok\":true,\"sessionId\":\"" + sid + "\",\"reply\":" + System.Text.Json.JsonSerializer.Serialize(clean) +
                      (filtersJson != null ? ",\"filters\":" + filtersJson : "") + "}";
        return Results.Content(payload, "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- AI Vision: paste a screenshot, Gemini Vision identifies the Pokémon ---
app.MapPost("/api/ai/vision", async (AiVisionRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false, error = "AI not configured" });
    if (string.IsNullOrWhiteSpace(req.ImageBase64)) return Results.Json(new { ok = false, error = "no image" });
    try
    {
        var mt = string.IsNullOrEmpty(req.MimeType) ? "image/png" : req.MimeType!;
        var b64 = req.ImageBase64!.Contains(",") ? req.ImageBase64.Substring(req.ImageBase64.IndexOf(',') + 1) : req.ImageBase64;
        var prompt = "Look at this Pokémon image (screenshot, sprite, or photo). Identify what's shown and return ONLY a JSON object with these fields (null when unsure): " +
                     filtersSchema + ". Pay attention to body color (is it the shiny coloration?), tera crystal glow if visible (its type), and star count if visible. No commentary.";
        var contents = new[] { new { parts = new object[] {
            new { text = prompt },
            new { inline_data = new { mime_type = mt, data = b64 } }
        }}};
        var text = await GeminiCallAsync(new { contents, generationConfig = new { temperature = 0.1, responseMimeType = "application/json" } });
        return Results.Content("{\"ok\":true,\"filters\":" + text + "}", "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- AI Raid Coach: counter team + moves + tera advice for a specific raid result ---
app.MapPost("/api/ai/coach", async (AiCoachRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false, error = "AI not configured" });
    try
    {
        var moves = req.Moves == null || req.Moves.Length == 0 ? "(unknown)" : string.Join(", ", req.Moves);
        var prompt = $"You're a Tera Raid coach. Brief the player on beating this 7-min timer Scarlet/Violet raid: {(req.Shiny ? "Shiny " : "")}{req.SpeciesName}, {req.Stars}-star, Tera type: {req.TeraType}, ability: {req.Ability ?? "?"}, raid moves: {moves}. " +
                     "Return ONLY this JSON (no markdown, no commentary): " +
                     "{ \"counters\": [{\"name\":\"<Pokémon>\",\"reason\":\"<one short sentence>\"} , ...3 best counters available in S/V], " +
                     "\"moves\":[\"<top 3 attack moves to bring>\"], " +
                     "\"teraAdvice\":\"<best tera type for your attacker, one sentence>\", " +
                     "\"tip\":\"<one short pre-raid tip>\" }. " +
                     "Counters must be Pokémon actually catchable in Scarlet/Violet/DLC. Be specific, not generic.";
        var text = await GeminiCallAsync(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.4, responseMimeType = "application/json" } });
        return Results.Content("{\"ok\":true,\"coach\":" + text + "}", "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- AI Announcer: hype Discord-ready announcement when hosting ---
app.MapPost("/api/ai/announce", async (AiAnnounceRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false, error = "AI not configured" });
    try
    {
        var prompt = $"Write a SHORT (1-2 sentences) hype Discord announcement for a Tera Raid host in the PKM Universe Reborn server. Details: hoster={req.HosterName ?? "a trainer"}, species={(req.Shiny ? "✦ Shiny " : "")}{req.SpeciesName}, stars={req.Stars}★, tera={req.TeraType}. " +
                     "Tone: excited but classy. Use 1-2 emojis max. End with urgency like '— code drops soon!' or 'get in fast!'. No hashtags. Plain text only — do NOT wrap in quotes.";
        var text = await GeminiCallAsync(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.85 } });
        return Results.Json(new { ok = true, text = text.Trim().Trim('"', '\'') });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- AI Typeahead: live query completions while user types ---
app.MapPost("/api/ai/suggest", async (AiSuggestRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = true, suggestions = Array.Empty<string>() });
    if (string.IsNullOrWhiteSpace(req.Partial) || req.Partial!.Length < 2) return Results.Json(new { ok = true, suggestions = Array.Empty<string>() });
    try
    {
        var prompt = $"A user is typing a Tera Raid search query. Their partial input so far: \"{req.Partial.Replace("\"", "\\\"")}\". " +
                     "Return ONLY a JSON array of 4 short, complete, sensible search queries they probably intend (each a single line). " +
                     "Examples: \"shiny 6IV Dragapult\", \"shiny Ghost-tera 6-star\". No prose, just the JSON array.";
        var text = await GeminiCallAsync(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.5, responseMimeType = "application/json" } });
        return Results.Content("{\"ok\":true,\"suggestions\":" + text + "}", "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- AI Lookup Summary: analyze a batch lookup, return a punchy one-liner ---
app.MapPost("/api/ai/lookup-summary", async (AiLookupSummaryRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false });
    var rows = req.Results ?? Array.Empty<AiSummaryRow>();
    if (rows.Length == 0) return Results.Json(new { ok = false });
    try
    {
        int total = rows.Length;
        int shinies = rows.Count(r => r.Shiny);
        int flawless = rows.Count(r => r.FlawlessIVs >= 6);
        var byTera = rows.GroupBy(r => r.TeraType ?? "?").OrderByDescending(g => g.Count()).First();
        var bySpecies = rows.GroupBy(r => r.SpeciesName).OrderByDescending(g => g.Count()).First();
        var prompt = $"Write a single punchy sentence summarizing this batch of {total} Tera Raid seeds: {shinies} shiny, {flawless} flawless 6IV, dominant tera type is {byTera.Key} ({byTera.Count()}), most common species is {bySpecies.Key} ({bySpecies.Count()}). Make it vivid and useful for a raid host. No markdown.";
        var text = await GeminiCallAsync(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.6 } });
        return Results.Json(new { ok = true, text = text.Trim().Trim('"'), stats = new { total, shinies, flawless, topTera = byTera.Key, topSpecies = bySpecies.Key } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- Reorder rotation: proxies a new index list to the bot. Bot must implement /api/raid/reorder ---
app.MapPost("/api/raid/reorder", async (HttpContext ctx, RaidReorderRequest req) =>
{
    if (!ctx.Request.Cookies.TryGetValue("pku_sess", out var t) || !sessions.TryGetValue(t, out var u))
        return Results.Json(new { ok = false, error = "login" }, statusCode: 401);
    if (!u.Verified) return Results.Json(new { ok = false, error = "notmember" }, statusCode: 403);
    try
    {
        var ord = string.Join(',', req.Order ?? Array.Empty<int>());
        var resp = await botHttp.GetStringAsync($"{botBase}/api/raid/reorder?order={Uri.EscapeDataString(ord)}");
        return Results.Content(resp, "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = "bot didn't accept reorder: " + ex.Message }); }
});

// --- AI Summary: 1-2 sentence explainer above a search result list ---
app.MapPost("/api/ai/summary", async (AiSummaryRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false });
    try
    {
        var top = (req.Results ?? Array.Empty<AiSummaryRow>()).Take(5).ToArray();
        if (top.Length == 0) return Results.Json(new { ok = false });
        var rows = string.Join("; ", top.Select(r => $"{(r.Shiny ? "✦ " : "")}{r.SpeciesName} {r.Stars}★ {r.TeraType}-tera {r.FlawlessIVs}IV {r.Nature}"));
        var prompt = $"Summarize this Tera Raid search in 1-2 punchy sentences for the host's page. Original query: \"{req.Query}\". Top results: {rows}. Call out what's special (flawless? shiny? rare tera? best nature?). No markdown.";
        var text = await GeminiCallAsync(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.7 } });
        return Results.Json(new { ok = true, text = text.Trim().Trim('"') });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- AI Surprise Me: AI rolls a themed search based on your history ---
app.MapPost("/api/ai/surprise", async (HttpContext ctx) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false, error = "AI not configured" });
    string hist = "no prior history";
    if (ctx.Request.Cookies.TryGetValue("pku_sess", out var t) && sessions.TryGetValue(t, out var u))
        if (userRaids.TryGetValue(u.Id, out var q))
            hist = string.Join(", ", q.Take(10).Select(o => System.Text.Json.JsonSerializer.Serialize(o)));
    try
    {
        var prompt = "You're rolling a fun, themed Tera Raid pick for a player. Their recent raid history: " + hist + ". " +
                     "Return ONLY this JSON: { \"theme\":\"<short fun headline like 'Storm Riders' or 'Tiny Terrors'>\", \"filters\":{ " + filtersSchema + " } }. " +
                     "Bias toward shiny+high IVs+interesting tera. Pick something fresh (different from history when possible). Make the theme delightful.";
        var text = await GeminiCallAsync(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.95, responseMimeType = "application/json" } });
        return Results.Content("{\"ok\":true,\"surprise\":" + text + "}", "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// /api/species — returns ONLY species that actually appear as Tera Raid bosses for the chosen
// map/progress/stars. Pass all=true to bypass and get the full National Dex list (legacy).
app.MapGet("/api/species", (string? map, int? stars, int? progress, bool? all, RaidEngine engine) =>
{
    var list = GameInfo.GetStrings(2).specieslist;
    if (all == true)
    {
        var arr = new List<object>(list.Length);
        for (int i = 1; i < list.Length; i++)
            if (!string.IsNullOrWhiteSpace(list[i])) arr.Add(new { id = i, name = list[i] });
        return Results.Ok(arr);
    }
    var mapVal = (map ?? "Kitakami").ToLowerInvariant() switch
    {
        "paldea" => TeraRaidMapParent.Paldea,
        "blueberry" => TeraRaidMapParent.Blueberry,
        _ => TeraRaidMapParent.Kitakami,
    };
    int progressVal = progress is >= 3 and <= 6 ? progress.Value : 6;
    int starsVal = stars is >= 1 and <= 6 ? stars.Value : 0;
    int contentType = starsVal == 6 ? 1 : 0;

    var byStars = engine.GetSpeciesByStars(mapVal, progressVal, contentType);
    HashSet<int> ids;
    if (starsVal == 0)
    {
        ids = new HashSet<int>();
        foreach (var s in byStars.Values) foreach (var id in s) ids.Add(id);
    }
    else
    {
        ids = byStars.TryGetValue(starsVal, out var set) ? set : new HashSet<int>();
    }
    var outList = new List<object>();
    foreach (var id in ids.OrderBy(x => x))
        if (id > 0 && id < list.Length && !string.IsNullOrWhiteSpace(list[id]))
            outList.Add(new { id, name = list[id] });
    return Results.Ok(outList);
});

app.MapPost("/api/search", (SearchRequest req, RaidEngine engine) =>
{
    try { return Results.Ok(engine.Search(req)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Reverse / batch lookup: paste bare seeds OR full "!ra <seed> <stars> <progress>" lines,
// get full details for each (no filtering). Used to verify community seeds + bulk-import.
app.MapPost("/api/lookup", (LookupRequest req, RaidEngine engine) =>
{
    try { return Results.Ok(engine.Lookup(req)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// --- Permalink: GET single-seed by URL like /api/r/02AEAC78?stars=6&story=6&loc=Kitakami ---
app.MapGet("/api/r/{seed}", (string seed, string? loc, int? stars, int? story, RaidEngine engine) =>
{
    try
    {
        var req = new LookupRequest("Scarlet", loc ?? "Kitakami", story ?? 6, stars ?? 6, new[] { seed });
        var res = engine.Lookup(req);
        return res.Length > 0 ? Results.Json(res[0]) : Results.NotFound();
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// --- Leaderboard: aggregate the My-Raids data across all logged-in users ---
app.MapGet("/api/leaderboard", (string? period) =>
{
    long cutoff = period switch
    {
        "week"  => DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds(),
        "month" => DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
        _ => 0L
    };
    var rows = new List<LeaderboardRow>();
    foreach (var kv in userRaids)
    {
        var arr = kv.Value.ToArray();
        var filtered = arr.Where(e => e.When >= cutoff).ToArray();
        if (filtered.Length == 0) continue;
        var name = sessions.Values.Where(s => s.Id == kv.Key).Select(s => s.Name).FirstOrDefault() ?? "Trainer";
        int shinies = filtered.Count(e => e.Shiny);
        var topSpecies = filtered.GroupBy(e => e.Species).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "?";
        var lastWhen = filtered.Max(e => e.When);
        rows.Add(new LeaderboardRow(kv.Key, name, filtered.Length, shinies, topSpecies, lastWhen));
    }
    var top = rows.OrderByDescending(r => r.Total).ThenByDescending(r => r.Shinies).Take(20).ToArray();
    return Results.Json(new { period = period ?? "alltime", topHosts = top, generatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), totalUsers = rows.Count });
});

// --- AI Raid Party Builder: 6-Pokémon counter team with moves/items/teras ---
app.MapPost("/api/ai/party", async (AiPartyRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false, error = "AI not configured" });
    try
    {
        var prompt = $"Build the optimal 6-Pokémon counter team for this Scarlet/Violet Tera Raid boss: {(req.Shiny ? "Shiny " : "")}{req.SpeciesName}, {req.Stars}-star, Tera type: {req.TeraType}, ability: {req.Ability ?? "?"}. " +
                     "Return ONLY this JSON (no markdown, no commentary): " +
                     "{ \"strategy\":\"<one-sentence overall game plan>\", " +
                     "  \"team\": [ {\"name\":\"<species available in S/V or DLC>\", \"role\":\"lead|sweeper|support|tank|healer|screener\", \"item\":\"<held item>\", \"ability\":\"<ability>\", \"nature\":\"<nature>\", \"tera\":\"<best tera type for this mon>\", \"moves\":[\"<m1>\",\"<m2>\",\"<m3>\",\"<m4>\"], \"reason\":\"<one short sentence>\"} ×6 ] }. " +
                     "Include at least 1 support (Intimidate / screens / healing / cheers). All Pokémon must be obtainable in Scarlet/Violet/DLC. Use real S/V learnable movesets.";
        var text = await GeminiCallAsync(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.45, responseMimeType = "application/json" } });
        return Results.Content("{\"ok\":true,\"party\":" + text + "}", "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- AI Recommendations: "Picked for you" — Gemini learns from user's My Raids + Wishlist + search ---
app.MapPost("/api/ai/recommend", async (AiRecommendRequest req) =>
{
    if (string.IsNullOrEmpty(geminiKey)) return Results.Json(new { ok = false, error = "AI not configured" });
    try
    {
        var history = req.History == null || req.History.Length == 0 ? "(no prior history)"
            : string.Join(", ", req.History.Take(15).Select(h => $"{(h.Shiny ? "✦ " : "")}{h.Species} {h.Stars}★ {(string.IsNullOrEmpty(h.Tera) ? "" : h.Tera + "-tera")}"));
        var wish = req.Wishlist == null || req.Wishlist.Length == 0 ? "(empty)"
            : string.Join(", ", req.Wishlist.Take(15).Select(w => $"{(w.Shiny ? "✦ " : "")}{w.Species} {w.Stars}★"));
        var prompt = "You're recommending Tera Raid seeds for a Pokémon player. " +
                     $"Their hosted-raid history: {history}. Their wishlist: {wish}. " +
                     "Analyze the patterns (favorite types, star levels, tera preferences, shiny obsession, etc.) and produce 3 distinct themed picks they'd love. " +
                     "Return ONLY this JSON (no markdown): { \"picks\": [ { " +
                     "\"theme\":\"<short fun headline>\", \"reason\":\"<one-sentence why this matches them>\", \"filters\": { " + filtersSchema +
                     " } } x3 ] }. Make each pick FRESH and different from history. Bias toward shiny + interesting tera + high star raids.";
        var text = await GeminiCallAsync(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.85, responseMimeType = "application/json" } });
        return Results.Content("{\"ok\":true,\"reco\":" + text + "}", "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// --- Server-side wishlist for per-user push-alert matching (userWish declared above) ---
app.MapGet("/api/wishlist", (HttpContext ctx) =>
{
    if (!ctx.Request.Cookies.TryGetValue("pku_sess", out var t) || !sessions.TryGetValue(t, out var u))
        return Results.Json(new { loggedIn = false });
    return Results.Json(new { loggedIn = true, items = userWish.GetValueOrDefault(u.Id, Array.Empty<WishlistEntry>()) });
});
app.MapPost("/api/wishlist", (HttpContext ctx, WishlistEntry[] items) =>
{
    if (!ctx.Request.Cookies.TryGetValue("pku_sess", out var t) || !sessions.TryGetValue(t, out var u))
        return Results.Json(new { ok = false, error = "login" }, statusCode: 401);
    userWish[u.Id] = items ?? Array.Empty<WishlistEntry>();
    return Results.Json(new { ok = true, count = userWish[u.Id].Length });
});

// --- Network: live host list for the world map (real bot + sample federated nodes) ---
app.MapGet("/api/network/hosts", async () =>
{
    var hosts = new List<object>();
    // The real PKM Universe host
    try
    {
        var nowJson = await botHttp.GetStringAsync($"{botBase}/api/raid/now");
        using var doc = System.Text.Json.JsonDocument.Parse(nowJson);
        var st = doc.RootElement;
        hosts.Add(new
        {
            id = "pkm-universe-main",
            name = "PKM Universe Reborn",
            lat = 39.83, lng = -98.58, // continental US center as approx
            country = "US",
            active = st.GetProperty("active").GetBoolean(),
            species = st.TryGetProperty("species", out var sp) ? sp.GetString() ?? "" : "",
            stars = st.TryGetProperty("stars", out var stt) ? stt.GetInt32() : 0,
            shiny = st.TryGetProperty("shiny", out var sh) && sh.GetBoolean(),
            sprite = st.TryGetProperty("sprite", out var spr) ? spr.GetString() ?? "" : "",
            primary = true
        });
    }
    catch { }
    // Demo federated nodes (placeholder; replaces with real peers when federation lands)
    var demo = new (string id, string name, double lat, double lng, string country)[]
    {
        ("eu-london",  "EU · London",   51.50,   -0.13, "GB"),
        ("au-sydney",  "AU · Sydney",  -33.87,  151.20, "AU"),
        ("jp-tokyo",   "JP · Tokyo",    35.68,  139.76, "JP"),
        ("br-saopaulo","BR · São Paulo",-23.55, -46.63, "BR"),
        ("de-frankfurt","DE · Frankfurt",50.11,  8.68,  "DE"),
        ("ca-toronto", "CA · Toronto",  43.65,  -79.38, "CA"),
        ("kr-seoul",   "KR · Seoul",    37.57,  126.98, "KR"),
    };
    foreach (var d in demo)
        hosts.Add(new { id = d.id, name = d.name, lat = d.lat, lng = d.lng, country = d.country, active = false, species = "", stars = 0, shiny = false, sprite = "", primary = false, demo = true });
    return Results.Json(new { hosts });
});

// --- Achievements: 21 badges across hosting / shiny / quality / AI / discovery / collection ---
// State dictionaries (userAch / userTeraSet / userMapSet) are declared earlier so /api/host can mutate them.
var ACHIEVEMENT_DEFS = new[]
{
    new AchievementDef("first_host",     "🚀", "First Host",      "Host your first raid",                      1,  "Hosting"),
    new AchievementDef("ten_up",         "🔟", "Ten Up",          "Host 10 raids",                             10, "Hosting"),
    new AchievementDef("centurion",      "💯", "Centurion",       "Host 100 raids",                            100,"Hosting"),
    new AchievementDef("first_shiny",    "✨", "First Shiny",     "Host your first shiny",                     1,  "Shiny"),
    new AchievementDef("shiny_hunter",   "🌟", "Shiny Hunter",    "Host 10 shinies",                           10, "Shiny"),
    new AchievementDef("shiny_master",   "💫", "Shiny Master",    "Host 50 shinies",                           50, "Shiny"),
    new AchievementDef("flawless_find",  "💎", "Flawless Find",   "Host a 6-perfect-IV Pokémon",               1,  "Quality"),
    new AchievementDef("diamond_hand",   "💍", "Diamond Hand",    "Host 5 flawless Pokémon",                   5,  "Quality"),
    new AchievementDef("star_climber",   "⭐", "Star Climber",    "Host a 6★ raid",                            1,  "Difficulty"),
    new AchievementDef("six_star_vet",   "🌠", "6★ Veteran",      "Host 10 6★ raids",                          10, "Difficulty"),
    new AchievementDef("globetrotter",   "🗺️", "Globetrotter",   "Host in all 3 maps",                        3,  "Exploration"),
    new AchievementDef("type_collector", "🌈", "Type Collector",  "Host raids covering all 18 Tera types",     18, "Collection"),
    new AchievementDef("wish_granter",   "⭐", "Wish Granter",    "Host one of your wishlisted seeds",         1,  "Personal"),
    new AchievementDef("strategist",     "🪧", "Strategist",      "Use the AI Party Builder 10 times",         10, "AI Tools"),
    new AchievementDef("coach_pupil",    "🧠", "Coach's Pupil",   "View the AI Raid Coach 10 times",           10, "AI Tools"),
    new AchievementDef("3d_enthusiast",  "🎮", "3D Enthusiast",   "Open the 3D viewer 5 times",                5,  "Discovery"),
    new AchievementDef("world_traveler", "🌐", "World Traveler",  "Open the live host globe",                  1,  "Discovery"),
    new AchievementDef("voice_user",     "🎙️", "Voice User",     "Use voice search at least once",            1,  "AI Tools"),
    new AchievementDef("vision_user",    "📷", "Vision User",     "Identify a Pokémon by screenshot",          1,  "AI Tools"),
    new AchievementDef("chatty",         "💬", "Chatty",          "Send 10 chat messages",                     10, "AI Tools"),
    new AchievementDef("sharer",         "🔗", "Sharer",          "Copy 5 permalink share URLs",               5,  "Social"),
};

string? CurrentUserId(HttpContext ctx) =>
    ctx.Request.Cookies.TryGetValue("pku_sess", out var t) && sessions.TryGetValue(t, out var u) ? u.Id : null;

Dictionary<string,int> GetAchCounters(string uid) =>
    userAch.GetOrAdd(uid, _ => new Dictionary<string, int>());

void IncAch(string uid, string key, int by = 1)
{
    var d = GetAchCounters(uid);
    lock (d) { d[key] = (d.TryGetValue(key, out var v) ? v : 0) + by; }
}

// GET — returns definitions + this user's progress + unlocked list
app.MapGet("/api/achievements", (HttpContext ctx) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == null) return Results.Json(new { loggedIn = false, definitions = ACHIEVEMENT_DEFS });
    var counters = GetAchCounters(uid);
    var prog = new Dictionary<string, int>();
    var unlocked = new List<string>();
    foreach (var def in ACHIEVEMENT_DEFS)
    {
        var have = MapCounterFor(def.Id, counters, uid);
        prog[def.Id] = Math.Min(have, def.Target);
        if (have >= def.Target) unlocked.Add(def.Id);
    }
    return Results.Json(new { loggedIn = true, definitions = ACHIEVEMENT_DEFS, progress = prog, unlocked });
});

// POST — increment a UI-side counter (3d-viewer opens, coach views, etc.)
app.MapPost("/api/achievements/event", (HttpContext ctx, AchievementEvent ev) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == null) return Results.Json(new { ok = false, error = "login" }, statusCode: 401);
    var allowed = new[] { "party_builder", "coach_view", "viewer3d_open", "globe_open", "voice_used", "vision_used", "chat_msg", "share_copied", "tera_caught", "map_hosted" };
    if (!allowed.Contains(ev.EventType)) return Results.Json(new { ok = false, error = "unknown event" });
    // For tera/map "collected" events the detail represents the unique key
    if (ev.EventType == "tera_caught" && !string.IsNullOrWhiteSpace(ev.Detail))
        userTeraSet.GetOrAdd(uid, _ => new HashSet<string>()).Add(ev.Detail!.Trim().ToLowerInvariant());
    else if (ev.EventType == "map_hosted" && !string.IsNullOrWhiteSpace(ev.Detail))
        userMapSet.GetOrAdd(uid, _ => new HashSet<string>()).Add(ev.Detail!.Trim().ToLowerInvariant());
    else
        IncAch(uid, ev.EventType, 1);
    return Results.Json(new { ok = true });
});

// Map a definition id → current value, blending server-side host counters with UI events
int MapCounterFor(string defId, Dictionary<string,int> c, string uid)
{
    int hosts    = c.TryGetValue("hosts", out var h) ? h : 0;
    int shinies  = c.TryGetValue("shinies", out var s) ? s : 0;
    int flawless = c.TryGetValue("flawless", out var f) ? f : 0;
    int sixstars = c.TryGetValue("six_stars", out var ss) ? ss : 0;
    return defId switch
    {
        "first_host"     => hosts,
        "ten_up"         => hosts,
        "centurion"      => hosts,
        "first_shiny"    => shinies,
        "shiny_hunter"   => shinies,
        "shiny_master"   => shinies,
        "flawless_find"  => flawless,
        "diamond_hand"   => flawless,
        "star_climber"   => sixstars,
        "six_star_vet"   => sixstars,
        "globetrotter"   => userMapSet.TryGetValue(uid, out var ms) ? ms.Count : 0,
        "type_collector" => userTeraSet.TryGetValue(uid, out var ts) ? ts.Count : 0,
        "wish_granter"   => c.TryGetValue("wish_granted", out var wg) ? wg : 0,
        "strategist"     => c.TryGetValue("party_builder", out var pb) ? pb : 0,
        "coach_pupil"    => c.TryGetValue("coach_view", out var cv) ? cv : 0,
        "3d_enthusiast"  => c.TryGetValue("viewer3d_open", out var vo) ? vo : 0,
        "world_traveler" => c.TryGetValue("globe_open", out var go) ? go : 0,
        "voice_user"     => c.TryGetValue("voice_used", out var vu) ? vu : 0,
        "vision_user"    => c.TryGetValue("vision_used", out var viu) ? viu : 0,
        "chatty"         => c.TryGetValue("chat_msg", out var cm) ? cm : 0,
        "sharer"         => c.TryGetValue("share_copied", out var sc) ? sc : 0,
        _ => 0,
    };
}

// Permalink SPA fallback — /r/<seed> serves index.html so the client-side router can hydrate
app.MapFallbackToFile("/r/{seed}", "index.html");

app.Run();

// ----------------------------------------------------------------------------------

public sealed record SearchRequest(
    string? Game,            // "Scarlet" | "Violet"
    string? Location,        // "Paldea" | "Kitakami" | "Blueberry"
    int? StoryProgress,      // 3..6 (6 = post-game, full pool)
    int? Stars,              // 1..6 (required-ish; derives content type)
    int? Species,            // National dex number; null = any
    bool? Shiny,             // null = any
    int? TeraType,           // MoveType id; null = any
    int? MinFlawlessIVs,     // null = any
    int? MaxResults,         // default 25, capped 100
    long? MaxScan,           // seeds to scan; default 60M, capped 300M
    string? Nature,          // nature name; null = any
    string? Gender,          // "Male"|"Female"|"Genderless"; null = any
    bool? HiddenAbility,     // true = require HA; null/false = any
    string? Scale,           // "XXXS"|"XS"|"S"|"M"|"L"|"XL"|"XXXL"; null = any
    string? RewardItem,      // reward item name substring; null = any
    int? RewardMinQty);      // min total quantity of RewardItem; null = 1

public sealed record LookupRequest(string? Game, string? Location, int? StoryProgress, int? Stars, string[]? Seeds);
public sealed record HostRequest(string? Seed, int Stars, int Progress, string? Location);
public sealed record AiParseRequest(string? Query);
public sealed record AiChatRequest(string? SessionId, string Message);
public sealed record AiVisionRequest(string ImageBase64, string? MimeType);
public sealed record AiCoachRequest(string SpeciesName, bool Shiny, int Stars, string TeraType, string? Ability, string[]? Moves);
public sealed record AiAnnounceRequest(string SpeciesName, bool Shiny, int Stars, string TeraType, string? HosterName);
public sealed record AiSuggestRequest(string? Partial);
public sealed record AiSummaryRow(string SpeciesName, bool Shiny, int Stars, string TeraType, int FlawlessIVs, string? Nature);
public sealed record AiSummaryRequest(string Query, AiSummaryRow[]? Results);
public sealed record UserRaidEntry(string Seed, int Stars, string Species, bool Shiny, string Location, long When);
public sealed record LeaderboardRow(string Id, string Name, int Total, int Shinies, string TopSpecies, long LastWhen);
public sealed record AiPartyRequest(string SpeciesName, bool Shiny, int Stars, string TeraType, string? Ability);
public sealed record HistoryRow(string Species, int Stars, bool Shiny, string? Tera);
public sealed record AiRecommendRequest(HistoryRow[]? History, HistoryRow[]? Wishlist);
public sealed record WishlistEntry(string Seed, int Species, string SpeciesName, bool Shiny, int Stars);
public sealed record AchievementDef(string Id, string Icon, string Name, string Description, int Target, string Category);
public sealed record AchievementEvent(string EventType, string? Detail);
public sealed record AiLookupSummaryRequest(AiSummaryRow[]? Results);
public sealed record RaidReorderRequest(int[]? Order);
public sealed record MoveDto(string Name, string Type);
public sealed record RewardDto(string Name, int Qty);
public sealed record RaidResultDto(
    string Seed, int Species, string SpeciesName, byte Form, bool Shiny,
    int Stars, int TeraType, string TeraName, int FlawlessIVs,
    int[]? IVs, string? Nature, string? Gender, int? Scale, string RaCommand,
    string? Ability, bool Hidden, MoveDto[]? Moves, RewardDto[]? Rewards, int[]? BaseStats);

public sealed record SearchResponse(
    RaidResultDto[] Results, long Scanned, long ElapsedMs, bool HitLimit, string Echo);

public sealed class RaidEngine
{
    private readonly ConcurrentDictionary<string, RaidContainer> _containers = new();
    // Cache: which species (and at which star count) actually appear in raids for a given map/progress/contentType.
    // Built lazily by scanning a sample of seeds; first call per key takes ~1-2s, subsequent calls are O(1).
    private readonly ConcurrentDictionary<(TeraRaidMapParent map, int progress, int contentType), Dictionary<int, HashSet<int>>> _speciesByMap = new();

    public void Warm() { _ = Get("Scarlet"); }

    private RaidContainer Get(string game) =>
        _containers.GetOrAdd(game, g => { var c = new RaidContainer(g); c.SetGame(g); return c; });

    // Fresh container per parallel partition (avoids any cross-thread state in the engine).
    private static RaidContainer Fresh(string game) { var c = new RaidContainer(game); c.SetGame(game); return c; }

    private static byte[] Hex(string h)
    {
        var b = new byte[h.Length / 2];
        for (int i = 0; i < h.Length; i += 2) b[i / 2] = Convert.ToByte(h.Substring(i, 2), 16);
        Array.Reverse(b);
        return b;
    }

    private static byte[] RaidBytes(uint seed, int contentType)
    {
        return Hex("00000001").Concat(Hex("00000001")).Concat(Hex("00000001")).Concat(Hex("00000001"))
            .Concat(Hex(seed.ToString("X8"))).Concat(Hex("00000000"))
            .Concat(Hex($"0000000{contentType}")).Concat(Hex("00000000")).ToArray();
    }

    private static int RemapProgress(int storyProgress) =>
        storyProgress switch { 3 => 1, 4 => 2, 5 => 3, 6 => 4, 0 => 0, _ => 4 };

    private static TeraRaidMapParent ParseMap(string? loc) => (loc ?? "Paldea").ToLowerInvariant() switch
    {
        "kitakami" => TeraRaidMapParent.Kitakami,
        "blueberry" => TeraRaidMapParent.Blueberry,
        _ => TeraRaidMapParent.Paldea,
    };

    // Enumerate ONLY the species that actually show up in raids for this map / progress / contentType,
    // grouped by star count. Built once per key from a ~600k-seed sample (parallel, ~1.5s on first call).
    // Subsequent calls return the cached dictionary instantly.
    public Dictionary<int, HashSet<int>> GetSpeciesByStars(TeraRaidMapParent map, int storyProgress, int contentType)
    {
        return _speciesByMap.GetOrAdd((map, storyProgress, contentType), k =>
        {
            int progress = RemapProgress(k.progress); // engine expects 3-6 → 1-4 here
            const long Sample = 600_000;
            var dict = new Dictionary<int, HashSet<int>>();
            var locker = new object();
            var po = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            Parallel.For(0L, Sample, po,
                () => (Container: Fresh("Scarlet"), Local: new Dictionary<int, HashSet<int>>()),
                (i, _, t) =>
                {
                    try
                    {
                        var raid = new Raid(RaidBytes((uint)i, k.contentType), k.map);
                        var enc = raid.GetTeraEncounter(t.Container, raid.IsEvent ? 3 : progress, k.contentType == 3 ? 1 : 0);
                        if (enc != null)
                        {
                            int stars = raid.IsEvent ? enc.Stars : raid.GetStarCount(raid.Difficulty, progress, raid.IsBlack);
                            if (!t.Local.TryGetValue(stars, out var set)) { set = new HashSet<int>(); t.Local[stars] = set; }
                            set.Add(enc.Species);
                        }
                    }
                    catch { }
                    return t;
                },
                t =>
                {
                    lock (locker)
                    {
                        foreach (var kv in t.Local)
                        {
                            if (!dict.TryGetValue(kv.Key, out var set)) { set = new HashSet<int>(); dict[kv.Key] = set; }
                            foreach (var s in kv.Value) set.Add(s);
                        }
                    }
                });
            return dict;
        });
    }

    public SearchResponse Search(SearchRequest req)
    {
        var game = (req.Game ?? "Scarlet").Trim();
        var map = ParseMap(req.Location);
        int storyProgress = req.StoryProgress is >= 3 and <= 6 ? req.StoryProgress.Value : 6;
        int progress = RemapProgress(storyProgress);
        int starsWanted = req.Stars is >= 1 and <= 6 ? req.Stars.Value : 0;
        int contentType = starsWanted == 6 ? 1 : 0;   // black crystal raids use content type 1
        int maxResults = Math.Clamp(req.MaxResults ?? 25, 1, 100);
        long maxScan = Math.Clamp(req.MaxScan ?? 60_000_000L, 1, 300_000_000L);

        var found = new ConcurrentQueue<RaidResultDto>();
        int foundCount = 0;
        long scanned = 0;
        var sw = Stopwatch.StartNew();
        var timeBudget = TimeSpan.FromSeconds(20);

        var po = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        Parallel.For(0L, maxScan, po,
            () => Fresh(game),
            (i, state, container) =>
            {
                if (Volatile.Read(ref foundCount) >= maxResults || sw.Elapsed > timeBudget) { state.Stop(); return container; }
                Interlocked.Increment(ref scanned);

                var dto = Evaluate((uint)i, container, map, progress, contentType, starsWanted, req, fullDetails: false);
                if (dto is not null)
                {
                    if (Interlocked.Increment(ref foundCount) <= maxResults) found.Enqueue(dto);
                    else state.Stop();
                }
                return container;
            },
            _ => { });

        sw.Stop();

        // Enrich the matched results with full PK9 details (IVs/nature/gender/scale) — cheap for <=100.
        var detailed = found.Take(maxResults)
            .Select(r => Evaluate(Convert.ToUInt32(r.Seed, 16), Get(game), map, progress, contentType, starsWanted, req, fullDetails: true)!)
            .OrderBy(r => r.Species).ThenBy(r => r.Seed)
            .ToArray();

        bool hitLimit = foundCount < maxResults && (scanned >= maxScan || sw.Elapsed >= timeBudget);
        return new SearchResponse(detailed, scanned, sw.ElapsedMilliseconds, hitLimit, "!ra");
    }

    public RaidResultDto[] Lookup(LookupRequest req)
    {
        var game = (req.Game ?? "Scarlet").Trim();
        var map = ParseMap(req.Location);
        int defProgress = req.StoryProgress is >= 3 and <= 6 ? req.StoryProgress.Value : 6;
        int defStars = req.Stars is >= 1 and <= 6 ? req.Stars.Value : 6;
        var container = Get(game);
        var blank = new SearchRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var outList = new List<RaidResultDto>();
        foreach (var raw in (req.Seeds ?? Array.Empty<string>()).Take(50))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Trim().TrimStart('!').Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string hex; int stars = defStars, story = defProgress;
            if (parts[0].Equals("ra", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2) continue;
                hex = parts[1];
                if (parts.Length >= 3 && int.TryParse(parts[2], out var s)) stars = s;
                if (parts.Length >= 4 && int.TryParse(parts[3], out var p)) story = p;
            }
            else hex = parts[0];
            hex = hex.Replace("0x", "").Replace("0X", "").Trim();
            if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var seed)) continue;
            int progress = RemapProgress(story);
            int contentType = stars == 6 ? 1 : 0;
            try { var dto = Evaluate(seed, container, map, progress, contentType, 0, blank, fullDetails: true); if (dto != null) outList.Add(dto); }
            catch { }
        }
        return outList.ToArray();
    }

    private static RaidResultDto? Evaluate(uint seed, RaidContainer container, TeraRaidMapParent map,
        int progress, int contentType, int starsWanted, SearchRequest req, bool fullDetails)
    {
        var raid = new Raid(RaidBytes(seed, contentType), map);
        var enc = raid.GetTeraEncounter(container, raid.IsEvent ? 3 : progress, contentType == 3 ? 1 : 0);
        if (enc is null) return null;

        int stars = raid.IsEvent ? enc.Stars : raid.GetStarCount(raid.Difficulty, progress, raid.IsBlack);
        int tera = raid.GetTeraType(enc);
        bool shiny = raid.IsShiny;

        if (starsWanted != 0 && stars != starsWanted) return null;
        if (req.Species is > 0 && enc.Species != req.Species) return null;
        if (req.Shiny.HasValue && shiny != req.Shiny.Value) return null;
        if (req.TeraType.HasValue && tera != req.TeraType.Value) return null;
        if (req.MinFlawlessIVs.HasValue && enc.FlawlessIVCount < req.MinFlawlessIVs.Value) return null;

        var seedHex = seed.ToString("X8");
        int storyOut = req.StoryProgress is >= 3 and <= 6 ? req.StoryProgress.Value : 6;
        string ra = $"!ra {seedHex} {stars} {storyOut}";

        // Advanced filters (nature / gender / hidden-ability / scale / reward) require generating the
        // PK9 + rewards, so the cheap scan pass must also generate when any of them is requested.
        bool hasAdvanced = !string.IsNullOrEmpty(req.Nature) || !string.IsNullOrEmpty(req.Gender)
            || (req.HiddenAbility ?? false) || !string.IsNullOrEmpty(req.Scale) || !string.IsNullOrEmpty(req.RewardItem);

        if (!fullDetails && !hasAdvanced)
            return new RaidResultDto(seedHex, enc.Species, SpeciesName(enc.Species), enc.Form, shiny,
                stars, tera, ((MoveType)tera).ToString(), enc.FlawlessIVCount, null, null, null, null, ra,
                null, false, null, null, null);

        // Full generation for display
        var shinyState = shiny ? Shiny.Always : Shiny.Never;
        var gender = PersonalTable.SV.GetFormEntry(enc.Species, enc.Form).Gender;
        var param = new GenerateParam9(enc.Species, gender, enc.FlawlessIVCount, 1, 0, 0,
            SizeType9.RANDOM, 0, enc.Ability, shinyState);
        var pk = new PK9
        {
            Species = enc.Species,
            Form = enc.Form,
            Move1 = enc.Move1, Move2 = enc.Move2, Move3 = enc.Move3, Move4 = enc.Move4,
            TeraTypeOriginal = (MoveType)tera,
            CurrentLevel = (byte)enc.Level,
        };
        Encounter9RNG.GenerateData(pk, param, EncounterCriteria.Unrestricted, raid.Seed);

        var ivs = new[] { pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE };
        string genderStr = pk.Gender switch { 0 => "Male", 1 => "Female", _ => "Genderless" };

        var strings = GameInfo.GetStrings(2);
        string abilityName = SafeIdx(strings.Ability, pk.Ability);
        bool hidden = pk.AbilityNumber == 4;
        var moves = new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 }
            .Where(m => m != 0)
            .Select(m => new MoveDto(SafeIdx(strings.Move, m), ((MoveType)MoveInfo.GetType((ushort)m, pk.Context)).ToString()))
            .ToArray();

        var rewardsList = new List<RewardDto>();
        try
        {
            var rw = enc.GetRewards(container, raid, 0);
            if (rw != null)
                foreach (var t in rw)
                    if (t.Item1 > 0)
                        rewardsList.Add(new RewardDto(SafeIdx(strings.Item, t.Item1), t.Item2));
        }
        catch { }
        var rewards = rewardsList.Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => r.Name).Select(g => new RewardDto(g.Key, g.Sum(x => x.Qty)))
            .Take(8).ToArray();

        // --- Advanced filters (applied post-generation) ---
        if (!string.IsNullOrEmpty(req.Nature) && !string.Equals(pk.Nature.ToString(), req.Nature, StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrEmpty(req.Gender) && !string.Equals(genderStr, req.Gender, StringComparison.OrdinalIgnoreCase)) return null;
        if ((req.HiddenAbility ?? false) && !hidden) return null;
        if (!string.IsNullOrEmpty(req.Scale) && !string.Equals(ScaleLabel(pk.Scale), req.Scale, StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrEmpty(req.RewardItem))
        {
            int want = req.RewardMinQty is > 0 ? req.RewardMinQty.Value : 1;
            // match against the FULL reward list (not the truncated Top-8) so nothing is missed
            int have = rewardsList.Where(r => r.Name.Contains(req.RewardItem, StringComparison.OrdinalIgnoreCase)).Sum(r => r.Qty);
            if (have < want) return null;
        }

        int[]? baseStats = null;
        try { var pi = PersonalTable.SV.GetFormEntry(enc.Species, enc.Form); baseStats = new[] { pi.HP, pi.ATK, pi.DEF, pi.SPA, pi.SPD, pi.SPE }; }
        catch { }

        return new RaidResultDto(seedHex, enc.Species, SpeciesName(enc.Species), enc.Form, shiny,
            stars, tera, ((MoveType)tera).ToString(), enc.FlawlessIVCount,
            ivs, pk.Nature.ToString(), genderStr, pk.Scale, ra,
            abilityName, hidden, moves, rewards, baseStats);
    }

    private static string SafeIdx(IReadOnlyList<string> arr, int i) => (arr != null && i >= 0 && i < arr.Count) ? arr[i] : "?";

    // Mirrors the frontend's scaleLabel() so the Scale filter matches what users see on the card.
    private static string ScaleLabel(int s) => s switch
    {
        0 => "XXXS",
        255 => "XXXL",
        <= 50 => "XS",
        <= 110 => "S",
        <= 170 => "M",
        _ => "L",
    };

    private static string SpeciesName(int species)
    {
        try { return GameInfo.GetStrings(2).specieslist[species]; }
        catch { return ((Species)species).ToString(); }
    }
}
