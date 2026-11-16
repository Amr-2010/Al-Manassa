using Npgsql;
namespace AlManassa.Web;
public sealed class LearningStore(IConfiguration config) {
    private string ConnectionString {
        get {
            var raw=config["DATABASE_URL"]??throw new InvalidOperationException("DATABASE_URL missing");
            if(!raw.StartsWith("postgres",StringComparison.OrdinalIgnoreCase)) return raw;
            var u=new Uri(raw);var p=u.UserInfo.Split(':',2);
            return new NpgsqlConnectionStringBuilder{Host=u.Host,Port=u.IsDefaultPort?5432:u.Port,Database=Uri.UnescapeDataString(u.AbsolutePath.Trim('/')),Username=Uri.UnescapeDataString(p[0]),Password=p.Length>1?Uri.UnescapeDataString(p[1]):"",SslMode=SslMode.Require}.ConnectionString;
        }
    }
    public async Task<bool> CanAccessStageAsync(Guid userId,Guid stageId,CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT EXISTS(
              SELECT 1 FROM course_stages s
              JOIN course_enrollments e ON e.course_id=s.course_id AND e.user_id=@user
              WHERE s.id=@stage AND NOT EXISTS (
                SELECT 1 FROM course_stages prior
                WHERE prior.course_id=s.course_id AND prior.position<s.position
                AND (
                  EXISTS(SELECT 1 FROM stage_lessons l WHERE l.stage_id=prior.id
                    AND NOT EXISTS(SELECT 1 FROM lesson_completions done WHERE done.lesson_id=l.id AND done.user_id=@user))
                  OR NOT EXISTS(SELECT 1 FROM assessment_attempts a WHERE a.stage_id=prior.id AND a.user_id=@user AND a.passed=true)
                )
              )
            )
            """,conn);
        cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("stage",stageId);
        return (bool)(await cmd.ExecuteScalarAsync(ct)??false);
    }
    public async Task<bool> CompleteLessonAsync(Guid userId,Guid lessonId,CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            INSERT INTO lesson_completions(user_id,lesson_id)
            SELECT @user,l.id FROM stage_lessons l
            JOIN course_stages s ON s.id=l.stage_id
            JOIN course_enrollments e ON e.course_id=s.course_id AND e.user_id=@user
            WHERE l.id=@lesson
            AND NOT EXISTS(
              SELECT 1 FROM course_stages prior WHERE prior.course_id=s.course_id AND prior.position<s.position
              AND (EXISTS(SELECT 1 FROM stage_lessons pl WHERE pl.stage_id=prior.id AND NOT EXISTS(SELECT 1 FROM lesson_completions lc WHERE lc.lesson_id=pl.id AND lc.user_id=@user))
              OR NOT EXISTS(SELECT 1 FROM assessment_attempts a WHERE a.stage_id=prior.id AND a.user_id=@user AND a.passed))
            )
            ON CONFLICT DO NOTHING
            """,conn);
        cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("lesson",lessonId);
        return await cmd.ExecuteNonQueryAsync(ct)>0;
    }
}
