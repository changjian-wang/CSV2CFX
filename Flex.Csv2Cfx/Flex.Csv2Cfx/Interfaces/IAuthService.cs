using Flex.Csv2Cfx.Models;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Interfaces
{
    public interface IAuthService
    {
        User? CurrentUser { get; }
        Task<bool> LoginAsync(string username, string password, string apiUrl);
        bool Login(string username, string password);
        void Logout();
    }
}