using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FontAutoLoader
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // 在进程启动最早期将 WebView2 缓存数据目录重定向至 LocalAppData，彻底杜绝权限不足弹窗
            string webViewDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FontAutoLoader", "WebView2Data");
            if (!System.IO.Directory.Exists(webViewDataDir))
            {
                System.IO.Directory.CreateDirectory(webViewDataDir);
            }
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDataDir);

            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();

            string[] cmdArgs = Environment.GetCommandLineArgs();
            bool isSilent = System.Linq.Enumerable.Any(cmdArgs, a => 
                string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase));

            // 如果满足开机自启且不显示主窗口，则直接在托盘静默运行
            if (isSilent && Services.SettingsService.Current.AutoStart && Services.SettingsService.Current.SilentStart)
            {
                // 主窗口已经在 MainWindow 构造函数中初始化了托盘，不调用 Activate() 即可保持静默
            }
            else
            {
                _window.Activate();
            }
        }
    }
}
