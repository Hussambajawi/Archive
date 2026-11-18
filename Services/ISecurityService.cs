using System.Collections.Generic;
using System.Threading.Tasks;
using DocuArchivePro.Models;

namespace DocuArchivePro.Services
{
    public interface ISecurityService
    {
        AppUser CurrentUser { get; set; }
        Task<List<AppUser>> GetAllUsersAsync();
        Task<bool> IsUsernameTakenAsync(string username, int excludeUserId = 0);
        Task<AppUser> SaveUserAsync(AppUser user, bool clearPassword = false);
        Task<bool> DeleteUserAsync(int userId);
        Task<SystemSettings> GetSettingsAsync();
        Task<SystemSettings> SaveSettingsAsync(SystemSettings settings);
        Task<bool> VerifyActionCodeAsync(string enteredCode);
        Task<bool> VerifyEntityCodeAsync(string enteredCode);
        bool VerifyUserPassword(AppUser user, string enteredPassword);
    }
}
