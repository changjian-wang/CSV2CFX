using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Flex.Csv2Cfx.Converters
{
    /// <summary>
    /// 将连接状态文本转换为对应的颜色画刷
    /// </summary>
    public class ConnectionStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                // 已连接 - 绿色
                if (status.Contains("已连接"))
                {
                    return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // #4CAF50
                }
                // 连接中/重新连接中 - 橙色
                else if (status.Contains("连接中") || status.Contains("重新连接中"))
                {
                    return new SolidColorBrush(Color.FromRgb(255, 152, 0)); // #FF9800
                }
                // 失败/错误 - 红色
                else if (status.Contains("失败") || status.Contains("错误"))
                {
                    return new SolidColorBrush(Color.FromRgb(244, 67, 54)); // #F44336
                }
                // 未连接/已停止 - 灰色
                else if (status.Contains("未连接") || status.Contains("已停止"))
                {
                    return new SolidColorBrush(Color.FromRgb(158, 158, 158)); // #9E9E9E
                }
            }

            // 默认灰色
            return new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 将服务运行状态转换为对应的颜色画刷
    /// </summary>
    public class ServiceStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRunning)
            {
                return isRunning
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // #4CAF50 - 绿色
                    : new SolidColorBrush(Color.FromRgb(158, 158, 158)); // #9E9E9E - 灰色
            }

            return new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}