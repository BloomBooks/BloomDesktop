using System.Collections.Generic;
using Bloom.Collection;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.web
{
	static class I18NHandler
	{
		public static bool HandleRequest(string localPath, IRequestInfo info, CollectionSettings currentCollectionSettings)
		{
			var lastSep = localPath.IndexOf("/", System.StringComparison.Ordinal);
			var lastSegment = (lastSep > -1) ? localPath.Substring(lastSep + 1) : localPath;

			switch (lastSegment)
			{
				case "loadStrings":

					var d = new Dictionary<string, string>();
					var post = info.GetPostData();

					foreach (string key in post.Keys)
					{
						var translation = LocalizationManager.GetDynamicString("Bloom", key, post[key]);
						if (!d.ContainsKey(key)) d.Add(key, translation);
					}

					info.ContentType = "application/json";
					info.WriteCompleteOutput(JsonConvert.SerializeObject(d));
					return true;
			}

			return false;
		}
	}
}
