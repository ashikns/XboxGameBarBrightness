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
        static readonly Dictionary<byte, IMonitor> _monitors = new();
        static readonly object _lock = new();

        static AppServiceConnection _connection = null;
        static AutoResetEvent _disconnectEvent = null;
        static bool _exit = false;

        const string RunMutex = "XboxGameBarBrightness.FullTrust.Mutex.Run";
        const string ConnectEvent = "XboxGameBarBrightness.FullTrust.Event.Connect";

        static void Main(string[] args)
        {
            var runMutex = new Mutex(false, RunMutex);
            if (runMutex.WaitOne(0, false))
            {
                _disconnectEvent = new AutoResetEvent(false);
                var reconnectEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ConnectEvent);

                while (!_exit)
                {
                    InitializeAppServiceConnection();
                    _disconnectEvent.WaitOne();
                    reconnectEvent.WaitOne();
                }
            }
            else
            {
                var reconnectEvent = EventWaitHandle.OpenExisting(ConnectEvent);
                reconnectEvent.Set();
            }
        }

        static async void InitializeAppServiceConnection()
        {
            _connection = new AppServiceConnection
            {
                AppServiceName = "XboxGameBarBrightnessInteropService",
                PackageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName
            };
            _connection.RequestReceived += Connection_RequestReceived;
            _connection.ServiceClosed += Connection_ServiceClosed;

            AppServiceConnectionStatus status = await _connection.OpenAsync();
            if (status != AppServiceConnectionStatus.Success)
            {
                // TODO: error handling
            }
        }

        private static void Connection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
        {
            // signal the event so the process can shut down
            _disconnectEvent.Set();
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
                            if (_monitors.Count == 0)
                            {
                                foreach (var m in await MonitorManager.EnumerateMonitorsAsync())
                                {
                                    m.UpdateBrightness();
                                    m.UpdateContrast();

                                    _monitors.Add(m.DisplayIndex, m);
                                }
                            }

                            var response = new ValueSet();
                            var idx = 0;

                            foreach (var m in _monitors.Values)
                            {
                                m.UpdateBrightness();
                                m.UpdateContrast();

                                response.Add($"index{idx}", m.DisplayIndex);
                                response.Add($"name{idx}", m.Description);
                                response.Add($"brightness{idx}", m.Brightness);
                                response.Add($"contrast{idx}", m.Contrast);
                                idx++;
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
                    case "exit":
                        {
                            _exit = true;
                            _disconnectEvent.Set();
                            var reconnectEvent = EventWaitHandle.OpenExisting(ConnectEvent);
                            reconnectEvent.Set();
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
