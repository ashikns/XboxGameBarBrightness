using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace XboxGameBarBrightness.UI
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public ObservableCollection<Monitor> Monitors { get; } = new ObservableCollection<Monitor>();

        private AppServiceConnection _connection;

        public MainPage()
        {
            this.InitializeComponent();

            _ = Setup();
        }

        private async Task Setup()
        {
            (Application.Current as App).AppServiceConnected += MainPage_AppServiceConnected;
            (Application.Current as App).AppServiceDisconnected += MainPage_AppServiceDisconnected;
            await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
        }

        private void MainPage_AppServiceConnected(object sender, AppServiceConnection connection)
        {
            _connection = connection;
            Populate();
        }

        private async void MainPage_AppServiceDisconnected(object sender, EventArgs e)
        {
            _connection = null;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Curtain.Visibility = Visibility.Visible;
            });
            await Task.Delay(3000);
            await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
        }

        private async void Populate()
        {
            var request = new ValueSet
            {
                { "request", "populate" }
            };
            var response = await _connection.SendMessageAsync(request);

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Monitors.Clear();
                Curtain.Visibility = Visibility.Collapsed;

                var count = (int)response.Message["count"];
                for (int i = 0; i < count; i++)
                {
                    Monitors.Add(new Monitor
                    {
                        Index = (byte)response.Message[$"index{i}"],
                        Name = (string)response.Message[$"name{i}"],
                        Brightness = (int)response.Message[$"brightness{i}"],
                        Contrast = (int)response.Message[$"contrast{i}"]
                    });
                }
            });
        }

        private async void Brightness_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var monitor = (sender as FrameworkElement).DataContext as Monitor;
            if (monitor == null) { return; }
            if (monitor.Brightness == e.NewValue) { return; }

            var args = new ValueSet
            {
                { "request", "brightness" },
                { "index", monitor.Index },
                { "value", (int)e.NewValue }
            };
            var response = await _connection.SendMessageAsync(args);
            var result = response.Status == AppServiceResponseStatus.Success ? (int)response.Message["brightness"] : -1;
            Debug.WriteLine($"Set brightness: {result}");
        }

        private async void Contrast_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var monitor = (sender as FrameworkElement).DataContext as Monitor;
            if (monitor == null) { return; }
            if (monitor.Contrast == e.NewValue) { return; }

            var args = new ValueSet
            {
                { "request", "contrast" },
                { "index", monitor.Index },
                { "value", (int)e.NewValue }
            };
            var response = await _connection.SendMessageAsync(args);
            var result = response.Status == AppServiceResponseStatus.Success ? (int)response.Message["contrast"] : -1;
            Debug.WriteLine($"Set contrast: {result}");
        }

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            var request = new ValueSet
            {
                { "request", "exit" }
            };
            await _connection.SendMessageAsync(request);
        }
    }
}
