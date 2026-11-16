using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using System.Security.Claims;
using System.Net;
using AlManassa.Web;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<CourseStore>();
builder.Services.AddScoped<AuthStore>();
builder.Services.AddScoped<LearningStore>();
builder.Services.AddScoped<LessonMediaStore>();
builder.Services.AddScoped<ContentEditor>();
builder.Services.AddScoped<CurriculumAiService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => { options.LoginPath="/login"; options.Cookie.Name="AlManassa.Auth"; options.Cookie.HttpOnly=true; options.Cookie.SecurePolicy=CookieSecurePolicy.Always; options.Cookie.SameSite=SameSiteMode.Lax; options.ExpireTimeSpan=TimeSpan.FromDays(30); options.SlidingExpiration=true; options.Events.OnRedirectToLogin=context=>{context.Response.Redirect("/login?returnUrl="+Uri.EscapeDataString(context.Request.Path+context.Request.QueryString));return Task.CompletedTask;}; });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();
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
    var prompt=$"Create a professional {input.DurationWeeks}-week curriculum for {input.Topic}. Level: {input.Level}. Language: {input.Language}. Output JSON only with title, overview, stages array (each stage has title, lessons array with title/content/video_provider/video_reference, and quiz questions array with prompt and four options containing label/is_correct), and final_exam array of exactly 20 multiple-choice questions in the same format. Every question must have exactly one correct option. Include clear learning objectives and original educational content. Do not invent video URLs. This is a draft requiring human approval before publishing.";
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

// Bootstrap only the dedicated Al-Manassa schema. SQL uses IF NOT EXISTS and ON CONFLICT DO NOTHING.
using (var scope=app.Services.CreateScope()) {
    try {
        await scope.ServiceProvider.GetRequiredService<AuthStore>().EnsureAsync();
        await scope.ServiceProvider.GetRequiredService<CourseStore>().EnsureDemoSchemaAsync();
        await scope.ServiceProvider.GetRequiredService<LessonMediaStore>().EnsureAsync();
        app.Logger.LogInformation("Al-Manassa database schema ready");
    } catch (Exception ex) {
        app.Logger.LogError("Al-Manassa database initialization failed ({ErrorType})",ex.GetType().Name);
    }
}
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (ctx,next)=>{
 if(ctx.User.Identity?.IsAuthenticated==true && Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)
    && !ctx.Request.Path.StartsWithSegments("/_framework")
    && !ctx.Request.Path.StartsWithSegments("/health")
    && !ctx.Request.Path.StartsWithSegments("/api/lesson-assets")){
  try{await ctx.RequestServices.GetRequiredService<AuthStore>().RecordActivityAsync(id);}
  catch(Exception ex){app.Logger.LogWarning("Activity update unavailable: {Type}",ex.GetType().Name);}
 }
 await next();
});

