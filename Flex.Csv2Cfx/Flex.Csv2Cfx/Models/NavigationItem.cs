using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Models
{
    public class NavigationItem
    {
        public string Content { get; set; } = string.Empty;
        public string PageType { get; set; } = string.Empty;
        public string Icon { get; set; } = "Home";
        public bool IsVisible { get; set; } = true;
    }
}
