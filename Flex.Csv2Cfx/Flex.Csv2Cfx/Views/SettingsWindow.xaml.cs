using Flex.Csv2Cfx.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wpf.Ui.Controls;

namespace Flex.Csv2Cfx.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : FluentWindow
    {
        public SettingsWindow(SettingsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();

            // 加载后设置密码框的值
            Loaded += (s, e) =>
            {
                MqttPasswordBox.Password = viewModel.MqttPassword;
                RabbitMqPasswordBox.Password = viewModel.RabbitMqPassword;
            };
        }

        private void MqttPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.MqttPassword = ((Wpf.Ui.Controls.PasswordBox)sender).Password;
            }
        }

        private void RabbitMqPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.RabbitMqPassword = ((Wpf.Ui.Controls.PasswordBox)sender).Password;
            }
        }
    }
}
