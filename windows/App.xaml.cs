using Microsoft.Extensions.DependencyInjection;
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
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.Services;
using bilibili_music_player_windows.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace bilibili_music_player_windows
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private readonly ServiceProvider _serviceProvider;
        private Window? _window;

        public static new App Current => (App)Application.Current;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            _serviceProvider = ConfigureServices();
        }

        public T GetService<T>() where T : notnull => _serviceProvider.GetRequiredService<T>();

        private static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // HttpClient — shared across all services
            services.AddSingleton<HttpClient>(sp =>
            {
                var handler = new HttpClientHandler();
                var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(30),
                };

                BilibiliClient.ConfigureDefaultHeaders(client);
                return client;
            });

            // API
            services.AddSingleton<BilibiliClient>();

            // Services
            services.AddSingleton<CacheService>();
            services.AddSingleton<AudioPlayerService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<PlayerViewModel>();
            services.AddTransient<SearchViewModel>();
            services.AddSingleton<LibraryViewModel>();
            services.AddSingleton<SettingsViewModel>();

            // Window
            services.AddSingleton<MainWindow>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = _serviceProvider.GetRequiredService<MainWindow>();
            _window.Activate();
        }
    }
}
