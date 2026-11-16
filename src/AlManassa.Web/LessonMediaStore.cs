using Npgsql;
namespace AlManassa.Web;
public sealed class LessonMediaStore(IConfiguration config) {
 private string ConnectionString {
  get {
   var raw=config["DATABASE_URL"]??throw new InvalidOperationException("DATABASE_URL missing");
   if(!raw.StartsWith("postgres",StringComparison.OrdinalIgnoreCase))return raw;
   var u=new Uri(raw);var p=u.UserInfo.Split(':',2);
   return new NpgsqlConnectionStringBuilder{Host=u.Host,Port=u.IsDefaultPort?5432:u.Port,Database=Uri.UnescapeDataString(u.AbsolutePath.Trim('/')),Username=Uri.UnescapeDataString(p[0]),Password=p.Length>1?Uri.UnescapeDataString(p[1]):"",SslMode=SslMode.Require}.ConnectionString;
  }
 }
 public async Task EnsureAsync() {
  var path=Path.Combine(AppContext.BaseDirectory,"database","005_lesson_media_progress.sql");
  await using var db=new NpgsqlConnection(ConnectionString);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand(await File.ReadAllTextAsync(path),db);
  await cmd.ExecuteNonQueryAsync();
 }
 public async Task<Guid> UploadAsync(Guid lessonId,string filename,string mime,byte[] data) {
  if(data.Length is <1 or >10485760 || mime is not ("image/png" or "image/jpeg" or "image/webp" or "application/pdf"))
   throw new ArgumentException("Only PNG, JPEG, WebP or PDF files up to 10 MB are supported");
  var id=Guid.NewGuid();
  await using var db=new NpgsqlConnection(ConnectionString);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("INSERT INTO lesson_assets(id,lesson_id,filename,mime_type,bytes,size_bytes) VALUES(@id,@lesson,@name,@mime,@bytes,@size)",db);
  cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("lesson",lessonId);
  cmd.Parameters.AddWithValue("name",Path.GetFileName(filename));cmd.Parameters.AddWithValue("mime",mime);
  cmd.Parameters.AddWithValue("bytes",data);cmd.Parameters.AddWithValue("size",data.Length);
  await cmd.ExecuteNonQueryAsync();return id;
 }
 public async Task<(byte[] Bytes,string Mime)?> GetAsync(Guid assetId,Guid userId,bool admin) {
  await using var db=new NpgsqlConnection(ConnectionString);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("""
   SELECT a.bytes,a.mime_type FROM lesson_assets a
   JOIN stage_lessons l ON l.id=a.lesson_id
   JOIN course_stages s ON s.id=l.stage_id
   WHERE a.id=@id AND (@admin OR EXISTS(SELECT 1 FROM course_enrollments e WHERE e.course_id=s.course_id AND e.user_id=@user))
   """,db);
  cmd.Parameters.AddWithValue("id",assetId);cmd.Parameters.AddWithValue("admin",admin);cmd.Parameters.AddWithValue("user",userId);
  await using var r=await cmd.ExecuteReaderAsync();
  return await r.ReadAsync()?(r.GetFieldValue<byte[]>(0),r.GetString(1)):null;
 }
 public async Task<List<(Guid Id,string Filename,string Mime)>> ListAsync(Guid lessonId,Guid userId) {
  await using var db=new NpgsqlConnection(ConnectionString);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("""
   SELECT a.id,a.filename,a.mime_type FROM lesson_assets a
   JOIN stage_lessons l ON l.id=a.lesson_id JOIN course_stages s ON s.id=l.stage_id
   JOIN course_enrollments e ON e.course_id=s.course_id AND e.user_id=@user
   WHERE a.lesson_id=@lesson ORDER BY a.created_at
   """,db);
  cmd.Parameters.AddWithValue("lesson",lessonId);cmd.Parameters.AddWithValue("user",userId);
  var items=new List<(Guid,string,string)>();await using var r=await cmd.ExecuteReaderAsync();
  while(await r.ReadAsync())items.Add((r.GetGuid(0),r.GetString(1),r.GetString(2)));
  return items;
 }
 public async Task<int> PositionAsync(Guid userId,Guid lessonId) {
  await using var db=new NpgsqlConnection(ConnectionString);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("SELECT playback_seconds FROM student_lesson_progress WHERE user_id=@user AND lesson_id=@lesson",db);
  cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("lesson",lessonId);
  return Convert.ToInt32(await cmd.ExecuteScalarAsync()??0);
 }
 public async Task SavePositionAsync(Guid userId,Guid lessonId,int seconds) {
  if(seconds is <0 or >86400)throw new ArgumentOutOfRangeException(nameof(seconds));
  await using var db=new NpgsqlConnection(ConnectionString);await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("""
   INSERT INTO student_lesson_progress(user_id,lesson_id,playback_seconds)
   SELECT @user,l.id,@seconds FROM stage_lessons l
   JOIN course_stages s ON s.id=l.stage_id
   JOIN course_enrollments e ON e.course_id=s.course_id AND e.user_id=@user
   WHERE l.id=@lesson
   ON CONFLICT(user_id,lesson_id) DO UPDATE SET playback_seconds=excluded.playback_seconds,last_position_at=now()
   """,db);
  cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("lesson",lessonId);cmd.Parameters.AddWithValue("seconds",seconds);
  await cmd.ExecuteNonQueryAsync();
 }
}
