using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserContext _userContext;
        private readonly HttpClient _httpClient;

        public AuthService(IUserContext userContext)
        {
            _userContext = userContext;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public User CurrentUser => _userContext.CurrentUser;

        public async Task<bool> LoginAsync(string username, string password, string apiUrl)
        {
            try
            {
                // 准备登录请求数据
                var loginRequest = new
                {
                    username = username,
                    password = password
                };

                var jsonContent = JsonSerializer.Serialize(loginRequest);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 调用登录 API
                var response = await _httpClient.PostAsync(apiUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // 解析 API 响应
                    var user = JsonSerializer.Deserialize<User>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (user != null && user.Status)
                    {
                        _userContext.CurrentUser = user;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

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