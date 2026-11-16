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
            """,c)) await cmd.ExecuteNonQueryAsync();
        var email=config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant();
        var password=config["ADMIN_PASSWORD"];
        if(string.IsNullOrWhiteSpace(email)||string.IsNullOrWhiteSpace(password)) return;
        await using var seed=new NpgsqlCommand("""
            INSERT INTO app_users (id,email,password_hash,role) VALUES (@id,@email,@hash,'Admin')
            ON CONFLICT(email) DO NOTHING
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
        return email.Trim().Equals("admin",StringComparison.OrdinalIgnoreCase) && role!="Admin" ? null : (Verify(password,hash)?(id,role):null);
    }
    public async Task<bool> RegisterAsync(string email,string password) {
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("insert into app_users(id,email,password_hash,role) values (@id,@email,@hash,'Student') on conflict(email) do nothing",c);
        cmd.Parameters.AddWithValue("id",Guid.NewGuid());
        cmd.Parameters.AddWithValue("email",email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("hash",Hash(password));
        return await cmd.ExecuteNonQueryAsync()>0;
    }
    public async Task<long> CountStudentsAsync() {
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("select count(*) from app_users where role='Student'",c);
        return (long)(await cmd.ExecuteScalarAsync()??0L);
    }
}
