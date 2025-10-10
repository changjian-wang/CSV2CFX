using Flex.Csv2Cfx.ViewModels;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Flex.Csv2Cfx.Views
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow(MainViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();

            // 设置初始值
            Loaded += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    ProtocolComboBox.SelectedItem = vm.SelectedProtocol;
                }
            };
        }

        private void ProtocolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel && sender is ComboBox comboBox)
            {
                if (e.AddedItems.Count > 0 && e.AddedItems[0] is MessageProtocol newProtocol)
                {
                    var oldProtocol = viewModel.SelectedProtocol;

                    // 尝试设置新值
                    viewModel.SelectedProtocol = newProtocol;

                    // 如果服务正在运行，setter 会阻止更改，我们需要强制恢复 ComboBox 的值
                    if (viewModel.IsServiceRunning && viewModel.SelectedProtocol == oldProtocol)
                    {
                        // 暂时移除事件处理器，避免递归
                        comboBox.SelectionChanged -= ProtocolComboBox_SelectionChanged;
                        comboBox.SelectedItem = oldProtocol;
                        comboBox.SelectionChanged += ProtocolComboBox_SelectionChanged;
                    }
                }
            }
        }
    }
}