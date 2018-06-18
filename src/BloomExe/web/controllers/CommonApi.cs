using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;

namespace Bloom.web.controllers
{
	/// <summary>
	/// API functions common to various areas of Bloom's HTML UI.
	/// </summary>
	public class CommonApi
	{
		private readonly CollectionSettings _settings;

		// Called by autofac, which creates the one instance and registers it with the server.
		public CommonApi(CollectionSettings settings)
		{
			_settings = settings;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("common/enterpriseFeaturesEnabled", HandleEnterpriseFeaturesEnabled, false);
		}

		public void HandleEnterpriseFeaturesEnabled(ApiRequest request)
		{
			lock (request)
			{
				request.ReplyWithText(_settings.HaveEnterpriseFeatures ? "true" : "false");
			}
		}
	}
}
