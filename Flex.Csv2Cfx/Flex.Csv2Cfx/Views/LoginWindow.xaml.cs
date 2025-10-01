using Flex.Csv2Cfx.ViewModels;
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
            this.DataContext = viewModel;
            InitializeComponent();
        }
    }
}
