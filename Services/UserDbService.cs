using System.Security.Cryptography;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;
using Microsoft.Data.Sqlite;

namespace iFlyCompassGUI.Services;

public class UserDbService : IUserDbService
{
    private readonly IInstallService _installService;

    public bool IsDbAvailable => File.Exists(DbPath);

    public string DbPath
    {
        get
        {
            var baseDir = PathHelper.DataDirectory;
            var iFlyCompassDir = Path.Combine(baseDir, "iFlyCompass");
            return Path.Combine(iFlyCompassDir, "instance", "users.db");
        }
    }
    
    public UserDbService(IInstallService installService)
    {
        _installService = installService;
    }
    
    private SqliteConnection CreateConnection()
    {
        var connectionString = $"Data Source={DbPath};Mode=ReadWrite";
        return new SqliteConnection(connectionString);
    }
    
    public async Task<List<UserInfo>> GetAllUsersAsync()
    {
        var users = new List<UserInfo>();
        if (!IsDbAvailable) return users;
        
        using var connection = CreateConnection();
        await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, username, nickname, is_super_admin, is_admin, created_at 
            FROM user 
            ORDER BY id";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(MapUserInfo(reader));
        }
        
        return users;
    }
    
    public async Task<UserInfo?> GetUserByIdAsync(int id)
    {
        if (!IsDbAvailable) return null;
        
        using var connection = CreateConnection();
        await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, username, nickname, is_super_admin, is_admin, created_at 
            FROM user 
            WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapUserInfo(reader);
        }
        
        return null;
    }
    
    public async Task<bool> AddUserAsync(string username, string? nickname, string password, bool isAdmin)
    {
        if (!IsDbAvailable) return false;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;
        
        var passwordHash = HashPassword(password);
        
        using var connection = CreateConnection();
        await connection.OpenAsync();
        
        using var transaction = connection.BeginTransaction();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = @"
                INSERT INTO user (username, nickname, password_hash, is_super_admin, is_admin, session_version, line_height, letter_spacing, created_at)
                VALUES (@username, @nickname, @password_hash, 0, @is_admin, 0, 1.6, 0.0, @created_at)";
            command.Parameters.AddWithValue("@username", username.Trim());
            command.Parameters.AddWithValue("@nickname", string.IsNullOrWhiteSpace(nickname) ? DBNull.Value : nickname.Trim());
            command.Parameters.AddWithValue("@password_hash", passwordHash);
            command.Parameters.AddWithValue("@is_admin", isAdmin ? 1 : 0);
            command.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            
            await command.ExecuteNonQueryAsync();
            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            return false;
        }
    }
    
    public async Task<bool> UpdateUserAsync(int id, string? nickname, bool isAdmin)
    {
        if (!IsDbAvailable) return false;
        
        using var connection = CreateConnection();
        await connection.OpenAsync();
        
        using var transaction = connection.BeginTransaction();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = @"
                UPDATE user 
                SET nickname = @nickname, is_admin = @is_admin
                WHERE id = @id AND is_super_admin = 0";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@nickname", string.IsNullOrWhiteSpace(nickname) ? DBNull.Value : nickname.Trim());
            command.Parameters.AddWithValue("@is_admin", isAdmin ? 1 : 0);
            
            var rowsAffected = await command.ExecuteNonQueryAsync();
            transaction.Commit();
            return rowsAffected > 0;
        }
        catch
        {
            transaction.Rollback();
            return false;
        }
    }
    
    public async Task<bool> ResetPasswordAsync(int id, string newPassword)
    {
        if (!IsDbAvailable) return false;
        if (string.IsNullOrWhiteSpace(newPassword)) return false;
        
        var passwordHash = HashPassword(newPassword);
        
        using var connection = CreateConnection();
        await connection.OpenAsync();
        
        using var transaction = connection.BeginTransaction();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = @"
                UPDATE user 
                SET password_hash = @password_hash, session_version = COALESCE(session_version, 0) + 1
                WHERE id = @id";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@password_hash", passwordHash);
            
            var rowsAffected = await command.ExecuteNonQueryAsync();
            transaction.Commit();
            return rowsAffected > 0;
        }
        catch
        {
            transaction.Rollback();
            return false;
        }
    }
    
    public async Task<bool> DeleteUserAsync(int id)
    {
        if (!IsDbAvailable) return false;
        
        using var connection = CreateConnection();
        await connection.OpenAsync();
        
        using var transaction = connection.BeginTransaction();
        try
        {
            var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = (SqliteTransaction)transaction;
            checkCmd.CommandText = "SELECT is_super_admin FROM user WHERE id = @id";
            checkCmd.Parameters.AddWithValue("@id", id);
            var isSuperAdmin = await checkCmd.ExecuteScalarAsync();
            if (isSuperAdmin != null && Convert.ToInt32(isSuperAdmin) == 1)
            {
                transaction.Rollback();
                return false;
            }
            
            var cleanupSqls = new[]
            {
                "DELETE FROM ai_message WHERE conversation_id IN (SELECT id FROM ai_conversation WHERE user_id = @uid)",
                "DELETE FROM ai_conversation WHERE user_id = @uid",
                "DELETE FROM drop_blacklist WHERE user_id = @uid OR blocked_user_id = @uid",
                "DELETE FROM drop_settings WHERE user_id = @uid",
                "DELETE FROM drop_message WHERE sender_id = @uid",
                "DELETE FROM drop_read WHERE user_id = @uid",
                "DELETE FROM user_sticker WHERE user_id = @uid",
                "DELETE FROM pack_sticker WHERE user_id = @uid",
                "DELETE FROM novel_reading_progress WHERE user_id = @uid",
                "DELETE FROM bili_video_user WHERE user_id = @uid",
                "DELETE FROM user_announcement_status WHERE user_id = @uid",
                "DELETE FROM video_access_user WHERE user_id = @uid",
                "UPDATE video_access_control SET created_by = NULL WHERE created_by = @uid",
                "UPDATE video_folder_access SET created_by = NULL WHERE created_by = @uid",
                "DELETE FROM user_game_stats WHERE user_id = @uid",
                "DELETE FROM chat_room WHERE created_by = @uid",
                "DELETE FROM user_announcement_status WHERE announcement_id IN (SELECT id FROM announcement WHERE created_by = @uid)",
                "DELETE FROM announcement WHERE created_by = @uid",
                "DELETE FROM user WHERE id = @uid"
            };
            
            foreach (var sql in cleanupSqls)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = (SqliteTransaction)transaction;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@uid", id);
                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                }
            }
            
            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            return false;
        }
    }
    
    public async Task<bool> UsernameExistsAsync(string username)
    {
        if (!IsDbAvailable) return false;
        
        using var connection = CreateConnection();
        await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM user WHERE username = @username";
        command.Parameters.AddWithValue("@username", username.Trim());
        
        var count = await command.ExecuteScalarAsync();
        return count != null && Convert.ToInt64(count) > 0;
    }
    
    private static UserInfo MapUserInfo(SqliteDataReader reader)
    {
        var isSuperAdmin = reader.GetInt32(3) == 1;
        var isAdmin = reader.GetInt32(4) == 1;
        
        string role;
        if (isSuperAdmin) role = "超级管理员";
        else if (isAdmin) role = "管理员";
        else role = "普通用户";
        
        return new UserInfo
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            Nickname = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Role = role,
            IsSuperAdmin = isSuperAdmin,
            IsAdmin = isAdmin,
            CreatedAt = reader.IsDBNull(5) ? DateTime.MinValue : DateTime.Parse(reader.GetString(5))
        };
    }
    
    private static string HashPassword(string password)
    {
        const int iterations = 260000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2:sha256:{iterations}${Convert.ToHexString(salt).ToLower()}${Convert.ToHexString(hash).ToLower()}";
    }
}
