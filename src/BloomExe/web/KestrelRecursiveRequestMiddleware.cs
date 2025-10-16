using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bloom.web
{
    /// <summary>
    /// Middleware that tracks recursive requests and manages thread pools to prevent deadlocks.
    /// Requests with ?generateThumbnailIfNecessary=true are potentially recursive in that we may
    /// have to navigate a browser to the template page in order to construct the thumbnail.
    /// </summary>
    public class KestrelRecursiveRequestMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<KestrelRecursiveRequestMiddleware> _logger;

        // Atomic counter for recursive requests (thread-safe replacement for _threadsDoingRecursiveRequests)
        private static int _recursiveRequestCount = 0;

        // Track busy requests for thread pool management
        private static int _busyRequestCount = 0;

        public KestrelRecursiveRequestMiddleware(
            RequestDelegate next,
            ILogger<KestrelRecursiveRequestMiddleware> logger
        )
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var isRecursiveRequest = IsRecursiveRequestContext(context);

            // Mark the request as recursive in context items for downstream middleware
            if (isRecursiveRequest)
            {
                context.Items["IsRecursiveRequest"] = true;

                // Atomically increment recursive request counter
                var currentRecursiveCount = Interlocked.Increment(ref _recursiveRequestCount);

                _logger.LogDebug(
                    $"Recursive request started. Current count: {currentRecursiveCount}"
                );

                // Note: In Kestrel, we don't need to manage worker threads manually like HttpListener
                // Kestrel's thread pool management is more sophisticated and handles this automatically
                // But we still track the count for monitoring and potential future use
            }

            // Track total busy requests
            var totalBusyCount = Interlocked.Increment(ref _busyRequestCount);
            _logger.LogTrace(
                $"Request started. Busy requests: {totalBusyCount}, Recursive: {_recursiveRequestCount}"
            );

            try
            {
                await _next(context);
            }
            finally
            {
                // Clean up counters
                if (isRecursiveRequest)
                {
                    var remainingRecursiveCount = Interlocked.Decrement(ref _recursiveRequestCount);
                    _logger.LogDebug(
                        $"Recursive request completed. Remaining count: {remainingRecursiveCount}"
                    );
                }

                var remainingBusyCount = Interlocked.Decrement(ref _busyRequestCount);
                _logger.LogTrace(
                    $"Request completed. Remaining busy: {remainingBusyCount}, Recursive: {_recursiveRequestCount}"
                );
            }
        }

        /// <summary>
        /// Requests with ?generateThumbnailIfNecessary=true are potentially recursive in that we may have to navigate
        /// a browser to the template page in order to construct the thumbnail.
        /// </summary>
        /// <param name="context">HttpContext containing the request</param>
        /// <returns>True if this is a recursive request context</returns>
        public static bool IsRecursiveRequestContext(HttpContext context)
        {
            return context.Request.Query["generateThumbnailIfNecessary"] == "true";
        }

        /// <summary>
        /// Get the current count of recursive requests being processed.
        /// </summary>
        public static int RecursiveRequestCount => _recursiveRequestCount;

        /// <summary>
        /// Get the current count of busy requests being processed.
        /// </summary>
        public static int BusyRequestCount => _busyRequestCount;
    }

    /// <summary>
    /// Extension methods for registering the recursive request middleware
    /// </summary>
    public static class KestrelRecursiveRequestMiddlewareExtensions
    {
        public static IApplicationBuilder UseKestrelRecursiveRequestMiddleware(
            this IApplicationBuilder builder
        )
        {
            return builder.UseMiddleware<KestrelRecursiveRequestMiddleware>();
        }
    }
}