app.UseStaticFiles();
app.MapGet("/login", (HttpContext ctx, IAntiforgery af) => {
    if(ctx.User.Identity?.IsAuthenticated==true) return Results.Redirect(ctx.User.IsInRole("Admin")?"/dashboard":"/");
    var token=af.GetAndStoreTokens(ctx).RequestToken;
    return Results.Content(AuthPage("دخول الحساب","/login",token!,ctx.Request.Query["returnUrl"].ToString()),"text/html; charset=utf-8");
});
app.MapGet("/register", (HttpContext ctx, IAntiforgery af) => {
    if(ctx.User.Identity?.IsAuthenticated==true) return Results.Redirect(ctx.User.IsInRole("Admin")?"/dashboard":"/");
    var token=af.GetAndStoreTokens(ctx).RequestToken;
    return Results.Content(AuthPage("إنشاء حساب طالب","/register",token!),"text/html; charset=utf-8");
});
app.MapPost("/login", async (HttpContext ctx, IAntiforgery af, AuthStore users) => {
    if(!await af.IsRequestValidAsync(ctx)) return Results.BadRequest("Invalid form token");
    var form=await ctx.Request.ReadFormAsync();
    var email=form["email"].ToString();var password=form["password"].ToString();
    var remember=form["remember"].ToString()=="on";
    var returnUrl=form["returnUrl"].ToString();
    if(email.Length>320||password.Length>1024) return Results.BadRequest();
    var account=await users.ValidateAsync(email,password);
    if(account is null) return Results.Content("بيانات الدخول غير صحيحة. <a href='/login'>حاول مرة أخرى</a>","text/html; charset=utf-8",statusCode:401);
    var claims=new[]{new Claim(ClaimTypes.NameIdentifier,account.Value.Id.ToString()),new Claim(ClaimTypes.Email,email),new Claim(ClaimTypes.Name,email.Split('@')[0]),new Claim(ClaimTypes.Role,account.Value.Role)};
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)),new AuthenticationProperties { IsPersistent=remember, AllowRefresh=true, ExpiresUtc=remember?DateTimeOffset.UtcNow.AddDays(30):DateTimeOffset.UtcNow.AddHours(8) });
    var defaultDestination=account.Value.Role=="Admin"?"/dashboard":"/";
    if(!string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith("/") && !returnUrl.StartsWith("//") && !returnUrl.Contains("\\") && (!returnUrl.StartsWith("/dashboard",StringComparison.OrdinalIgnoreCase)||account.Value.Role=="Admin")) return Results.Redirect(returnUrl);
    return Results.Redirect(defaultDestination);
});
app.MapPost("/register", async (HttpContext ctx, IAntiforgery af, AuthStore users) => {
    if(!await af.IsRequestValidAsync(ctx)) return Results.BadRequest("Invalid form token");
    var form=await ctx.Request.ReadFormAsync();
    var email=form["email"].ToString().Trim();var password=form["password"].ToString();
    if(email.Length is <5 or >320||!email.Contains('@')||password.Length is <12 or >128) return Results.BadRequest("Email invalid or password must be 12-128 characters");
    var created=await users.RegisterAsync(email,password);
    return created?Results.Redirect("/login?registered=1"):Results.Conflict("Account already exists");
});
app.MapPost("/logout",async (HttpContext ctx,IAntiforgery af) => {if(!await af.IsRequestValidAsync(ctx)) return Results.BadRequest();await ctx.SignOutAsync();return Results.Redirect("/");});
app.Use(async (ctx,next) => {
    if(ctx.Request.Path.StartsWithSegments("/dashboard") && !(ctx.User.Identity?.IsAuthenticated==true && ctx.User.IsInRole("Admin"))) {ctx.Response.Redirect(ctx.User.Identity?.IsAuthenticated==true?"/":"/login?returnUrl="+Uri.EscapeDataString(ctx.Request.Path+ctx.Request.QueryString));return;}
    await next();
});


