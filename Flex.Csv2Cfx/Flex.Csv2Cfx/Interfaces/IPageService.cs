using Flex.Csv2Cfx.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Interfaces
{
    public interface IPageService
    {
        ObservableCollection<NavigationItem> GetNavigationItems(User user);
    }
}
