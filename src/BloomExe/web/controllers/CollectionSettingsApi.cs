using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish.AccessibilityChecker;
using Bloom.Publish.Epub;
using SIL.CommandLineProcessing;
using SIL.PlatformUtilities;
using SIL.Progress;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Used by the settings dialog (currently just the EnterpriseSettings tab)
	/// </summary>
	public class CollectionSettingsApi
	{
		public const string kApiUrlPart = "settings/";

		// These options must match the strings used in accessibileImage.tsx
		public enum EnterpriseStatus
		{
			None, Community, Subscription
		}

		// These are static so they can easily be set by the collection settings dialog using SetSubscriptionCode()
		private static string SubscriptionCode { get; set; }
		private static DateTime _enterpriseExpiry = DateTime.MinValue;
		// True if the part of the subscription code that identifies the branding is one this version of Bloom knows about
		private static bool _knownBrandingInSubscriptionCode = false;
		private static EnterpriseStatus _enterpriseStatus;
		// This is set when we are running the collection settings dialog in a special mode  where it is
		// brought up automatically to inform the user that a previously used branding code is invalid.
		// (It might be a legacy branding from an earlier Bloom that did not require a validation code,
		// or one whose code has expired.) Unlike the InvalidBranding property on CollectionSettings itself,
		// this one is set ONLY while running the dialog in that mode. Not sure this is the best place to
		// keep track of this fact, but I haven't found a better one that is accessible to the code
		// in WorkspaceView that decides to run the dialog in this mode, the dialog itself, and the
		// React control that implements the behavior.
		public static string InvalidBranding { get; set; }
		
		public void RegisterWithServer(EnhancedImageServer server)
		{	
			server.RegisterEnumEndpointHandler(kApiUrlPart + "enterpriseStatus",
				request => _enterpriseStatus,
				(request, status) =>
				{
					_enterpriseStatus = status;
					if (_enterpriseStatus == EnterpriseStatus.None)
					{
						_knownBrandingInSubscriptionCode = true;
						BrandingChangeHandler("Default", null);
					} else if (_enterpriseStatus == EnterpriseStatus.Community)
					{
						BrandingChangeHandler("Local Community", null);
					}
					else
					{
						BrandingChangeHandler(GetBrandingFromCode(SubscriptionCode), SubscriptionCode);
					}
				}, false);
			server.RegisterEndpointHandler(kApiUrlPart + "invalidBranding",
				request => { request.ReplyWithText(InvalidBranding ?? ""); }, false);
			server.RegisterEndpointHandler(kApiUrlPart + "subscriptionCode", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithText(SubscriptionCode??"");
				}
				else // post
				{
					var requestData = DynamicJson.Parse(request.RequiredPostJson());
					SubscriptionCode = requestData.subscriptionCode;
					_enterpriseExpiry = GetExpirationDate(SubscriptionCode);
					if (_enterpriseExpiry < DateTime.Now) // expired or invalid
					{
						BrandingChangeHandler("Default", null);
					}
					else
					{
						_knownBrandingInSubscriptionCode = BrandingChangeHandler(GetBrandingFromCode(SubscriptionCode), SubscriptionCode);
						if (!_knownBrandingInSubscriptionCode)
						{
							BrandingChangeHandler("Default", null); // Review: or just leave unchanged?
						}
					}
					request.PostSucceeded();
				}
			}, false);
			server.RegisterEndpointHandler(kApiUrlPart + "enterpriseSummary", request =>
			{
				string branding = "";
				if (_enterpriseStatus == EnterpriseStatus.Community)
					branding = "Local Community";
				else if (_enterpriseStatus == EnterpriseStatus.Subscription)
					branding = GetBrandingFromCode(SubscriptionCode);
				var summaryFile = BloomFileLocator.GetOptionalBrandingFile(branding, "summary.htm");
				if (summaryFile == null)
					request.ReplyWithText("");
				else
					request.ReplyWithText(File.ReadAllText(summaryFile, Encoding.UTF8));
			}, false);
			server.RegisterEndpointHandler(kApiUrlPart + "enterpriseExpiry", request =>
			{
				if (_enterpriseExpiry == DateTime.MinValue)
				{
					request.ReplyWithText("null");
				} else if (_knownBrandingInSubscriptionCode)
				{
					// O is ISO 8601, the only format I can find that C# ToString() can produce and JS is guaranteed to parse.
					// See https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Date/parse
					request.ReplyWithText(_enterpriseExpiry.ToString("O", CultureInfo.InvariantCulture));
				}
				else
				{
					request.ReplyWithText("unknown");
				}
			}, false);
		}

		// CollectionSettingsDialog sets this so we can call back with results from the tab.
		public static Func<string, string, bool> BrandingChangeHandler;

		// Parse a string like PNG-RISE-361769-363798 or SIL-LEAD-361769-363644,
		// generated by a private google spreadsheet. The two last elements are numbers;
		// the first is an encoding of an expiry date, the second is a simple hash of
		// the project name (case-insensitive) and the expiry date, used to make it
		// a little less trivial to fake codes. We're not aiming for something that
		// would be difficult for someone willing to take the trouble to read this code.
		public static DateTime GetExpirationDate(string input)
		{
			if (input == null)
				return DateTime.MinValue;
			var parts = input.Split('-');
			if (parts.Length < 3)
				return DateTime.MinValue;
			int last = parts.Length - 1;
			int datePart;
			if (!Int32.TryParse(parts[last - 1], out datePart))
				return DateTime.MinValue;
			int combinedChecksum;
			if (!Int32.TryParse(parts[last], out combinedChecksum))
				return DateTime.MinValue;

			int checkSum = CheckSum(GetBrandingFromCode(input));
			if (Math.Floor(Math.Sqrt(datePart)) + checkSum != combinedChecksum)
				return DateTime.MinValue;
			int dateNum = datePart + 40000; // days since Dec 30 1899
			return new DateTime(1899, 12, 30) + TimeSpan.FromDays(dateNum);
		}

		// From the same sort of code extract the project name,
		// everything up to the second-last hyphen.
		public static string GetBrandingFromCode(string input)
		{
			if (input == null)
				return "";
			var parts = input.Split('-').ToList();
			if (parts.Count < 3)
				return "";
			parts.RemoveAt(parts.Count - 1);
			parts.RemoveAt(parts.Count - 1);
			return string.Join("-", parts.ToArray());
		}

		// Must match the function associated with the code generation google sheet
		private static int CheckSum(string input)
		{
			var sum = 0;
			input = input.ToUpperInvariant();
			for (var i = 0; i < input.Length; i++)
			{
				sum += input[i] * i;
			}
			return sum;
		}

		// Updates things when the legacy combo in another tab is used to set the enterprise project.
		// Does not try to inform the HTML; that is done by reloading the page.
		public static void SetSubscriptionCode(string code, bool knownCode, EnterpriseStatus status)
		{
			SubscriptionCode = code;
			_enterpriseExpiry = GetExpirationDate(code);
			_knownBrandingInSubscriptionCode = knownCode;
			_enterpriseStatus = status;
		}
	}
}
