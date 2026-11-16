using Npgsql;
namespace AlManassa.Web;
public record CourseItem(Guid Id,string TitleAr,string TitleEn,string Description,string Category,int Weeks,bool Published);
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
}
