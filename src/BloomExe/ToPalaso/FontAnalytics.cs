using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace Bloom.ToPalaso
{
	
	internal class FontAnalytics
	{
		static HttpClient sClient = new HttpClient();

		public class FontEvent
		{
			public string Value { get; private set; }
			private FontEvent(string value) { Value = value; }
			public static FontEvent ApplyFont  => new FontEvent( "apply-font");
			public static FontEvent PublishPdf  => new FontEvent( "publish-pdf");
			public static FontEvent PrintPdf => new FontEvent("print-pdf");
			public static FontEvent Publish  => new FontEvent( "publish-ebook");
			public static FontEvent PublishWeb  => new FontEvent( "publish-web");
		}

		public static void Report(string applicationName, FontEvent fontEventType, string documentId, string langTag, string font_name) {

			//			curl - i--location--request POST 'https://sil-font-analytics.deno.dev/api/v1/report-font-use' \
			//--header 'Content-Type: application/json' \
			//-d '{"source":"foo","document_id":"huh","font_name":"Padauk","language_tag":"my-MY","event_type":"configure_project"}'

			
			dynamic data = new JObject();
			data.source = applicationName;
			data.document_id = documentId;
			data.language_tag = langTag;
			data.event_type = fontEventType;
			var dataJson = JsonConvert.SerializeObject(data);
			HttpContent content = new StringContent(dataJson);
			content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

			try
			{
				sClient.PostAsync("https://font-analytics.languagetechnology.org/api/v1/report-font-use", content);
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
