﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Workspace;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Extensions;
using ApplicationException = System.ApplicationException;
using Timer = System.Windows.Forms.Timer;

namespace Bloom.web.controllers
{
	/// <summary>
	/// API functions common to various areas of Bloom's HTML UI.
	/// </summary>
	public class CommonApi
	{
		private readonly CollectionSettings _settings;
		private readonly BookSelection _bookSelection;
		public static bool AuthorMode { get; set; }

		// Called by autofac, which creates the one instance and registers it with the server.
		public CommonApi(CollectionSettings settings, BookSelection bookSelection)
		{
			_settings = settings;
			_bookSelection = bookSelection;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("uiLanguages", HandleUiLanguages, false);
			server.RegisterEndpointHandler("bubbleLanguages", HandleBubbleLanguages, false);
			server.RegisterEndpointHandler("authorMode", HandleAuthorMode, false);
			server.RegisterEndpointHandler("topics", HandleTopics, false);
			server.RegisterEndpointHandler("common/enterpriseFeaturesEnabled", HandleEnterpriseFeaturesEnabled, false);
			server.RegisterEndpointHandler("common/error", HandleJavascriptError, false);
			server.RegisterEndpointHandler("common/preliminaryError", HandlePreliminaryJavascriptError, false);
		}

		/// <summary>
		/// Returns json with property languages, an array of objects (one for each UI language Bloom knows about)
		/// each having label (what to show in a menu) and tag (the language code).
		/// Used in language select control in hint bubbles tab of text box properties dialog
		/// brought up from cog control in origami mode.
		/// </summary>
		/// <param name="request"></param>
		public void HandleUiLanguages(ApiRequest request)
		{
			lock (request)
			{
				var langs = new List<object>();
				foreach (var code in L10NSharp.LocalizationManager.GetAvailableLocalizedLanguages())
				{
					var langItem = WorkspaceView.CreateLanguageItem(code);
					langs.Add(new { label = langItem.MenuText, tag = code });
				}
				request.ReplyWithJson(JsonConvert.SerializeObject(new { languages = langs }));
			}
		}

		public void HandleBubbleLanguages(ApiRequest request)
		{
			lock (request)
			{
				var bubbleLangs = new List<string>();
				bubbleLangs.Add(LocalizationManager.UILanguageId);
				if (_bookSelection.CurrentSelection.MultilingualContentLanguage2 != null)
					bubbleLangs.Add(_bookSelection.CurrentSelection.MultilingualContentLanguage2);
				if (_bookSelection.CurrentSelection.MultilingualContentLanguage3 != null)
					bubbleLangs.Add(_bookSelection.CurrentSelection.MultilingualContentLanguage3);
				bubbleLangs.AddRange(new[] { "en", "fr", "sp", "ko", "zh-Hans" });
				// If we don't have a hint in the UI language or any major language, it's still
				// possible the page was made just for this langauge and has a hint in that language.
				// Not sure whether this should be before or after the list above.
				// Definitely wants to be after UILangage, otherwise we get the surprising result
				// that in a French collection these hints stay French even when all the rest of the
				// UI changes to English.
				bubbleLangs.Add(_bookSelection.CurrentSelection.CollectionSettings.Language1Iso639Code);
				// if it isn't available in any of those we'll arbitrarily take the first one.
				request.ReplyWithJson(JsonConvert.SerializeObject(new {langs = bubbleLangs}));
			}
		}

		public void HandleAuthorMode(ApiRequest request)
		{
			lock (request)
			{
				request.ReplyWithText(AuthorMode ? "true" : "false");
			}
		}


		public void HandleTopics(ApiRequest request)
		{
			var keyToLocalizedTopicDictionary = new Dictionary<string, string>();
			foreach (var topic in BookInfo.TopicsKeys)
			{
				var localized = LocalizationManager.GetDynamicString("Bloom", "Topics." + topic, topic,
					@"shows in the topics chooser in the edit tab");
				keyToLocalizedTopicDictionary.Add(topic, localized);
			}
			string localizedNoTopic = LocalizationManager.GetDynamicString("Bloom", "Topics.NoTopic", "No Topic",
				@"shows in the topics chooser in the edit tab");
			var arrayOfKeyValuePairs = from key in keyToLocalizedTopicDictionary.Keys
				orderby keyToLocalizedTopicDictionary[key]
				select string.Format("\"{0}\": \"{1}\"", key, keyToLocalizedTopicDictionary[key]);
			var pairs = arrayOfKeyValuePairs.Concat(",");
			var data = string.Format("{{\"NoTopic\": \"{0}\", {1} }}", localizedNoTopic, pairs);

			request.ReplyWithJson(data);
		}

		public void HandleEnterpriseFeaturesEnabled(ApiRequest request)
		{
			lock (request)
			{
				request.ReplyWithText(_settings.HaveEnterpriseFeatures ? "true" : "false");
			}
		}

		public void HandleJavascriptError(ApiRequest request)
		{
			lock (lockJsError)
			{
				preliminaryJavascriptError = null; // got a real report.
			}
			lock (request)
			{
				var details = DynamicJson.Parse(request.RequiredPostJson());
				ReportJavascriptError(details);
				request.PostSucceeded();
			}
		}

		private static void ReportJavascriptError(dynamic details)
		{
			var ex = new ApplicationException(details.message + Environment.NewLine + details.stack);
			// For now unimportant JS errors are still quite common, sadly. Per BL-4301, we don't want
			// more than a toast, even for developers.
			// It would seem logical that we should consider Browser.SuppressJavaScriptErrors here,
			// but somehow none are being reported while making an epub preview, which was its main
			// purpose. So I'm leaving that out until we know we need it.
			NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "A JavaScript error occurred", details.message, ex);
		}

		object lockJsError = new object();
		private dynamic preliminaryJavascriptError;
		private Timer jsErrorTimer;

		// This api receives javascript errors with stack dumps that have not been converted to source.
		// Javascript code will then attempt to convert them and report using HandleJavascriptError.
		// In case that fails, after 200ms we will make the report using the unconverted stack.
		public void HandlePreliminaryJavascriptError(ApiRequest request)
		{
			lock (request)
			{
				lock (lockJsError)
				{
					if (preliminaryJavascriptError != null)
					{
						// If we get more than one of these without a real report, the first is most likely to be useful, I think.
						// This also avoids ever having more than one timer running.
						request.PostSucceeded();
						return;
					}
					preliminaryJavascriptError = DynamicJson.Parse(request.RequiredPostJson());
				}

				var form = Application.OpenForms.Cast<Form>().Last();
				// If we don't have an active Bloom form, I think we can afford to discard this report.
				if (form != null)
				{
					form.BeginInvoke((Action) (() =>
					{
						// Arrange to report the error if we don't get a better report of it in 200ms.
						jsErrorTimer?.Stop(); // probably redundant
						jsErrorTimer?.Dispose(); // left over from previous report that had follow-up?
						jsErrorTimer = new Timer {Interval = 200};
						jsErrorTimer.Tick += (sender, args) =>
						{
							jsErrorTimer.Stop(); // probably redundant?
							// not well documented but found some evidence this is OK inside event handler.
							jsErrorTimer.Dispose();
							jsErrorTimer = null;

							dynamic temp;
							lock (lockJsError)
							{
								temp = preliminaryJavascriptError;
								preliminaryJavascriptError = null;
							}

							if (temp != null)
							{
								ReportJavascriptError(temp);
							}
						};
						jsErrorTimer.Start();
					}));
				}
				request.PostSucceeded();
			}
		}
	}
}