app.MapPost("/api/admin/subjects/save", async (HttpContext ctx, IAntiforgery af, CourseStore store) => {
 if(!await af.IsRequestValidAsync(ctx))return Results.BadRequest("Invalid form token");
 var form=await ctx.Request.ReadFormAsync();
 var slug=form["slug"].ToString().Trim();
 var name=form["name"].ToString().Trim();
 var icon=form["icon"].ToString().Trim();
 var description=form["description"].ToString().Trim();
 try {
  await store.SaveSubjectAsync(slug,name,icon,description);
  return Results.Redirect("/dashboard/subjects?notice="+Uri.EscapeDataString("تم حفظ القسم بنجاح"));
 }catch(Exception ex) {
  return Results.Redirect("/dashboard/subjects?notice="+Uri.EscapeDataString("فشل الحفظ: "+ex.Message));
 }
}).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute{Roles="Admin"});
app.MapPost("/api/admin/subjects/delete", async (HttpContext ctx, IAntiforgery af, CourseStore store) => {
 if(!await af.IsRequestValidAsync(ctx))return Results.BadRequest("Invalid form token");
 var form=await ctx.Request.ReadFormAsync();
 var slug=form["slug"].ToString().Trim();
 try {
  var message=await store.DeleteSubjectAsync(slug);
  return Results.Redirect("/dashboard/subjects?notice="+Uri.EscapeDataString(message));
 }catch(Exception ex){
  return Results.Redirect("/dashboard/subjects?notice="+Uri.EscapeDataString("فشل الحذف: "+ex.Message));
 }
}).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute{Roles="Admin"});
app.MapGet("/api/lesson-assets/{id:guid}",async (Guid id,HttpContext ctx,LessonMediaStore media)=>{
 if(ctx.User.Identity?.IsAuthenticated!=true || !Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier),out var userId))return Results.Unauthorized();
 var item=await media.GetAsync(id,userId,ctx.User.IsInRole("Admin"));
 return item is null?Results.NotFound():Results.File(item.Value.Bytes,item.Value.Mime,enableRangeProcessing:false);
}).RequireAuthorization();
app.MapPost("/api/admin/lessons/{lessonId:guid}/assets",async (Guid lessonId,HttpRequest req,LessonMediaStore media)=>{
 if(!req.HasFormContentType)return Results.BadRequest();
 var form=await req.ReadFormAsync();var file=form.Files.GetFile("file");
 if(file is null||file.Length is <1 or >10485760)return Results.BadRequest("File must be 1-10 MB");
 await using var stream=file.OpenReadStream();using var ms=new MemoryStream();await stream.CopyToAsync(ms);
 try {var id=await media.UploadAsync(lessonId,file.FileName,file.ContentType,ms.ToArray());return Results.Ok(new{id});}
 catch(ArgumentException){return Results.BadRequest("Unsupported file type");}
}).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute{Roles="Admin"});
app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<AlManassa.Web.Components.App>().AddInteractiveServerRenderMode();
app.Run();
static string AuthPage(string title,string action,string token,string returnUrl="") {
    var heading=WebUtility.HtmlEncode(title);
    var endpoint=action=="/register"?"/register":"/login";
    var passwordAutocomplete=endpoint=="/login"?"current-password":"new-password";
    var passwordMinimum=endpoint=="/login"?"1":"12";
    var switchLink=endpoint=="/login"?"/register":"/login";
    var switchText=endpoint=="/login"?"إنشاء حساب طالب جديد":"لديك حساب بالفعل؟";
    var safeReturn=returnUrl.StartsWith("/") && !returnUrl.StartsWith("//") && !returnUrl.Contains("\\") ? returnUrl : "/";
    return "<!doctype html><html lang='ar' dir='rtl'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>"+heading+
        " | المنصة</title><style>body{margin:0;background:#edf3fb;color:#183b69;font-family:Tahoma,Arial;display:grid;min-height:100vh;place-items:center}main{width:min(390px,90vw);background:white;padding:34px;border-radius:22px;box-shadow:0 15px 45px #11143d22}label{display:block;margin:18px 0 7px}input{box-sizing:border-box;width:100%;padding:13px;border:1px solid #d4d5e2;border-radius:10px}button{width:100%;margin-top:22px;background:#254e7e;color:#ffd96d;padding:15px;border:0;border-radius:10px;font-size:16px}a{color:#db8900}</style></head><body><main><h1>"+heading+
        "</h1><p>المنصة · Al-Manassa</p><form method='post' action='"+endpoint+"'><input type='hidden' name='__RequestVerificationToken' value='"+WebUtility.HtmlEncode(token)+
        "'><input type='hidden' name='returnUrl' value='"+WebUtility.HtmlEncode(safeReturn)+"'><label>"+(endpoint=="/login"?"اسم المستخدم أو البريد الإلكتروني":"البريد الإلكتروني")+"</label><input name='email' type='"+(endpoint=="/login"?"text":"email")+"' autocomplete='username' required maxlength='320'><label>كلمة المرور</label><input name='password' type='password' autocomplete='"+passwordAutocomplete+"' required minlength='"+passwordMinimum+
        "'>"+(endpoint=="/login"?"<label style='display:flex;align-items:center;gap:8px'><input type='checkbox' name='remember' style='width:auto' checked> تذكرني لمدة 30 يومًا</label>":"")+"<button type='submit'>"+heading+"</button></form><p><a href='"+switchLink+"'>"+switchText+"</a></p><p><a href='/'>العودة للرئيسية</a></p></main></body></html>";
}

record CurriculumInput(string Topic,string Level,string Language,int DurationWeeks);
