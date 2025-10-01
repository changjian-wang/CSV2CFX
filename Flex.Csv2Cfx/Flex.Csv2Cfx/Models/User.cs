using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Models
{
    public class User
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public TokenProvider? Data { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    public class TokenProvider
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("userName")]
        public string? UserName { get; set; }

        [JsonPropertyName("img")]
        public string? Img { get; set; }

        [JsonPropertyName("roles")]
        public List<Role> Roles { get; set; } = new List<Role>();
    }

    public class Role
    {
        [JsonPropertyName("roleID")]
        public int RoleID { get; set; }

        [JsonPropertyName("roleName")]
        public string? RoleName { get; set; }
    }
}
