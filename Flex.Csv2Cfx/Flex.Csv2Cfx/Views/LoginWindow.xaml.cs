using Flex.Csv2Cfx.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;

namespace Flex.Csv2Cfx.Views
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : FluentWindow
    {
        public LoginWindow(LoginViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel viewModel)
            {
                viewModel.Password = ((PasswordBox)sender).Password;
            }
        }
    }
}
