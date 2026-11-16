using Npgsql;
using Ganss.Xss;
namespace AlManassa.Web;
public record AdminLesson(Guid Id,string Course,string Stage,string Title,string Content,string? VideoProvider,string? VideoReference);
public record AdminVideo(Guid Id,string Course,string Stage,string Query,string? VideoId,string Status);
public sealed class ContentEditor(IConfiguration config) {
 private string Connection {
  get {
   var raw=config["DATABASE_URL"]??throw new InvalidOperationException("DATABASE_URL missing");
   if(!raw.StartsWith("postgres",StringComparison.OrdinalIgnoreCase))return raw;
   var u=new Uri(raw);var parts=u.UserInfo.Split(':',2);
   return new NpgsqlConnectionStringBuilder {Host=u.Host,Port=u.IsDefaultPort?5432:u.Port,Database=Uri.UnescapeDataString(u.AbsolutePath.Trim('/')),Username=Uri.UnescapeDataString(parts[0]),Password=parts.Length>1?Uri.UnescapeDataString(parts[1]):"",SslMode=SslMode.Require}.ConnectionString;
  }
 }

 public record AdminCourse(Guid Id,string TitleAr,string TitleEn,string Description,string Category,int DurationWeeks,bool Published);
 public async Task<List<AdminCourse>> CoursesAsync(){
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("SELECT id,title_ar,title_en,COALESCE(description,''),category,duration_weeks,published FROM courses ORDER BY created_at DESC",db);
  var result=new List<AdminCourse>();await using var r=await cmd.ExecuteReaderAsync();
  while(await r.ReadAsync())result.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.GetBoolean(6)));
  return result;
 }
 public async Task SaveCourseAsync(Guid? id,string ar,string en,string description,string category,int weeks,bool published){
  if(string.IsNullOrWhiteSpace(ar)||ar.Length>250||string.IsNullOrWhiteSpace(en)||en.Length>250||description.Length>10000||string.IsNullOrWhiteSpace(category)||category.Length>100||weeks is <1 or >52)throw new ArgumentException("Invalid course");
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using(var check=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM subjects WHERE slug=@category AND active=true)",db)) {
   check.Parameters.AddWithValue("category",category.Trim());
   if(await check.ExecuteScalarAsync() is not true)throw new ArgumentException("Select an active subject");
  }
  await using var cmd=new NpgsqlCommand(id.HasValue?
   "UPDATE courses SET title_ar=@ar,title_en=@en,description=@description,category=@category,duration_weeks=@weeks,published=@published WHERE id=@id":
   "INSERT INTO courses(id,title_ar,title_en,description,category,duration_weeks,published) VALUES(@id,@ar,@en,@description,@category,@weeks,@published)",db);
  cmd.Parameters.AddWithValue("id",id??Guid.NewGuid());cmd.Parameters.AddWithValue("ar",ar.Trim());cmd.Parameters.AddWithValue("en",en.Trim());cmd.Parameters.AddWithValue("description",description.Trim());cmd.Parameters.AddWithValue("category",category.Trim());cmd.Parameters.AddWithValue("weeks",weeks);cmd.Parameters.AddWithValue("published",published);
  if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Course not found");
 }
 public async Task DeleteCourseAsync(Guid id){
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("DELETE FROM courses WHERE id=@id AND NOT EXISTS(SELECT 1 FROM course_enrollments WHERE course_id=@id) AND NOT EXISTS(SELECT 1 FROM course_stages WHERE course_id=@id)",db);
  cmd.Parameters.AddWithValue("id",id);
  if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Remove stages first; enrolled courses cannot be deleted. Unpublish instead.");
 }
 public async Task AddStageAsync(Guid courseId,string title){
  if(string.IsNullOrWhiteSpace(title)||title.Length>250)throw new ArgumentException("Invalid stage");
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("INSERT INTO course_stages(id,course_id,title_ar,position,passing_percent) SELECT @id,@course,@title,COALESCE((SELECT MAX(position)+1 FROM course_stages WHERE course_id=@course),1),70 WHERE EXISTS(SELECT 1 FROM courses WHERE id=@course)",db);
  cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("course",courseId);cmd.Parameters.AddWithValue("title",title.Trim());
  if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Course not found");
 }
 public static string CleanLessonHtml(string html){
  var sanitizer=new HtmlSanitizer();
  sanitizer.AllowedTags.Clear();
  foreach(var tag in new[]{"p","div","br","strong","b","em","i","u","s","h2","h3","h4","ul","ol","li","blockquote","span"})sanitizer.AllowedTags.Add(tag);
  sanitizer.AllowedAttributes.Clear();
  return sanitizer.Sanitize(html);
 }
 public async Task AddLessonAsync(Guid stageId,string title,string content){
  if(string.IsNullOrWhiteSpace(title)||title.Length>250||string.IsNullOrWhiteSpace(content)||content.Length>30000)throw new ArgumentException("Invalid lesson");
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("""
   INSERT INTO stage_lessons(id,stage_id,title_ar,content,position)
   SELECT @id,@stage,@title,@content,COALESCE((SELECT MAX(l.position)+1 FROM stage_lessons l WHERE l.stage_id=@stage),1)
   WHERE EXISTS(SELECT 1 FROM course_stages WHERE id=@stage)
   """,db);
  cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("stage",stageId);cmd.Parameters.AddWithValue("title",title.Trim());cmd.Parameters.AddWithValue("content",CleanLessonHtml(content.Trim()));
  if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Stage not found");
 }
 public async Task<List<(Guid Id,Guid CourseId,string Title,int Position)>> StagesAsync(){
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("SELECT id,course_id,title_ar,position FROM course_stages ORDER BY course_id,position",db);
  var list=new List<(Guid,Guid,string,int)>();await using var r=await cmd.ExecuteReaderAsync();
  while(await r.ReadAsync())list.Add((r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetInt32(3)));
  return list;
 }
 public async Task RenameStageAsync(Guid id,string title){
  if(string.IsNullOrWhiteSpace(title)||title.Length>250)throw new ArgumentException("Invalid title");
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("UPDATE course_stages SET title_ar=@title WHERE id=@id",db);
  cmd.Parameters.AddWithValue("title",title.Trim());cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync();
 }
 public async Task DeleteLessonAsync(Guid id){
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("DELETE FROM stage_lessons WHERE id=@id AND NOT EXISTS(SELECT 1 FROM lesson_completions WHERE lesson_id=@id)",db);
  cmd.Parameters.AddWithValue("id",id);
  if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Completed lessons cannot be deleted");
 }

 public async Task<List<AdminLesson>> LessonsAsync() {
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("SELECT l.id,c.title_ar,s.title_ar,l.title_ar,l.content,l.video_provider,l.video_reference FROM stage_lessons l JOIN course_stages s ON s.id=l.stage_id JOIN courses c ON c.id=s.course_id ORDER BY c.title_ar,s.position,l.position",db);
  var list=new List<AdminLesson>();await using var r=await cmd.ExecuteReaderAsync();
  while(await r.ReadAsync())list.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6)));
  return list;
 }
 public async Task SaveLessonAsync(Guid id,string title,string content) {
  if(string.IsNullOrWhiteSpace(title)||title.Length>250||string.IsNullOrWhiteSpace(content)||content.Length>30000)throw new ArgumentException("Invalid lesson");
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("UPDATE stage_lessons SET title_ar=@title,content=@content WHERE id=@id",db);
  cmd.Parameters.AddWithValue("title",title.Trim());cmd.Parameters.AddWithValue("content",content.Trim());cmd.Parameters.AddWithValue("id",id);
  if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Lesson not found");
 }

 public async Task SetLessonYoutubeAsync(Guid lessonId,string url) {
  string? videoId=null;
  if(!string.IsNullOrWhiteSpace(url)){
   if(!Uri.TryCreate(url.Trim(),UriKind.Absolute,out var uri) || uri.Scheme!="https")
    throw new ArgumentException("Use a valid HTTPS YouTube URL");
   var host=uri.Host.ToLowerInvariant();
   if(host=="youtu.be")videoId=uri.AbsolutePath.Trim('/').Split('/')[0];
   else if(host is "youtube.com" or "www.youtube.com" or "m.youtube.com"){
    if(uri.AbsolutePath.StartsWith("/shorts/") || uri.AbsolutePath.StartsWith("/embed/"))videoId=uri.AbsolutePath.Trim('/').Split('/').Last();
    else if(uri.AbsolutePath=="/watch")videoId=uri.Query.TrimStart('?').Split('&').Select(x=>x.Split('=',2)).Where(x=>x.Length==2 && x[0]=="v").Select(x=>Uri.UnescapeDataString(x[1])).FirstOrDefault();
   }
   if(videoId is null || videoId.Length!=11 || !videoId.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_'||c=='-'))
    throw new ArgumentException("Invalid YouTube video URL");
  }
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("UPDATE stage_lessons SET video_provider=@provider,video_reference=@reference WHERE id=@id",db);
  cmd.Parameters.AddWithValue("id",lessonId);
  cmd.Parameters.AddWithValue("provider",videoId is null?DBNull.Value:(object)"youtube");
  cmd.Parameters.AddWithValue("reference",videoId is null?DBNull.Value:(object)videoId);
  if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Lesson not found");
 }
 public async Task<List<AdminVideo>> VideosAsync() {
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("SELECT v.id,c.title_ar,s.title_ar,v.search_query,v.youtube_video_id,v.status FROM content_video_suggestions v JOIN courses c ON c.id=v.course_id JOIN course_stages s ON s.id=v.stage_id ORDER BY c.title_ar,s.position",db);
  var list=new List<AdminVideo>();await using var r=await cmd.ExecuteReaderAsync();
  while(await r.ReadAsync())list.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.GetString(5)));
  return list;
 }
 public async Task ReviewVideoAsync(Guid id,string videoId,bool approve) {
  if(approve && (videoId.Length!=11||!videoId.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_'||c=='-')))throw new ArgumentException("Invalid YouTube video ID");
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var tx=await db.BeginTransactionAsync();
  await using var cmd=new NpgsqlCommand("UPDATE content_video_suggestions SET youtube_video_id=@video,status=@status WHERE id=@id RETURNING stage_id",db,tx);
  cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("video",approve?(object)videoId:DBNull.Value);cmd.Parameters.AddWithValue("status",approve?"Approved":"Rejected");
  var stage=await cmd.ExecuteScalarAsync();
  if(stage is not Guid sid)throw new InvalidOperationException("Suggestion not found");
  if(approve) {
   await using var update=new NpgsqlCommand("UPDATE stage_lessons SET video_provider='youtube',video_reference=@video WHERE stage_id=@stage AND position=1",db,tx);
   update.Parameters.AddWithValue("video",videoId);update.Parameters.AddWithValue("stage",sid);await update.ExecuteNonQueryAsync();
  }
  await tx.CommitAsync();
 }
}
