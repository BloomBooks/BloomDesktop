// Copyright (c) 2014-2018 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Threading.Tasks;
using EmbedIO;

namespace Bloom.Api
{
    /// <summary>
    /// Custom EmbedIO module that forwards all requests to BloomServer's queue-based processing system
    /// </summary>
    public class BloomServerModule : WebModuleBase
    {
        private readonly BloomServer _bloomServer;

        public BloomServerModule(string baseRoute, BloomServer bloomServer)
            : base(baseRoute)
        {
            _bloomServer = bloomServer;
        }

        public override bool IsFinalHandler => true;

        protected override async Task OnRequestAsync(IHttpContext context)
        {
            // Queue the request for processing by BloomServer's worker threads
            await _bloomServer.HandleRequestAsync(context);
        }
    }
}
