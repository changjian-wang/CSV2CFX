using Flex.Csv2Cfx.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Interfaces
{
    public interface IAuthService
    {
        User? CurrentUser { get; }
        bool Login(string username, string password);
        void Logout();
    }
}
