using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface IUserDbService
{
    bool IsDbAvailable { get; }
    string DbPath { get; }
    
    Task<List<UserInfo>> GetAllUsersAsync();
    Task<UserInfo?> GetUserByIdAsync(int id);
    Task<bool> AddUserAsync(string username, string? nickname, string password, bool isAdmin);
    Task<bool> UpdateUserAsync(int id, string? nickname, bool isAdmin);
    Task<bool> ResetPasswordAsync(int id, string newPassword);
    Task<bool> DeleteUserAsync(int id);
    Task<bool> UsernameExistsAsync(string username);
}
