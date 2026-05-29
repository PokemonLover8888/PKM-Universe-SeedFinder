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
app.UseStaticFiles();

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

app.MapPost("/api/host", async (HttpContext ctx, HostRequest req) =>
{
    if (!ctx.Request.Cookies.TryGetValue("pku_sess", out var t) || !sessions.TryGetValue(t, out var u))
        return Results.Json(new { ok = false, error = "login" }, statusCode: 401);
    if (!u.Verified)
        return Results.Json(new { ok = false, error = "notmember" }, statusCode: 403);
    try
    {
        var url = $"{botBase}/api/raid/add?seed={Uri.EscapeDataString(req.Seed ?? "")}&stars={req.Stars}&progress={req.Progress}&location={Uri.EscapeDataString(req.Location ?? "Kitakami")}";
        return Results.Content(await botHttp.GetStringAsync(url), "application/json");
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = "bot unreachable: " + ex.Message }); }
});

app.MapGet("/api/species", () =>
{
    var list = GameInfo.GetStrings(2).specieslist;
    var arr = new List<object>(list.Length);
    for (int i = 1; i < list.Length; i++)
        if (!string.IsNullOrWhiteSpace(list[i])) arr.Add(new { id = i, name = list[i] });
    return Results.Ok(arr);
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
    long? MaxScan);          // seeds to scan; default 60M, capped 300M

public sealed record LookupRequest(string? Game, string? Location, int? StoryProgress, int? Stars, string[]? Seeds);
public sealed record HostRequest(string? Seed, int Stars, int Progress, string? Location);
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
        var blank = new SearchRequest(null, null, null, null, null, null, null, null, null, null);
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

        if (!fullDetails)
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

        int[]? baseStats = null;
        try { var pi = PersonalTable.SV.GetFormEntry(enc.Species, enc.Form); baseStats = new[] { pi.HP, pi.ATK, pi.DEF, pi.SPA, pi.SPD, pi.SPE }; }
        catch { }

        return new RaidResultDto(seedHex, enc.Species, SpeciesName(enc.Species), enc.Form, shiny,
            stars, tera, ((MoveType)tera).ToString(), enc.FlawlessIVCount,
            ivs, pk.Nature.ToString(), genderStr, pk.Scale, ra,
            abilityName, hidden, moves, rewards, baseStats);
    }

    private static string SafeIdx(IReadOnlyList<string> arr, int i) => (arr != null && i >= 0 && i < arr.Count) ? arr[i] : "?";

    private static string SpeciesName(int species)
    {
        try { return GameInfo.GetStrings(2).specieslist[species]; }
        catch { return ((Species)species).ToString(); }
    }
}
