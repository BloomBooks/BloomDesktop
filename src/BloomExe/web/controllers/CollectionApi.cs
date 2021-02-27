using System;
using System.Collections.Generic;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Newtonsoft.Json;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;

namespace Bloom.web.controllers
{

	public class CollectionApi
	{
		private readonly CollectionSettings _settings;
		private readonly BookCollection _collection;
		public const string kApiUrlPart = "collection/";
		public 	 CollectionApi(CollectionSettings settings, BookCollection collection)
		{
			_settings = settings;
			_collection = collection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "name", HandleCollectionNameRequest, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "books", HandleBooksRequest, true);
		}
		public void HandleCollectionNameRequest(ApiRequest request)
		{
			// always null? request.ReplyWithText(_collection.Name);
			request.ReplyWithText(_settings.CollectionName);
		}


		public void HandleBooksRequest(ApiRequest request)
		{
			//dynamic books = new ExpandoObject();
			
			
			request.ReplyWithJson("[{'title':'one'},{'title':'two']");
		}

	}
}
