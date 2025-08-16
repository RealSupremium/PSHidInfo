using Avalonia.Controls;
using Avalonia.Threading;
using HidSharp;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PSHidInfo
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<HidDevice> _devices;
        private PSHidDevice? _deviceManager;

        private bool _ignoreEvent = false;

        public MainWindow()
        {
            InitializeComponent();
            _devices = new ObservableCollection<HidDevice>();
            DeviceSelector.ItemsSource = _devices;

            try
            {
                foreach (var deviceInfo in DeviceList.Local.GetHidDevices(vendorID: 1356))
                {
                    _devices.Add(deviceInfo);
                }
            }
            catch (Exception ex)
            {
                ShowMessageBox($"Error enumerating devices: {ex.Message}");
            }

            if (_devices.Count == 0)
            {
                ShowMessageBox("No compatible Sony (VID 1356) device found.");
            }

            this.Closing += MainWindow_Closing;
        }

        private void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Task.Run(StartDevice);
            Rates.SelectedIndex = -1;
        }

        private async Task StartDevice()
        {
            if (DeviceSelector.SelectedItem is HidDevice selectedDevice)
            {
                try
                {
                    _deviceManager = new PSHidDevice(selectedDevice);
                    _deviceManager.FrequencyDataUpdated += DeviceManager_FrequencyDataUpdated;
                    _deviceManager.LatencyDataUpdated += DeviceManager_LatencyDataUpdated;
                    _deviceManager.RateUpdated += DeviceManager_RateUpdated;
                    await _deviceManager.Start();
                }
                catch (Exception ex)
                {
                    ShowMessageBox($"Device Error for {selectedDevice.DevicePath}:\n{ex.Message}");
                    _deviceManager?.Dispose();
                    _deviceManager = null;
                }
            }
        }

        private void DeviceManager_LatencyDataUpdated(object? sender, DataUpdateEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Latency.Text = $"Median {e.Median:F3} ms\r\nDeviation {e.Deviation:F3} ms\r\nAverage {e.Average:F3} ms";
            });
        }

        private void DeviceManager_FrequencyDataUpdated(object? sender, DataUpdateEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Indicator.Text = $"Median {e.Median:F3} Hz\r\nDeviation {e.Deviation:F3} Hz\r\nAverage {e.Average:F3} Hz";
            });
        }

        private void DeviceManager_RateUpdated(object? sender, RateEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _ignoreEvent = true;
                Rates.SelectedItem = e.PollRate;
                _ignoreEvent = false;
            });
        }

        private void Rate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Rates.SelectedItem is Rate newRate && _deviceManager != null && !_ignoreEvent)
            {
                try
                {
                    _deviceManager.SetPollingRate(newRate);
                }
                catch (Exception ex)
                {
                    ShowMessageBox($"Error setting polling rate: {ex.Message}");
                }
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _deviceManager?.Dispose();
        }

        private void DeviceSelector_DropDownOpened(object sender, EventArgs e)
        {
            // Stop the current device since the drop down being opened unselects the current device.
            _deviceManager?.Dispose();
            _deviceManager = null;

            _devices.Clear();
            try
            {
                foreach (var deviceInfo in DeviceList.Local.GetHidDevices(vendorID: 1356))
                {
                    _devices.Add(deviceInfo);
                }
            }
            catch (Exception ex)
            {
                ShowMessageBox($"Error enumerating devices: {ex.Message}");
            }
        }

        private void ShowMessageBox(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = "Error",
                    ContentMessage = message,
                    ButtonDefinitions = ButtonEnum.Ok,
                    Icon = MsBox.Avalonia.Enums.Icon.Error
                }).ShowAsPopupAsync(this);
            });
        }
    }
}