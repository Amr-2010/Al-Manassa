using Npgsql;
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
