using System.Net.Http;
using Bloom.Utils;

namespace Bloom
{
    /// <summary>
    /// A concrete implementation of IBloomWebClient backed by HttpClient.
    /// Having this behind the interface lets us mock DownloadString in tests
    /// to simulate things like a captive portal.
    /// </summary>
    public class BloomWebClient : IBloomWebClient
    {
        // A single shared HttpClient is the recommended pattern; reusing it avoids socket exhaustion.
        private static readonly HttpClient s_client = new HttpClient();

        /// <summary>
        /// Synchronously GET the given url and return the response body as a string.
        /// Throws HttpRequestException (or TaskCanceledException on timeout), which callers handle.
        /// </summary>
        public string DownloadString(string url)
        {
            // RunSync executes on the thread pool so we don't deadlock if called on a thread
            // with a synchronization context (e.g. the WinForms UI thread).
            return AsyncUtil.RunSync(() => s_client.GetStringAsync(url));
        }
    }

    /// <summary>
    /// Allows moq-ing of DownloadString to return a simulated captive portal.
    /// </summary>
    public interface IBloomWebClient
    {
        string DownloadString(string url);
    }
}
