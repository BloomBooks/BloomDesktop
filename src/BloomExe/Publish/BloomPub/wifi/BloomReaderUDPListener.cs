using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Publish.BloomPUB.wifi;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// Helper class to listen for a single packet from the Android. Construct an instance to start
    /// listening (on another thread); hook NewMessageReceived to receive a packet each time a client sends it.
    /// </summary>
    class BloomReaderUDPListener : IDisposable
    {
        // must match BloomReader.NewBookListenerService.desktopPort
        // and be different from WiFiAdvertiser.Port and port in BloomReaderPublisher.SendBookToWiFi
        private int _portToListen = 5915;
        public event EventHandler<AndroidMessageArgs> NewMessageReceived;
        private CancellationTokenSource _cts;

        //constructor: starts listening.
        public BloomReaderUDPListener()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ListenAsync(_cts.Token));
        }

        /// <summary>
        /// Listens as an async task for UDP packets from the Android Bloom Reader.
        /// </summary>
        /// <remarks>
        /// This was suggested by github copilot as a way to do async UDP listening with cancellation support.
        /// </remarks>
        private async Task ListenAsync(CancellationToken ct)
        {
            using (var client = new UdpClient(_portToListen))
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // UdpClient.ReceiveAsync does not accept a CancellationToken directly.
                        // Workaround: Use Task.WhenAny to support cancellation.
                        var receiveTask = client.ReceiveAsync();
                        var completedTask = await Task.WhenAny(
                            receiveTask,
                            Task.Delay(Timeout.Infinite, ct)
                        );
                        if (completedTask == receiveTask)
                        {
                            var result = receiveTask.Result;
                            NewMessageReceived?.Invoke(this, new AndroidMessageArgs(result.Buffer));
                        }
                        else
                        {
                            // Cancellation requested
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (SocketException se) when (!ct.IsCancellationRequested)
                    {
                        throw;
                    }
                }
            }
        }

        public void StopListener()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            StopListener();
        }
    }
}
