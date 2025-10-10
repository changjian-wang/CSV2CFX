using Flex.Csv2Cfx.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;

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

        private void MqttPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.MqttPassword = ((PasswordBox)sender).Password;
            }
        }

        private void RabbitMqPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.RabbitMqPassword = ((PasswordBox)sender).Password;
            }
        }
    }
}