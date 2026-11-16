using System.Collections.Concurrent;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(o => o.AddPolicy("Web", p => p.WithOrigins(builder.Configuration["WebOrigin"] ?? "http://localhost:5100").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors("Web");
var courses = new ConcurrentDictionary<int,Course>();
foreach(var c in new [] { new Course(1,"أساسيات الذكاء الاصطناعي","AI Fundamentals","Technology",8,"Draft"),new Course(2,"تطوير تطبيقات .NET",".NET Application Development","Programming",6,"Published"),new Course(3,"التسويق الرقمي","Digital Marketing","Business",4,"Published") }) courses[c.Id]=c;
app.MapGet("/health", () => Results.Ok(new {status="ok",platform="Al-Manassa",mode="demo"}));
app.MapGet("/api/courses", () => Results.Ok(courses.Values.OrderBy(c=>c.Id)));
app.MapGet("/api/dashboard/summary", () => Results.Ok(new {courses=courses.Count,published=courses.Values.Count(c=>c.Status=="Published"),students=0,instructors=0,aiSuggestions=0,mode="demo"}));
// Demonstration only: no model provider, no persistence and no authentication. Do not expose admin routes publicly.
app.MapPost("/api/demo/curriculum/suggest", (CurriculumRequest request) => {
 if(string.IsNullOrWhiteSpace(request.Topic) || request.DurationWeeks < 1 || request.DurationWeeks > 52) return Results.BadRequest(new {error="Provide a topic and duration between 1 and 52 weeks."});
 var units = Enumerable.Range(1,Math.Min(request.DurationWeeks,16)).Select(i => new {week=i,title=$"{request.Topic} — الوحدة {i}",objectives=new [] {"فهم المفاهيم الأساسية", "تطبيق عملي", "تقييم قصير"},lessons=new [] {$"مقدمة الوحدة {i}","تطبيقات وتمارين"}});
 return Results.Ok(new {title=request.Topic,request.Level,request.Language,request.DurationWeeks,source="rule-based demo; NOT AI-generated",status="PendingReview",units});
});
app.Run();
record Course(int Id,string TitleAr,string TitleEn,string Category,int DurationWeeks,string Status);
record CurriculumRequest(string Topic,string Level,string Language,int DurationWeeks);
