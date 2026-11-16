var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "Al-Manassa Web", mode = "demo" }));
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<AlManassa.Web.Components.App>().AddInteractiveServerRenderMode();
app.Run();
