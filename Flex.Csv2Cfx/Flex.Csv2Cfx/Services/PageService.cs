using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wpf.Ui.Abstractions;

namespace Flex.Csv2Cfx.Services
{
    public class PageService : IPageService
    {
        public ObservableCollection<NavigationItem> GetNavigationItems(User user) 
        {
            var items = new ObservableCollection<NavigationItem>();
            // 基础页面
            items.Add(new NavigationItem
            {
                Content = "首页",
                PageType = "Home",
                Icon = "Home"
            });

            /* // 根据权限动态添加页面
            if (user.Permissions.Contains("Messages"))
            {
                items.Add(new NavigationItem
                {
                    Content = "消息中心",
                    PageType = "Messages",
                    Icon = "Chat"
                });
            }
            */

            return items;
        }
    }
}
