using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Reflection;

namespace Bloom.ToPalaso
{
	
	public class FontAnalytics
	{
		static HttpClient sClient = new HttpClient();

		public class FontEventType
		{
			public string Value { get; private set; }
			private FontEventType(string value) { Value = value; }
			public static FontEventType ApplyFont  => new FontEventType( "apply-font");
			public static FontEventType PublishPdf  => new FontEventType( "publish-pdf");
			public static FontEventType PublishEbook  => new FontEventType( "publish-ebook");
			public static FontEventType PublishWeb  => new FontEventType( "publish-web");
		}

		public static async void Report(string documentId, FontEventType fontEventType, string langTag, bool testOnly, string fontName, string  eventDetails = null) {
			var name = Assembly.GetEntryAssembly().GetName().Name;
			var version = Assembly.GetEntryAssembly().GetName().Version.ToString();
			FontAnalytics.Report(name, version, documentId, fontEventType,  langTag, testOnly, fontName, eventDetails);
		}


		public static async void Report(string applicationName, string applicationVersion, string documentId, FontEventType fontEventType,
									string langTag, bool testOnly, string fontName, string eventDetails = null)
		{
			try
			{
				dynamic data = new JObject();
				data.source = applicationName;
				data.source_version = applicationVersion;
				data.font_name = fontName;
				data.document_id = documentId;
				data.language_tag = langTag;
				data.event_type = fontEventType.Value;
				data.test_only = testOnly;
				if (!string.IsNullOrWhiteSpace(eventDetails))
				{
					data.event_details = eventDetails;
				}
				data.event_time = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
				var dataJson = JsonConvert.SerializeObject(data);
				HttpContent content = new StringContent(dataJson);
				content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

				var r = await sClient.PostAsync("https://font-analytics.languagetechnology.org/api/v1/report-font-use", content);
				if (!r.IsSuccessStatusCode)
				{
					Debug.WriteLine(r.ToString());
					Debug.WriteLine(await r.Content.ReadAsStringAsync());
				}
			}
			catch (Exception err)
			{
#if DEBUG
				throw err;
#endif
				// normally, swallow
			}
		}
	}
}
