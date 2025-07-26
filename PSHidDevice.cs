using HidSharp;
using System.Diagnostics;

namespace PSHidInfo
{
    /// <summary>
    /// Event arguments for data updates from the device.
    /// </summary>
    public class DataUpdateEventArgs : EventArgs
    {
        public double Median { get; }
        public double Deviation { get; }
        public double Average { get; }

        public DataUpdateEventArgs(double median, double deviation, double average)
        {
            Median = median;
            Deviation = deviation;
            Average = average;
        }
    }

    /// <summary>
    /// Manages the reading and writing threads for a specific PS HID device.
    /// Implements IDisposable to ensure proper resource cleanup.
    /// </summary>
    public class PSHidDevice : IDisposable
    {
        private readonly HidDevice _device;
        private HidStream? _deviceStream;
        private readonly CancellationTokenSource _cancellationTokenSource;

        // Rolling statistics for frequency
        private readonly RollingAverage _rollingAverage = new RollingAverage(200);
        private readonly RollingMedian _rollingMedian = new RollingMedian(200);

        // Rolling statistics for latency
        private readonly RollingAverage _rollingAverageLatency = new RollingAverage(20);
        private readonly RollingMedian _rollingMedianLatency = new RollingMedian(20);

        /// <summary>
        /// Fires when new frequency data is available.
        /// </summary>
        public event EventHandler<DataUpdateEventArgs>? FrequencyDataUpdated;

        /// <summary>
        /// Fires when new latency data is available.
        /// </summary>
        public event EventHandler<DataUpdateEventArgs>? LatencyDataUpdated;

        /// <summary>
        /// Initializes a new instance of the PSHidDevice class.
        /// </summary>
        /// <param name="device">The HID device to manage.</param>
        public PSHidDevice(HidDevice device)
        {
            _device = device;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Asynchronously starts the device, initializing the streams and starting the read/write tasks.
        /// </summary>
        public async Task Start()
        {
            try
            {
                _deviceStream = _device.Open();
                var token = _cancellationTokenSource.Token;
                Task ReadThreadTask = ReadThread(token);
                Task WriteThreadTask = WriteThread(token);

                Task firstTask = await Task.WhenAny(ReadThreadTask, WriteThreadTask);

                if (firstTask.IsFaulted)
                {
                    throw firstTask.Exception.GetBaseException() ?? new Exception("An error occurred in the device threads.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Device error: {ex.Message}");
                throw; // Rethrow to be handled by the UI
            }
        }

        /// <summary>
        /// The main loop for reading data from the HID device.
        /// </summary>
        private async Task ReadThread(CancellationToken token)
        {
            if (_deviceStream == null)
            {
                return;
            }

            int reportLength = _device.GetMaxOutputReportLength();
            if (reportLength == 0)
            {
                throw new IOException("Cannot read output report! Max size is 0.");
            }

            uint? lastDeviceTimeNullable = null;

            byte[] reportBuffer = new byte[reportLength];

            // This is a bit of a hack. Might also want to support other PS controllers in the future?
            bool isDualsense = _device.ProductID == 3302 || _device.ProductID == 3570;

            int deviceTimestampOffset = isDualsense ? 50 : 49;

            while (!token.IsCancellationRequested && _deviceStream.CanRead)
            {
                try
                {
                    int readLength = await _deviceStream.ReadAsync(reportBuffer, 0, reportLength);

                    for (int i = 0; i < readLength / 78; i++)
                    {
                        uint currentTime = (uint)BitConverter.ToInt32(reportBuffer, deviceTimestampOffset + (i * 78));

                        if (lastDeviceTimeNullable is uint lastDeviceTime && currentTime != lastDeviceTime)
                        {
                            uint delta = currentTime - lastDeviceTime;

                            double frequency = 3e6 / (double)delta;
                            _rollingAverage.Add(frequency);
                            _rollingMedian.Add(_rollingAverage.Average);

                            OnFrequencyDataUpdated(new DataUpdateEventArgs(_rollingMedian.Median, _rollingMedian.Deviation, _rollingAverage.Average));
                        }

                        lastDeviceTimeNullable = currentTime;
                    }
                }
                catch (IOException) { break; } // Stream was likely closed
                catch (TimeoutException) { continue; } // No data, continue waiting
                catch (Exception) { break; } // Other exceptions
            }
        }

        /// <summary>
        /// The main loop for getting latency from the HID device.
        /// </summary>
        private async Task WriteThread(CancellationToken token)
        {
            if (_deviceStream == null) return;

            Stopwatch stopwatch = new Stopwatch();
            byte[] featureReport = new byte[0x40];

            while (!token.IsCancellationRequested && _deviceStream.CanWrite)
            {
                try
                {
                    // Prepare to receive the response
                    featureReport.AsSpan().Fill(0);
                    featureReport[0] = 0x81; // Report ID
                    featureReport[1] = 0x09;
                    featureReport[2] = 0x1a;

                    // GetFeature will block until the device responds.
                    // We'll also measure the time it takes.
                    await Task.Run(() =>
                    {
                        stopwatch.Restart();
                        _deviceStream.GetFeature(featureReport);
                        stopwatch.Stop();
                    }, token);

                    // Calculate and report latency stats
                    _rollingAverageLatency.Add(stopwatch.Elapsed.TotalMilliseconds);
                    _rollingMedianLatency.Add(_rollingAverageLatency.Average);

                    OnLatencyDataUpdated(new DataUpdateEventArgs(_rollingMedianLatency.Median, _rollingMedianLatency.Deviation, _rollingAverageLatency.Average));

                    // Wait before the next latency check
                    await Task.Delay(TimeSpan.FromMilliseconds(100), token);
                }
                catch (OperationCanceledException)
                {
                    break; // Exit if cancellation is requested
                }
            }
        }

        /// <summary>
        /// Sets the output polling rate of the device.
        /// </summary>
        public void SetPollingRate(Rate newRate)
        {
            if (_deviceStream == null || !_deviceStream.CanWrite) return;

            byte[] featureReport = new byte[0x30];
            featureReport.AsSpan().Fill(0);

            featureReport[0] = 0x08; // Report ID
            featureReport[1] = 0x0E;

            // First, send a zero rate to reset
            int rate = 0;
            BitConverter.GetBytes(rate).CopyTo(featureReport, 2);
            uint crc = CRC32.CalculateCrc32(0x53, featureReport);
            BitConverter.GetBytes(crc).CopyTo(featureReport, 0x2C);
            _deviceStream.SetFeature(featureReport);
            _deviceStream.Flush();

            // Then, send the new rate
            rate = (int)newRate + (((int)newRate/6) << 16);
            BitConverter.GetBytes(rate).CopyTo(featureReport, 2);
            crc = CRC32.CalculateCrc32(0x53, featureReport);
            BitConverter.GetBytes(crc).CopyTo(featureReport, 0x2C);
            _deviceStream.SetFeature(featureReport);
            _deviceStream.Flush();

            // Reset statistics after changing the rate
            _rollingAverage.Reset();
            _rollingMedian.Reset();
            _rollingAverageLatency.Reset();
            _rollingMedianLatency.Reset();
        }

        protected virtual void OnFrequencyDataUpdated(DataUpdateEventArgs e)
        {
            FrequencyDataUpdated?.Invoke(this, e);
        }

        protected virtual void OnLatencyDataUpdated(DataUpdateEventArgs e)
        {
            LatencyDataUpdated?.Invoke(this, e);
        }

        /// <summary>
        /// Disposes of the device's resources, stopping threads and closing the stream.
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _deviceStream?.Dispose();
            _cancellationTokenSource.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
