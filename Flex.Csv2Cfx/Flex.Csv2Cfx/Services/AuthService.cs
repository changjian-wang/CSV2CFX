using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserContext _userContext;

        public AuthService(IUserContext userContext)
        {
            _userContext = userContext;
        }

        public User CurrentUser => _userContext.CurrentUser;

        public bool Login(string username, string password)
        {
            if (username == "admin" && password == "admin123")
            {
                _userContext.CurrentUser = new User
                {
                    Status = true,
                    Code = "200",
                    Message = "Login successful",
                    Success = true,
                    Data = new TokenProvider
                    {
                        Token = Guid.NewGuid().ToString(),
                        UserName = "admin",
                        Img = "https://example.com/admin.png",
                        Roles = new List<Role>
                        {
                            new Role { RoleID = 1, RoleName = "Administrator" }
                        }
                    }
                };

                return true;
            }

            return false;
        }

        public void Logout()
        {
            _userContext.CurrentUser = new User();
        }
    }
}
