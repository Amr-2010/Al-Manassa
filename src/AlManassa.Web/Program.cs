using AlManassa.Web;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<CourseStore>();
builder.Services.AddHttpClient("ai", c=>{c.BaseAddress=new Uri("https://api.openai.com/");c.Timeout=TimeSpan.FromSeconds(90);});
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new {status="ok",application="Al-Manassa Web"}));
app.MapGet("/health/database", async (CourseStore store, CancellationToken ct) => { try { var counts = await store.CountsAsync(ct); return Results.Ok(new {status="connected",courseCount=counts.Total}); } catch (Exception ex) { var reason = ex switch { InvalidOperationException => "configuration_missing", Npgsql.PostgresException pg when pg.SqlState == "42P01" => "schema_missing", Npgsql.PostgresException pg when pg.SqlState == "28P01" => "authentication_failed", Npgsql.NpgsqlException => "connection_failed", _ => "database_error" }; return Results.Json(new {status="unavailable",reason},statusCode:503); } });
app.MapPost("/api/admin/ai/curriculum", async (HttpContext ctx, CurriculumInput input, CourseStore store, IHttpClientFactory clients, IConfiguration config, CancellationToken ct) => {
    // Temporary server-side API key gate. Replace with ASP.NET Identity roles before exposing admin UI.
    var expected=config["AI_ADMIN_TOKEN"];
    var supplied=ctx.Request.Headers["X-Admin-Token"].ToString();
    if(string.IsNullOrWhiteSpace(expected)||string.IsNullOrWhiteSpace(supplied)||
       !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
        return Results.Unauthorized();
    var key=config["OPENAI_API_KEY"];
    if(string.IsNullOrWhiteSpace(key)) return Results.Problem("AI provider is not configured",statusCode:503);
    if(string.IsNullOrWhiteSpace(input.Topic)||input.Topic.Length>250||input.DurationWeeks is <1 or >24) return Results.BadRequest("Invalid topic or duration");
    var prompt=$"Create a professional {input.DurationWeeks}-week curriculum for {input.Topic}. Level: {input.Level}. Language: {input.Language}. Include weekly units, learning objectives, lessons, practical activities and assessments. Output JSON only, with fields title, overview, units (array). This is a draft requiring human approval.";
    var request=new HttpRequestMessage(HttpMethod.Post,"v1/chat/completions");
    request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
    request.Content=JsonContent.Create(new {model=config["OPENAI_MODEL"]??"gpt-4.1-mini",messages=new[]{new {role="system",content="You are an educational curriculum designer. Return valid JSON only. Never claim accreditation."},new{role="user",content=prompt}},response_format=new{type="json_object"}});
    try {
        var response=await clients.CreateClient("ai").SendAsync(request,ct);
        if(!response.IsSuccessStatusCode) return Results.Problem("AI provider request failed",statusCode:502);
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var raw=doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if(string.IsNullOrWhiteSpace(raw)) return Results.Problem("AI returned empty content",statusCode:502);
        using var validated=JsonDocument.Parse(raw);
        var id=Guid.NewGuid();
        await store.SaveSuggestionAsync(id,input.Topic,input.Level,input.Language,input.DurationWeeks,raw,"OpenAI",ct);
        return Results.Ok(new {id,status="PendingReview",content=validated.RootElement.Clone()});
    } catch(Exception ex) when(ex is HttpRequestException or JsonException or Npgsql.NpgsqlException or TaskCanceledException) {
        return Results.Problem("AI or database operation failed; check server configuration",statusCode:503);
    }
});
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<AlManassa.Web.Components.App>().AddInteractiveServerRenderMode();
app.Run();
record CurriculumInput(string Topic,string Level,string Language,int DurationWeeks);
