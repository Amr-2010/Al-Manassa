using Npgsql;
namespace AlManassa.Web;
public sealed class LessonVideoStore(IConfiguration config){
 private string Connection {
  get {
   var raw=config["DATABASE_URL"]??throw new InvalidOperationException("DATABASE_URL missing");
   if(!raw.StartsWith("postgres",StringComparison.OrdinalIgnoreCase))return raw;
   var u=new Uri(raw);var p=u.UserInfo.Split(':',2);
   return new NpgsqlConnectionStringBuilder{Host=u.Host,Port=u.IsDefaultPort?5432:u.Port,Database=Uri.UnescapeDataString(u.AbsolutePath.Trim('/')),Username=Uri.UnescapeDataString(p[0]),Password=p.Length>1?Uri.UnescapeDataString(p[1]):"",SslMode=SslMode.Require}.ConnectionString;
  }
 }
 public async Task SaveAsync(Guid lessonId,string filename,string mime,Stream stream,CancellationToken ct=default){
  if(mime is not ("video/mp4" or "video/webm"))throw new ArgumentException("Only MP4 or WebM videos are supported");
  const int max=50*1024*1024;
  using var buffer=new MemoryStream();
  var chunk=new byte[81920];int read;
  while((read=await stream.ReadAsync(chunk,ct))>0){
   if(buffer.Length+read>max)throw new ArgumentException("Video exceeds 50 MB. Use a YouTube link for larger videos.");
   await buffer.WriteAsync(chunk.AsMemory(0,read),ct);
  }
  if(buffer.Length==0)throw new ArgumentException("Empty video");
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await using(var cmd=new NpgsqlCommand("""
   INSERT INTO lesson_uploaded_videos(lesson_id,filename,mime_type,data)
   VALUES(@id,@name,@mime,@data)
   ON CONFLICT(lesson_id) DO UPDATE SET filename=excluded.filename,mime_type=excluded.mime_type,data=excluded.data,uploaded_at=now()
   """,db,tx)){
   cmd.Parameters.AddWithValue("id",lessonId);cmd.Parameters.AddWithValue("name",Path.GetFileName(filename));
   cmd.Parameters.AddWithValue("mime",mime);cmd.Parameters.AddWithValue("data",buffer.ToArray());
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await using(var cmd=new NpgsqlCommand("UPDATE stage_lessons SET video_provider='hosted',video_reference=@id WHERE id=@id",db,tx)){
   cmd.Parameters.AddWithValue("id",lessonId);await cmd.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
 }
 public async Task<(byte[] Data,string Mime)?> GetAsync(Guid lessonId,Guid userId,bool admin){
  await using var db=new NpgsqlConnection(Connection);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("""
   SELECT v.data,v.mime_type FROM lesson_uploaded_videos v
   JOIN stage_lessons l ON l.id=v.lesson_id
   JOIN course_stages s ON s.id=l.stage_id
   WHERE v.lesson_id=@id AND (@admin OR EXISTS(
    SELECT 1 FROM course_enrollments e WHERE e.course_id=s.course_id AND e.user_id=@user))
   """,db);
  cmd.Parameters.AddWithValue("id",lessonId);cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("admin",admin);
  await using var r=await cmd.ExecuteReaderAsync();
  return await r.ReadAsync()?(r.GetFieldValue<byte[]>(0),r.GetString(1)):null;
 }
}
