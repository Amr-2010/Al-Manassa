using Npgsql;
using System.Security.Cryptography;
namespace AlManassa.Web;
public sealed class AuthStore(IConfiguration config) {
    private string ConnectionString {
        get {
            var raw=config["DATABASE_URL"] ?? throw new InvalidOperationException("DATABASE_URL missing");
            if(!raw.StartsWith("postgres",StringComparison.OrdinalIgnoreCase)) return raw;
            var u=new Uri(raw); var parts=u.UserInfo.Split(':',2);
            return new NpgsqlConnectionStringBuilder {Host=u.Host,Port=u.IsDefaultPort?5432:u.Port,Database=Uri.UnescapeDataString(u.AbsolutePath.Trim('/')),Username=Uri.UnescapeDataString(parts[0]),Password=parts.Length>1?Uri.UnescapeDataString(parts[1]):"",SslMode=SslMode.Require}.ConnectionString;
        }
    }
    public async Task EnsureAsync() {
        await using var c=new NpgsqlConnection(ConnectionString); await c.OpenAsync();
        await using(var cmd=new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS app_users (
              id uuid PRIMARY KEY, email varchar(320) NOT NULL UNIQUE,
              password_hash text NOT NULL, role varchar(20) NOT NULL DEFAULT 'Student',
              created_at timestamptz NOT NULL DEFAULT now()
            );
            ALTER TABLE app_users ADD COLUMN IF NOT EXISTS last_seen_at timestamptz;
            """,c)) await cmd.ExecuteNonQueryAsync();
        var email=config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant();
        var password=config["ADMIN_PASSWORD"];
        if(string.IsNullOrWhiteSpace(email)||string.IsNullOrWhiteSpace(password)) return;
        await using var seed=new NpgsqlCommand("""
            INSERT INTO app_users (id,email,password_hash,role) VALUES (@id,@email,@hash,'SuperAdmin')
            ON CONFLICT(email) DO UPDATE SET role='SuperAdmin'
            """,c);
        seed.Parameters.AddWithValue("id",Guid.NewGuid());
        seed.Parameters.AddWithValue("email",email);
        seed.Parameters.AddWithValue("hash",Hash(password));
        await seed.ExecuteNonQueryAsync();
    }
    public static string Hash(string password) {
        var salt=RandomNumberGenerator.GetBytes(16);
        var derived=Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32);
        return $"pbkdf2-sha256:210000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(derived)}";
    }
    public static bool Verify(string password,string encoded) {
        var parts=encoded.Split(':');
        if(parts.Length!=4||parts[0]!="pbkdf2-sha256"||!int.TryParse(parts[1],out var rounds)||rounds<100000||rounds>1000000) return false;
        try {
            var salt=Convert.FromBase64String(parts[2]);var expected=Convert.FromBase64String(parts[3]);
            var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,rounds,HashAlgorithmName.SHA256,expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual,expected);
        } catch(FormatException) {return false;}
    }
    public async Task<(Guid Id,string Role)?> ValidateAsync(string email,string password) {
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("select id,password_hash,role from app_users where email=@email",c);
        var login=email.Trim().ToLowerInvariant();
        if(login=="admin") {
            var adminEmail=config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant();
            if(string.IsNullOrWhiteSpace(adminEmail)) return null;
            login=adminEmail;
        }
        cmd.Parameters.AddWithValue("email",login);
        await using var r=await cmd.ExecuteReaderAsync();
        if(!await r.ReadAsync()) return null;
        var id=r.GetGuid(0);var hash=r.GetString(1);var role=r.GetString(2);
        return email.Trim().Equals("admin",StringComparison.OrdinalIgnoreCase) && role!="SuperAdmin" ? null : (Verify(password,hash)?(id,role):null);
    }
    public async Task<bool> RegisterAsync(string email,string password) {
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("insert into app_users(id,email,password_hash,role) values (@id,@email,@hash,'Student') on conflict(email) do nothing",c);
        cmd.Parameters.AddWithValue("id",Guid.NewGuid());
        cmd.Parameters.AddWithValue("email",email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("hash",Hash(password));
        return await cmd.ExecuteNonQueryAsync()>0;
    }
    public sealed record ManagedUser(Guid Id,string Email,string Role,DateTime CreatedAt,DateTime? LastSeenAt);
    public async Task<List<ManagedUser>> ListManagedAsync(){
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("select id,email,role,created_at,last_seen_at from app_users where role <> 'SuperAdmin' and email <> @owner order by created_at desc",c);
        cmd.Parameters.AddWithValue("owner",config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant()??"");
        var list=new List<ManagedUser>();await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync())list.Add(new ManagedUser(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetDateTime(3),r.IsDBNull(4)?null:r.GetDateTime(4)));
        return list;
    }
    public async Task CreateManagedAsync(string email,string password,string role){
        email=email.Trim().ToLowerInvariant();
        if(email.Length is <5 or >320 || !email.Contains('@') || password.Length is <12 or >128 || role is not ("Admin" or "Student"))throw new ArgumentException("بيانات المستخدم غير صالحة أو كلمة المرور أقل من 12 حرفًا");
        if(email==(config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant()))throw new InvalidOperationException("لا يمكن إنشاء حساب بنفس بريد المدير الرئيسي");
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("insert into app_users(id,email,password_hash,role) values (@id,@email,@hash,@role)",c);
        cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("email",email);cmd.Parameters.AddWithValue("hash",Hash(password));cmd.Parameters.AddWithValue("role",role);
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task ChangeManagedRoleAsync(Guid id,string role){
        if(role is not ("Admin" or "Student"))throw new ArgumentException("الدور غير مسموح");
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("update app_users set role=@role where id=@id and role <> 'SuperAdmin' and email <> @owner",c);
        cmd.Parameters.AddWithValue("role",role);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("owner",config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant()??"");
        if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("المستخدم غير موجود أو محمي");
    }
    public async Task DeleteManagedAsync(Guid id){
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("delete from app_users where id=@id and role <> 'SuperAdmin' and email <> @owner",c);
        cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("owner",config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant()??"");
        if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("تعذر حذف المستخدم؛ قد يكون مرتبطًا ببيانات تعليمية");
    }
    public async Task<long> CountStudentsAsync() {
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("select count(*) from app_users where role='Student'",c);
        return (long)(await cmd.ExecuteScalarAsync()??0L);
    }
    public async Task<(long Sections,long Courses,long Lessons,long Students,long CertifiedStudents,long OnlineStudents)> DashboardCountsAsync() {
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT
             (SELECT count(*) FROM subjects WHERE active=true),
             (SELECT count(*) FROM courses),
             (SELECT count(*) FROM stage_lessons),
             (SELECT count(*) FROM app_users WHERE role='Student'),
             (SELECT count(DISTINCT user_id) FROM course_certificates),
             (SELECT count(*) FROM app_users WHERE role='Student' AND last_seen_at >= now()-interval '5 minutes')
            """,c);
        await using var r=await cmd.ExecuteReaderAsync();await r.ReadAsync();
        return (r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetInt64(3),r.GetInt64(4),r.GetInt64(5));
    }
    public async Task RecordActivityAsync(Guid userId){
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("""
            UPDATE app_users SET last_seen_at=now()
            WHERE id=@id AND (last_seen_at IS NULL OR last_seen_at < now()-interval '60 seconds')
            """,c);
        cmd.Parameters.AddWithValue("id",userId);await cmd.ExecuteNonQueryAsync();
    }

}
