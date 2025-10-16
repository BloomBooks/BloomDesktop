// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bloom.Api
{
    /// <summary>
    /// ASP.NET Core middleware for handling /bloom/api/* requests.
    /// Phase 2.2 Implementation: Minimal API routing that delegates to BloomApiHandler.
    ///
    /// This middleware:
    /// 1. Intercepts requests to /bloom/api/*
    /// 2. Creates RequestInfo adapter for the HTTP context
    /// 3. Delegates to BloomApiHandler.ProcessRequestAsync
    /// 4. Handles response writing
    /// </summary>
    public class KestrelApiMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<KestrelApiMiddleware> _logger;
        private readonly BloomApiHandler _apiHandler;

        public KestrelApiMiddleware(
            RequestDelegate next,
            ILogger<KestrelApiMiddleware> logger,
            BloomApiHandler apiHandler
        )
        {
            _next = next;
            _logger = logger;
            _apiHandler = apiHandler;
        }

        /// <summary>
        /// Main middleware invoke method.
        /// Routes /bloom/api/* requests to the API handler.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value;

            // Check if this is an API request
            if (path != null && path.StartsWith("/bloom/api/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _logger.LogDebug($"Processing API request: {path}");

                    // Remove the "/bloom/" prefix to get the local path (keep "api/" for BloomApiHandler)
                    // BloomApiHandler.ProcessRequestAsync expects paths like "api/pagetemplatethumbnail"
                    var localPath = path.Substring("/bloom/".Length);

                    // Create a request info adapter
                    var requestInfo = new KestrelRequestInfo(context);

                    try
                    {
                        // Process the API request
                        var handled = await _apiHandler.ProcessRequestAsync(requestInfo, localPath);

                        if (handled)
                        {
                            _logger.LogDebug($"API request handled: {path}");
                            return;
                        }
                    }
                    finally
                    {
                        requestInfo?.Dispose();
                    }

                    // If not handled by API handler, return 404
                    _logger.LogDebug($"API request not handled by handler: {path}");
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync($"Not found: {path}");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing API request: {path}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync($"Error: {ex.Message}");
                    return;
                }
            }

            // Not an API request, pass to next middleware
            await _next(context);
        }
    }

    /// <summary>
    /// Extension method to register KestrelApiMiddleware in the middleware pipeline.
    /// </summary>
    public static class KestrelApiMiddlewareExtensions
    {
        public static IApplicationBuilder UseKestrelApiMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<KestrelApiMiddleware>();
        }
    }
}
