using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using System;
using System.Windows;
using System.Windows.Input;

namespace Flex.Csv2Cfx.ViewModels
{
    public class LoginViewModel : ObservableObject
    {
        private readonly IAuthService _authService;
        private readonly IConfigurationService _configurationService;

        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string LoginApiUrl { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public ICommand LoginCommand { get; }

        public LoginViewModel(IAuthService authService, IConfigurationService configurationService)
        {
            _authService = authService;
            _configurationService = configurationService;
            LoginCommand = new RelayCommand(ExecuteLogin, CanExecuteLogin);

            // 从配置文件加载保存的 API URL
            var settings = _configurationService.GetSettings();
            LoginApiUrl = settings.LoginApiUrl;
        }

        private bool CanExecuteLogin(object? parameter)
        {
            return !string.IsNullOrWhiteSpace(Username) &&
                   !string.IsNullOrWhiteSpace(Password) &&
                   !string.IsNullOrWhiteSpace(LoginApiUrl);
        }

        private async void ExecuteLogin(object? parameter)
        {
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));

            try
            {
                // 保存 API URL 到配置文件（使用 preserveLoginApiUrl = false 来更新）
                var settings = _configurationService.GetSettings();
                if (settings.LoginApiUrl != LoginApiUrl)
                {
                    settings.LoginApiUrl = LoginApiUrl;
                    await _configurationService.SaveSettingsAsync(settings, preserveLoginApiUrl: false);
                }

                // 调用 API 登录
                var result = await _authService.LoginAsync(Username, Password, LoginApiUrl);

                if (result)
                {
                    if (parameter is Window window)
                    {
                        window.DialogResult = true;
                        window.Close();
                    }
                }
                else
                {
                    ErrorMessage = "登录失败，请检查用户名、密码或API地址";
                    OnPropertyChanged(nameof(ErrorMessage));
                    OnPropertyChanged(nameof(HasError));
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"登录错误: {ex.Message}";
                OnPropertyChanged(nameof(ErrorMessage));
                OnPropertyChanged(nameof(HasError));
            }
        }
    }
}