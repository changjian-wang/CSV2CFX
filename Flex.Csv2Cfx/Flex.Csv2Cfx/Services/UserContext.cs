using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Services
{
    public class UserContext : IUserContext
    {
        public User CurrentUser { get; set; } = new User();
        public bool IsAuthenticated => !string.IsNullOrEmpty(CurrentUser.Data?.UserName);
    }
}
