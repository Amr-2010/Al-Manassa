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
    public async Task<(string Title,string Description)?> CourseInfoAsync(Guid courseId,CancellationToken ct=default) {
        await using var db=new NpgsqlConnection(ConnectionString);await db.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("SELECT title_ar,COALESCE(description,'') FROM courses WHERE id=@id AND published=true",db);
        cmd.Parameters.AddWithValue("id",courseId);
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)?(reader.GetString(0),reader.GetString(1)):null;
    }
    public async Task<List<(Guid Id,string Title)>> PublicLessonTitlesAsync(Guid courseId,CancellationToken ct=default){
        var list=new List<(Guid,string)>();
        await using var db=new NpgsqlConnection(ConnectionString);await db.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
          SELECT l.id,l.title_ar FROM stage_lessons l
          JOIN course_stages s ON s.id=l.stage_id
          JOIN courses c ON c.id=s.course_id
          WHERE c.id=@id AND c.published=true ORDER BY s.position,l.position
          """,db);
        cmd.Parameters.AddWithValue("id",courseId);
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))list.Add((reader.GetGuid(0),reader.GetString(1)));
        return list;
    }
    public async Task<Guid?> ResumeStageAsync(Guid userId,Guid courseId,CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT s.id FROM course_stages s
            JOIN course_enrollments e ON e.course_id=s.course_id AND e.user_id=@user
            WHERE s.course_id=@course
              AND (NOT EXISTS(SELECT 1 FROM assessment_attempts a WHERE a.stage_id=s.id AND a.user_id=@user AND a.passed)
                OR EXISTS(SELECT 1 FROM stage_lessons l WHERE l.stage_id=s.id AND NOT EXISTS
                  (SELECT 1 FROM lesson_completions lc WHERE lc.lesson_id=l.id AND lc.user_id=@user)))
            ORDER BY s.position LIMIT 1
            """,conn);
        cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("course",courseId);
        var value=await cmd.ExecuteScalarAsync(ct);
        return value is Guid id?id:null;
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
    public async Task<bool> EnrollAsync(Guid userId,Guid courseId,CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            INSERT INTO course_enrollments(id,course_id,user_id)
            SELECT @id,c.id,@user FROM courses c WHERE c.id=@course AND c.published
            ON CONFLICT(course_id,user_id) DO NOTHING
            """,conn);
        cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("course",courseId);
        await cmd.ExecuteNonQueryAsync(ct);
        await using var check=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM course_enrollments WHERE course_id=@course AND user_id=@user)",conn);
        check.Parameters.AddWithValue("course",courseId);check.Parameters.AddWithValue("user",userId);
        return (bool)(await check.ExecuteScalarAsync(ct)??false);
    }
    public async Task<List<StageInfo>> StagesAsync(Guid userId,Guid courseId,CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT s.id,s.title_ar,s.position,
            EXISTS(SELECT 1 FROM assessment_attempts a WHERE a.stage_id=s.id AND a.user_id=@user AND a.passed),
            EXISTS(SELECT 1 FROM course_enrollments e WHERE e.course_id=s.course_id AND e.user_id=@user)
            FROM course_stages s WHERE s.course_id=@course ORDER BY s.position
            """,conn);
        cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("course",courseId);
        var list=new List<StageInfo>();await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct)) list.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetInt32(2),reader.GetBoolean(3),reader.GetBoolean(4)));
        return list;
    }
    public async Task<List<LessonInfo>> LessonsAsync(Guid userId,Guid stageId,CancellationToken ct=default) {
        if(!await CanAccessStageAsync(userId,stageId,ct)) return new();
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT l.id,l.title_ar,l.content,l.video_provider,l.video_reference,
            EXISTS(SELECT 1 FROM lesson_completions done WHERE done.lesson_id=l.id AND done.user_id=@user)
            FROM stage_lessons l WHERE l.stage_id=@stage ORDER BY l.position
            """,conn);
        cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("stage",stageId);
        var list=new List<LessonInfo>();await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.GetBoolean(5)));
        return list;
    }
    public async Task<List<QuestionInfo>> QuestionsAsync(Guid userId,Guid courseId,Guid? stageId,CancellationToken ct=default) {
        if(stageId is Guid sid && !await CanAccessStageAsync(userId,sid,ct)) return new();
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT q.id,q.prompt,o.id,o.label
            FROM course_questions q JOIN question_options o ON o.question_id=q.id
            WHERE q.course_id=@course AND q.stage_id IS NOT DISTINCT FROM @stage
            AND q.is_final=@final ORDER BY q.position,o.id
            """,conn);
        cmd.Parameters.AddWithValue("course",courseId);cmd.Parameters.Add("stage",NpgsqlTypes.NpgsqlDbType.Uuid).Value=stageId.HasValue?(object)stageId.Value:DBNull.Value;
        cmd.Parameters.AddWithValue("final",stageId is null);
        var list=new List<QuestionInfo>();await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)){
            var id=r.GetGuid(0);var q=list.FirstOrDefault(x=>x.Id==id);
            if(q is null){q=new QuestionInfo(id,r.GetString(1),new());list.Add(q);}
            q.Options.Add(new(r.GetGuid(2),r.GetString(3)));
        }
        return list;
    }
    public async Task<AssessmentResult> SubmitAsync(Guid userId,Guid courseId,Guid? stageId,Dictionary<Guid,Guid> answers,CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var tx=await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        async Task<long> Scalar(string sql,Action<NpgsqlCommand>? extra=null){
            await using var cmd=new NpgsqlCommand(sql,conn,tx);
            cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("course",courseId);
            cmd.Parameters.Add("stage",NpgsqlTypes.NpgsqlDbType.Uuid).Value=stageId.HasValue?(object)stageId.Value:DBNull.Value;
            extra?.Invoke(cmd);return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)??0);
        }
        if(await Scalar("SELECT count(*) FROM course_enrollments WHERE user_id=@user AND course_id=@course")!=1) return new(false,0,"غير مسجل في الدورة");
        if(stageId is Guid stage) {
            if(await Scalar("SELECT count(*) FROM course_stages WHERE id=@stage AND course_id=@course")!=1) return new(false,0,"مرحلة غير صالحة");
            if(!await CanAccessStageAsync(userId,stage,ct)) return new(false,0,"المرحلة مقفلة");
            if(await Scalar("""
                SELECT count(*) FROM stage_lessons l WHERE l.stage_id=@stage AND NOT EXISTS
                (SELECT 1 FROM lesson_completions d WHERE d.lesson_id=l.id AND d.user_id=@user)
                """)>0) return new(false,0,"أكمل جميع دروس المرحلة أولًا");
        } else {
            if(await Scalar("""
                SELECT count(*) FROM course_stages s WHERE s.course_id=@course AND NOT EXISTS
                (SELECT 1 FROM assessment_attempts a WHERE a.stage_id=s.id AND a.user_id=@user AND a.passed)
                """)>0 || await Scalar("SELECT count(*) FROM course_stages WHERE course_id=@course")==0)
                return new(false,0,"يجب اجتياز جميع المراحل أولًا");
        }
        await using var questions=new NpgsqlCommand("""
            SELECT q.id,o.id,o.is_correct FROM course_questions q
            JOIN question_options o ON o.question_id=q.id
            WHERE q.course_id=@course AND q.stage_id IS NOT DISTINCT FROM @stage AND q.is_final=@final
            """,conn,tx);
        questions.Parameters.AddWithValue("course",courseId);
        questions.Parameters.Add("stage",NpgsqlTypes.NpgsqlDbType.Uuid).Value=stageId.HasValue?(object)stageId.Value:DBNull.Value;
        questions.Parameters.AddWithValue("final",stageId is null);
        var options=new Dictionary<Guid,Dictionary<Guid,bool>>();
        await using(var reader=await questions.ExecuteReaderAsync(ct)){
            while(await reader.ReadAsync(ct)){
                var q=reader.GetGuid(0);if(!options.ContainsKey(q)) options[q]=new();
                options[q][reader.GetGuid(1)]=reader.GetBoolean(2);
            }
        }
        if(options.Count==0 || (stageId is null && options.Count!=20) ||
           answers.Count!=options.Count || answers.Any(x=>!options.TryGetValue(x.Key,out var choices)||!choices.ContainsKey(x.Value)))
            return new(false,0,"الاختبار غير مكتمل أو عدد أسئلته غير صحيح");
        var correct=answers.Count(a=>options[a.Key][a.Value]);
        var score=(int)Math.Round(100.0*correct/options.Count);
        var passThreshold=stageId is null?70:(int)await Scalar("SELECT passing_percent FROM course_stages WHERE id=@stage AND course_id=@course");
        var passed=score>=passThreshold;var attemptId=Guid.NewGuid();
        await using(var insert=new NpgsqlCommand("""
            INSERT INTO assessment_attempts(id,user_id,course_id,stage_id,is_final,score,passed)
            VALUES (@id,@user,@course,@stage,@final,@score,@passed)
            """,conn,tx)){
            insert.Parameters.AddWithValue("id",attemptId);insert.Parameters.AddWithValue("user",userId);
            insert.Parameters.AddWithValue("course",courseId);insert.Parameters.Add("stage",NpgsqlTypes.NpgsqlDbType.Uuid).Value=stageId.HasValue?(object)stageId.Value:DBNull.Value;
            insert.Parameters.AddWithValue("final",stageId is null);insert.Parameters.AddWithValue("score",score);insert.Parameters.AddWithValue("passed",passed);
            await insert.ExecuteNonQueryAsync(ct);
        }
        foreach(var answer in answers){
            await using var cmd=new NpgsqlCommand("INSERT INTO assessment_answers(attempt_id,question_id,option_id) VALUES(@attempt,@q,@o)",conn,tx);
            cmd.Parameters.AddWithValue("attempt",attemptId);cmd.Parameters.AddWithValue("q",answer.Key);cmd.Parameters.AddWithValue("o",answer.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if(stageId is null && passed){
            await using var cert=new NpgsqlCommand("""
                INSERT INTO course_certificates(id,user_id,course_id,final_attempt_id,verification_code)
                VALUES(@id,@user,@course,@attempt,@verify)
                ON CONFLICT(user_id,course_id) DO NOTHING
                """,conn,tx);
            cert.Parameters.AddWithValue("id",Guid.NewGuid());cert.Parameters.AddWithValue("user",userId);
            cert.Parameters.AddWithValue("course",courseId);cert.Parameters.AddWithValue("attempt",attemptId);
            cert.Parameters.AddWithValue("verify",Guid.NewGuid());await cert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);return new(passed,score,passed?"اجتزت الاختبار":"يمكنك المحاولة مجددًا");
    }
    public async Task<CertificateInfo?> VerifyCertificateAsync(Guid code,CancellationToken ct=default){
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT c.verification_code,c.issued_at,u.email,co.title_ar
            FROM course_certificates c JOIN app_users u ON u.id=c.user_id
            JOIN courses co ON co.id=c.course_id WHERE c.verification_code=@code
            """,conn);
        cmd.Parameters.AddWithValue("code",code);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetGuid(0),r.GetDateTime(1),r.GetString(2),r.GetString(3)):null;
    }
    public async Task<CertificateInfo?> CertificateAsync(Guid userId,Guid courseId,CancellationToken ct=default){
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT c.verification_code,c.issued_at,u.email,co.title_ar
            FROM course_certificates c JOIN app_users u ON u.id=c.user_id
            JOIN courses co ON co.id=c.course_id WHERE c.user_id=@user AND c.course_id=@course
            """,conn);
        cmd.Parameters.AddWithValue("user",userId);cmd.Parameters.AddWithValue("course",courseId);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetGuid(0),r.GetDateTime(1),r.GetString(2),r.GetString(3)):null;
    }

}

public record StageInfo(Guid Id,string Title,int Position,bool Passed,bool Enrolled);
public record LessonInfo(Guid Id,string Title,string Content,string? VideoProvider,string? VideoReference,bool Completed);
public record OptionInfo(Guid Id,string Label);
public record QuestionInfo(Guid Id,string Prompt,List<OptionInfo> Options);
public record AssessmentResult(bool Passed,int Score,string Message);
public record CertificateInfo(Guid Code,DateTime IssuedAt,string StudentEmail,string CourseTitle);
