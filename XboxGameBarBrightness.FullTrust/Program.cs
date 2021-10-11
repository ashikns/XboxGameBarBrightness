using Monitorian.Core.Models.Monitor;
using System;
using System.Collections.Generic;
using System.Threading;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace XboxGameBarBrightness.FullTrust
{
    class Program
    {
        static Dictionary<byte, IMonitor> _monitors = new();
        static readonly object _lock = new object();

        static AppServiceConnection connection = null;
        static AutoResetEvent appServiceExit;

        static void Main(string[] args)
        {
            appServiceExit = new AutoResetEvent(false);
            InitializeAppServiceConnection();
            appServiceExit.WaitOne();
        }

        static async void InitializeAppServiceConnection()
        {
            connection = new AppServiceConnection
            {
                AppServiceName = "XboxGameBarBrightnessInteropService",
                PackageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName
            };
            connection.RequestReceived += Connection_RequestReceived;
            connection.ServiceClosed += Connection_ServiceClosed;

            AppServiceConnectionStatus status = await connection.OpenAsync();
            if (status != AppServiceConnectionStatus.Success)
            {
                // TODO: error handling
            }
        }

        private static void Connection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
        {
            // signal the event so the process can shut down
            appServiceExit.Set();
        }

        private async static void Connection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            // Get a deferral because we use an awaitable API below to respond to the message
            // and we don't want this call to get cancelled while we are waiting.
            var messageDeferral = args.GetDeferral();

            try
            {
                var request = args.Request.Message["request"] as string;
                switch (request)
                {
                    case "populate":
                        {
                            _monitors.Clear();
                            var response = new ValueSet();

                            foreach (var m in await MonitorManager.EnumerateMonitorsAsync())
                            {
                                m.UpdateBrightness();
                                m.UpdateContrast();

                                _monitors.Add(m.DisplayIndex, m);

                                var i = _monitors.Count - 1;
                                response.Add($"index{i}", m.DisplayIndex);
                                response.Add($"name{i}", m.Description);
                                response.Add($"brightness{i}", m.Brightness);
                                response.Add($"contrast{i}", m.Contrast);
                            }
                            response.Add("count", _monitors.Count);
                            await args.Request.SendResponseAsync(response);
                            break;
                        }
                    case "brightness":
                        {
                            var index = (byte)args.Request.Message["index"];
                            var value = (int)args.Request.Message["value"];

                            AccessResult set;
                            lock (_lock)
                            {
                                set = _monitors[index].SetBrightness(value);
                            }
                            var result = set.Status == AccessStatus.Succeeded ? _monitors[index].Brightness : -1;

                            var response = new ValueSet
                            {
                                { "brightness", result },
                            };
                            await args.Request.SendResponseAsync(response);
                            break;
                        }
                    case "contrast":
                        {
                            var index = (byte)args.Request.Message["index"];
                            var value = (int)args.Request.Message["value"];

                            AccessResult set;
                            lock (_lock)
                            {
                                set = _monitors[index].SetContrast(value);
                            }
                            var result = set.Status == AccessStatus.Succeeded ? _monitors[index].Contrast : -1;

                            var response = new ValueSet
                            {
                                { "contrast", result },
                            };
                            await args.Request.SendResponseAsync(response);
                            break;
                        }
                    default:
                        break;
                }
            }
            finally
            {
                // Complete the deferral so that the platform knows that we're done responding to the app service call.
                // Note for error handling: this must be called even if SendResponseAsync() throws an exception.
                messageDeferral.Complete();
            }
        }
    }
}
