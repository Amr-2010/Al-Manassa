using Npgsql;
namespace AlManassa.Web;
public record CourseItem(Guid Id,string TitleAr,string TitleEn,string Description,string Category,int DurationWeeks,bool Published);
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
