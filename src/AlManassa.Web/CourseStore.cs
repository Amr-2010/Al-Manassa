using Npgsql;
namespace AlManassa.Web;
public record CourseItem(Guid Id,string TitleAr,string TitleEn,string Description,string Category,int DurationWeeks,bool Published);
public record SubjectItem(string Name,string Key,string Icon);
public record LearningPathItem(string Code,string Title,string Description,string Url);
public sealed class CourseStore(IConfiguration config)
{
    private string ConnectionString {
        get {
            var raw=config["DATABASE_URL"] ?? config.GetConnectionString("Default");
            if(string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("DATABASE_URL is not configured");
            if(!raw.StartsWith("postgres://",StringComparison.OrdinalIgnoreCase) && !raw.StartsWith("postgresql://",StringComparison.OrdinalIgnoreCase)) return raw;
            var u=new Uri(raw);
            var parts=u.UserInfo.Split(':',2);
            return new NpgsqlConnectionStringBuilder {Host=u.Host,Port=u.IsDefaultPort?5432:u.Port,Database=Uri.UnescapeDataString(u.AbsolutePath.Trim('/')),Username=Uri.UnescapeDataString(parts[0]),Password=parts.Length>1?Uri.UnescapeDataString(parts[1]):"",SslMode=SslMode.Require}.ConnectionString;
        }
    }
    public async Task<List<CourseItem>> PublishedAsync(CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("select id,title_ar,title_en,coalesce(description,''),coalesce(category,'General'),coalesce(duration_weeks,4),published from courses where published=true order by created_at desc limit 60",conn);
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        var result=new List<CourseItem>();
        while(await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt32(5),reader.GetBoolean(6)));
        return result;
    }
    public async Task<(long Total,long Published)> CountsAsync(CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("select count(*),count(*) filter (where published) from courses",conn);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetInt64(0),r.GetInt64(1));
    }
    public async Task SaveSuggestionAsync(Guid id,string topic,string level,string language,int weeks,string json,string provider,CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("insert into curriculum_suggestions(id,topic,level,language,duration_weeks,content_json,provider,status) values (@id,@topic,@level,@lang,@weeks,cast(@json as jsonb),@provider,'PendingReview')",conn);
        cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("topic",topic);cmd.Parameters.AddWithValue("level",level);cmd.Parameters.AddWithValue("lang",language);cmd.Parameters.AddWithValue("weeks",weeks);cmd.Parameters.AddWithValue("json",json);cmd.Parameters.AddWithValue("provider",provider);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<long> SuggestionCountAsync(CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("select count(*) from curriculum_suggestions",conn);
        return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }

    public async Task<List<SubjectItem>> SubjectsAsync(CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("select name_ar,slug,icon from subjects where active=true order by sort_order,name_ar",conn);
        await using var r=await cmd.ExecuteReaderAsync(ct);var list=new List<SubjectItem>();
        while(await r.ReadAsync(ct)) list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2)));
        return list;
    }
    public async Task<List<LearningPathItem>> LearningPathsAsync(CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("select code,title_ar,description_ar,link_url from learning_paths where active=true order by sort_order",conn);
        await using var r=await cmd.ExecuteReaderAsync(ct);var list=new List<LearningPathItem>();
        while(await r.ReadAsync(ct)) list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));
        return list;
    }
    public async Task EnsureDemoSchemaAsync(CancellationToken ct=default) {
        await using var conn=new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx=await conn.BeginTransactionAsync(ct);
        const string ddl = """
            CREATE TABLE IF NOT EXISTS courses (
              id uuid PRIMARY KEY,
              title_ar varchar(250) NOT NULL,
              title_en varchar(250) NOT NULL,
              description text,
              category varchar(100) NOT NULL DEFAULT 'General',
              duration_weeks integer NOT NULL DEFAULT 4 CHECK(duration_weeks BETWEEN 1 AND 52),
              published boolean NOT NULL DEFAULT false,
              created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS curriculum_suggestions (
              id uuid PRIMARY KEY,
              topic varchar(250) NOT NULL,
              level varchar(40) NOT NULL,
              language varchar(20) NOT NULL,
              duration_weeks integer NOT NULL,
              content_json jsonb NOT NULL,
              provider varchar(80) NOT NULL,
              status varchar(30) NOT NULL DEFAULT 'PendingReview',
              created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS subjects (
              slug varchar(100) PRIMARY KEY,
              name_ar varchar(200) NOT NULL,
              name_en varchar(200) NOT NULL,
              icon varchar(20) NOT NULL DEFAULT '◆',
              sort_order integer NOT NULL DEFAULT 0,
              active boolean NOT NULL DEFAULT true
            );
            CREATE TABLE IF NOT EXISTS learning_paths (
              code varchar(60) PRIMARY KEY,
              title_ar varchar(200) NOT NULL,
              title_en varchar(200) NOT NULL,
              description_ar text NOT NULL DEFAULT '',
              description_en text NOT NULL DEFAULT '',
              link_url varchar(500) NOT NULL DEFAULT '#courses',
              sort_order integer NOT NULL DEFAULT 0,
              active boolean NOT NULL DEFAULT true
            );
            CREATE TABLE IF NOT EXISTS course_stages (
              id uuid PRIMARY KEY, course_id uuid NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
              title_ar varchar(250) NOT NULL, position integer NOT NULL CHECK(position>0),
              passing_percent integer NOT NULL DEFAULT 70 CHECK(passing_percent BETWEEN 1 AND 100),
              UNIQUE(course_id,position)
            );
            CREATE TABLE IF NOT EXISTS stage_lessons (
              id uuid PRIMARY KEY, stage_id uuid NOT NULL REFERENCES course_stages(id) ON DELETE CASCADE,
              title_ar varchar(250) NOT NULL, content text NOT NULL DEFAULT '',
              video_provider varchar(20), video_reference text, position integer NOT NULL,
              UNIQUE(stage_id,position),
              CHECK(video_provider IS NULL OR video_provider IN ('youtube','hosted'))
            );
            CREATE TABLE IF NOT EXISTS course_questions (
              id uuid PRIMARY KEY, course_id uuid NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
              stage_id uuid REFERENCES course_stages(id) ON DELETE CASCADE,
              prompt text NOT NULL, position integer NOT NULL,
              is_final boolean NOT NULL DEFAULT false,
              UNIQUE(course_id,stage_id,position)
            );
            CREATE TABLE IF NOT EXISTS question_options (
              id uuid PRIMARY KEY, question_id uuid NOT NULL REFERENCES course_questions(id) ON DELETE CASCADE,
              label text NOT NULL, is_correct boolean NOT NULL DEFAULT false
            );
            CREATE TABLE IF NOT EXISTS course_enrollments (
              id uuid PRIMARY KEY, course_id uuid NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
              user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
              enrolled_at timestamptz NOT NULL DEFAULT now(), UNIQUE(course_id,user_id)
            );
            CREATE TABLE IF NOT EXISTS lesson_completions (
              user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
              lesson_id uuid NOT NULL REFERENCES stage_lessons(id) ON DELETE CASCADE,
              completed_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(user_id,lesson_id)
            );
            CREATE TABLE IF NOT EXISTS assessment_attempts (
              id uuid PRIMARY KEY, user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
              course_id uuid NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
              stage_id uuid REFERENCES course_stages(id) ON DELETE CASCADE,
              is_final boolean NOT NULL, score integer NOT NULL CHECK(score BETWEEN 0 AND 100),
              passed boolean NOT NULL, submitted_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS assessment_answers (
              attempt_id uuid NOT NULL REFERENCES assessment_attempts(id) ON DELETE CASCADE,
              question_id uuid NOT NULL REFERENCES course_questions(id) ON DELETE CASCADE,
              option_id uuid NOT NULL REFERENCES question_options(id),
              PRIMARY KEY(attempt_id,question_id)
            );
            CREATE TABLE IF NOT EXISTS course_certificates (
              id uuid PRIMARY KEY, user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
              course_id uuid NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
              final_attempt_id uuid NOT NULL REFERENCES assessment_attempts(id),
              issued_at timestamptz NOT NULL DEFAULT now(),
              verification_code uuid NOT NULL UNIQUE,
              UNIQUE(user_id,course_id)
            );
            INSERT INTO subjects(slug,name_ar,name_en,icon,sort_order) VALUES
            ('Programming','البرمجة وتطوير التطبيقات','Programming','⌘',1),
            ('Technology','الذكاء الاصطناعي','Artificial Intelligence','✦',2),
            ('Business','إدارة الأعمال','Business','◈',3),
            ('Cyber Security','الأمن السيبراني','Cyber Security','◇',4),
            ('Design','التصميم والإبداع','Design','✳',5),
            ('Marketing','التسويق الرقمي','Marketing','◎',6),
            ('Languages','اللغات','Languages','文',7),
            ('Science','العلوم والهندسة','Science','⚙',8),
            ('Teaching','التعليم والتدريب','Teaching','✎',9),
            ('Health','الصحة والطب','Health','✚',10),
            ('Data','تحليل البيانات','Data Analytics','▥',11),
            ('Skills','المهارات الشخصية','Personal Skills','✧',12)
            ON CONFLICT(slug) DO NOTHING;
            INSERT INTO learning_paths(code,title_ar,title_en,description_ar,description_en,link_url,sort_order) VALUES
            ('SHORT COURSES','الدورات القصيرة','Short Courses','مهارات محددة بخطة تعليمية مرنة.','Flexible short courses','#courses',1),
            ('EXPERT TRACKS','المسارات الاحترافية','Expert Tracks','سلسلة دورات مترابطة لتطوير خبرتك خطوة بخطوة.','Professional learning paths','#courses',2),
            ('CERTIFICATES','الشهادات التعليمية','Certificates','تقييمات وشهادات بعد تفعيل النظام.','Certificates after activation','/register',3),
            ('AI LEARNING','مناهج مقترحة بالذكاء الاصطناعي','AI Learning','مقترحات يراجعها فريق الإدارة قبل نشرها.','AI suggestions reviewed before publishing','/register',4)
            ON CONFLICT(code) DO NOTHING;
            INSERT INTO courses(id,title_ar,title_en,description,category,duration_weeks,published) VALUES
            ('11111111-1111-4111-8111-111111111111','أساسيات الذكاء الاصطناعي','AI Fundamentals','تعرف على مفاهيم وتطبيقات الذكاء الاصطناعي.','Technology',8,true),
            ('22222222-2222-4222-8222-222222222222','تطوير تطبيقات .NET','Developing with .NET','تعلم تطوير تطبيقات حديثة باستخدام .NET.','Programming',6,true),
            ('33333333-3333-4333-8333-333333333333','التسويق الرقمي','Digital Marketing','أساسيات التسويق عبر القنوات الرقمية.','Business',4,true)
            ON CONFLICT (id) DO NOTHING;
            """;
        await using var cmd=new NpgsqlCommand(ddl,conn,tx);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
}
