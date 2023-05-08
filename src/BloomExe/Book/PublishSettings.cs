using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Api;
using Bloom.Publish;
using Newtonsoft.Json;
using Sentry;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Book
{
	/// <summary>
	/// Stores the chosen values for things that can be set by user controls in the Publish tab.
	/// These are typically also things that affect how the Harvester generates publications for
	/// the book.
	/// These objects are stored in meta.json (for now, at least) as the PublishSettings field.
	/// This means this object must be suitable for use in JsonConvert.SerializeObject/DeserializeObject.
	/// </summary>
	public class PublishSettings
	{
		public PublishSettings()
		{
			AudioVideo = new AudioVideoSettings();
			Epub = new EpubSettings();
			BloomPub = new BloomPubSettings();
			BloomLibrary = new BloomLibrarySettings();
		}

		[JsonProperty("audioVideo")] public AudioVideoSettings AudioVideo;

		[JsonProperty("epub")] public EpubSettings Epub;

		[JsonProperty("bloomPUB")]
		public BloomPubSettings BloomPub;

		[JsonProperty("bloomLibrary")] public BloomLibrarySettings BloomLibrary;

		public static PublishSettings FromString(string json)
		{
			var ps = new PublishSettings();
			ps.LoadNewJson(json);
			return ps;
		}
		public void LoadNewJson(string json)
		{
			try
			{
				JsonConvert.PopulateObject(json, this,
					// Previously, various things could be null. As part of simplifying the use of PublishSettings,
					// we now never have nulls; everything gets defaults when it is created.
					// For backwards capabilty, if the json we are reading has a null for a value,
					// do not override the default value that we already have loaded.
					new JsonSerializerSettings() { NullValueHandling=NullValueHandling.Ignore});
			}
			catch (Exception e) { throw new ApplicationException("publish-settings of this book may be corrupt", e); }
		}


		[JsonIgnore]
		public string Json => JsonConvert.SerializeObject(this);

		public static string PublishSettingsPath(string bookFolderPath)
		{
			return bookFolderPath.CombineForPath(BookInfo.PublishSettingsFileName);
		}

		public void WriteToFolder(string bookFolderPath)
		{
			var publishSettingsPath = PublishSettingsPath(bookFolderPath);
			try
			{
				RobustFile.WriteAllText(publishSettingsPath, Json);
			}
			catch (Exception e)
			{
				ErrorReport.NotifyUserOfProblem(e, "Bloom could not save your publish settings.");
			}
		}

		/// <summary>
		/// Make a PublishSettings by reading the json file in the book folder.
		/// If some exception is thrown while trying to do that, or if it doesn't exist,
		/// just return a default PublishSettings.
		/// </summary>
		/// <param name="bookFolderPath"></param>
		/// <returns></returns>
		public static PublishSettings FromFolder(string bookFolderPath)
		{
			var publishSettingsPath = PublishSettingsPath(bookFolderPath);
			PublishSettings ps;
			if (!RobustFile.Exists(publishSettingsPath))
			{
				// see if they have a meta.json file instead, which was the place we used to store some of this info
				ps = MigrateFromOldMetaJson(bookFolderPath);
			}
			else if (TryReadSettings(publishSettingsPath, out PublishSettings result))
				ps = result;
			else
			{
				// We could implement a backup strategy like for MetaData, but I don't
				// think it's worth it. It's not that likely we will lose these, or very critical
				// if we do.
				return new PublishSettings();
			}
			return ps;
		}

		static PublishSettings MigrateFromOldMetaJson(string bookFolderPath)
		{
			// See if we can migrate data from an old metaData.

			var metaDataPath = BookMetaData.MetaDataPath(bookFolderPath);
			var settings = new PublishSettings();
			try
			{
				if (!RobustFile.Exists(metaDataPath))
					return settings;
				var metaDataString = RobustFile.ReadAllText(metaDataPath, Encoding.UTF8);

				// I chose to do this using DynamicJson, rather than just BloomMetaData.FromString() and
				// reading the obsolete properties, in hopes that we can eventually retire the obsolete
				// properties. However, that may not be possible without breaking things when we attempt
				// to load an old meta.json with JsonConvert. Still, at least this approach makes for
				// fewer warnings about use of obsolete methods.
				var metaDataJson = DynamicJson.Parse(metaDataString) as DynamicJson;
				if (metaDataJson.IsDefined("features"))
				{
					if (metaDataJson.TryGet("features", out string[] features))
					{
						if (features != null && features.Any(f => f == "motion"))
						{
							settings.BloomPub.PublishAsMotionBookIfApplicable = true;
							settings.AudioVideo.Motion = true; // something of a guess
						}
					}
				}

				if (metaDataJson.TryGetValue("epub_HowToPublishImageDescriptions", out double val2))
				{
					// unfortunately the default way an enum is converted to Json is as a number.
					// 'None' comes out as 0, OnPage as 1, and if by any chance we encounter an old book
					// using 'Link' that will be 2. We don't have Link any more, but that indicates
					// a desire to publish, so for now we'll treat it as OnPage. No other values should
					// be possible in legacy data. So basically, any value other than zero counts as OnPage
					if (val2 != 0)
						settings.Epub.HowToPublishImageDescriptions = BookInfo.HowToPublishImageDescriptions.OnPage;
				}

				// Note the name mismatch here. The old property unfortunately had this name, though all
				// we actually remove is font size rules.
				if (metaDataJson.TryGetValue("epub_RemoveFontStyles", out bool val3))
				{
					settings.Epub.RemoveFontSizes = val3;
				}

				if (metaDataJson.TryGet("textLangsToPublish", out DynamicJson langs))
				{
					if (langs.TryGet("bloomPUB", out Dictionary<string, InclusionSetting> forBloomPub))
						settings.BloomPub.TextLangs = forBloomPub;
					if (langs.TryGet("bloomLibrary", out Dictionary<string, InclusionSetting> forBloomLibrary))
						settings.BloomLibrary.TextLangs = forBloomLibrary;
				}

				if (metaDataJson.TryGet("audioLangsToPublish", out DynamicJson audioLangs))
				{
					if (audioLangs.TryGet("bloomPUB", out Dictionary<string, InclusionSetting> forBloomPub))
						settings.BloomPub.AudioLangs = forBloomPub;
					if (audioLangs.TryGet("bloomLibrary", out Dictionary<string, InclusionSetting> forBloomLibrary))
						settings.BloomLibrary.AudioLangs = forBloomLibrary;
				}

				if (metaDataJson.TryGet("signLangsToPublish", out DynamicJson signLangs))
				{
					if (signLangs.TryGet("bloomPUB", out Dictionary<string, InclusionSetting> forBloomPub))
						settings.BloomPub.SignLangs = forBloomPub;
					if (signLangs.TryGet("bloomLibrary", out Dictionary<string, InclusionSetting> forBloomLibrary))
						settings.BloomLibrary.SignLangs = forBloomLibrary;
				}
			}
			catch (Exception e)
			{
				// If we can't migrate for some reason, just drop back to defaults.
				SentrySdk.CaptureException(e);
			}

			return settings;

		}

		private static bool TryReadSettings(string path, out PublishSettings result)
		{
			result = null;
			if (!RobustFile.Exists(path))
				return false;
			try
			{
				result = FromString(RobustFile.ReadAllText(path, Encoding.UTF8));
				return true;
			}
			catch (Exception e)
			{
				Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
				return false;
			}
		}
	}

	/// <summary>
	/// Settings used by the Audio/Video tab.
	/// </summary>
	public class AudioVideoSettings
	{
		public AudioVideoSettings()
		{
			Format = "facebook";
			PageTurnDelay = 3000;
			PlayerSettings = "";
			PageRange = new int[0];
		}

		/// <summary>
		/// How long to display pages with no narration (milliseconds)
		/// </summary>
		[JsonProperty("pageTurnDelay")]
		public int PageTurnDelay;

		/// <summary>
		/// For Javascript and the Javascript API and BloomPlayer, it's most convenient to have
		/// this delay as a time in seconds. But for a setting to store in a persistent file,
		/// I feel more comfortable using an int value in ms. Floating point values have all
		/// kinds of ways to go wrong, like things that would be thought equal but are not exactly
		/// so, locale issues,...
		/// </summary>
		[JsonIgnore]
		public double PageTurnDelayDouble
		{
			get
			{
				return PageTurnDelay / 1000.0;
			}
			set
			{
				PageTurnDelay = (int)Math.Round(value * 1000);
			}
		}

		/// <summary>
		/// Which publishing mode to use. Currently one of facebook, feature, youtube, and mp3.
		/// </summary>
		[JsonProperty("format")]
		public string Format;

		/// <summary>
		/// Whether to record the book in landscape mode with full-screen pictures and
		/// animations. Closely related to the BloomPubSettings field of the same name,
		/// but a video can be made of reading the book with text visible and highlighted
		/// and no animations.
		/// </summary>
		[JsonProperty("motion")]
		public bool Motion;

		/// <summary>
		/// A string containing the video player settings that are handled entirely by
		/// BloomPlayer. The preview instance of BloomPlayer sends this string when a relevant
		/// control is operated, and the same string is sent to the Recording instance.
		/// Currently it is a URL-encoded JSON string representing the language chosen for
		/// playback and whether to play any image description narration, but the design
		/// is intended to allow more relevant controls to be added to BP without changing
		/// Bloom Editor.
		/// One downside is that quotes in the PlayerSettings JSON must be escaped in the
		/// file. We think it is worth this to keep the content of this string opaque to BE.
		/// </summary>
		[JsonProperty("playerSettings")]
		public string PlayerSettings;

		// An array (should always be exactly two items) of pages to include.
		// May be null or an empty array to indicate "all pages"
		[JsonProperty("pageRange")]
		public int[] PageRange;
	}

	/// <summary>
	/// Settings used by the BloomPUB publish tab.
	/// Currently incomplete; others should move here eventually.
	/// </summary>
	public class BloomPubSettings
	{
		public BloomPubSettings()
		{
			ImageSettings = new ImagePublishSettings();
			TextLangs = new Dictionary<string, InclusionSetting>();
			AudioLangs = new Dictionary<string, InclusionSetting>();
			SignLangs = new Dictionary<string, InclusionSetting>();
			PublishAsMotionBookIfApplicable = true; // Default for new books (ignored if they have no motion settings)
		}

		// Whether to publish the book as a motion book, that can be rotated
		// horizontal to trigger autoplay with animations. This may well be true
		// (it is by default) even if the book has no motion settings. However,
		// in that case the corresponding feature will not be set, and of course
		// the book can't actually be a motion book.
		[JsonProperty("motion")]
		public bool PublishAsMotionBookIfApplicable;

		/// <summary>
		/// This used to correspond to the checkbox values of which languages the user wants to publish the text for.
		/// It is now obsolete; we decided to use the BloomLibrary language settings in both screens.
		/// Keeping it for now so saved settings don't get thrown away in case users persuade us to reinstate it.
		/// </summary>
		/// <remarks>Previouly bookInfo.TextLangsToPublish.ForBloomPUB</remarks>
		[JsonProperty("textLangs")]
		public Dictionary<string, InclusionSetting> TextLangs;

		/// <summary>
		/// This used to correspond to the checkbox values of which languages the user wants to publish the audio for
		/// It is now obsolete; we decided to use the BloomLibrary language settings in both screens.
		/// Keeping it for now so saved settings don't get thrown away in case users persuade us to reinstate it.
		/// </summary>
		/// <remarks>Previouly bookInfo.AudioLangsToPublish.ForBloomPUB</remarks>
		[JsonProperty("audioLangs")]
		public Dictionary<string, InclusionSetting> AudioLangs;

		/// <summary>
		/// Used to be the sign language(s) -- currently we allow only one -- which the user wants to publish
		/// It is now obsolete; we decided to use the BloomLibrary language settings in both screens.
		/// Keeping it for now so saved settings don't get thrown away in case users persuade us to reinstate it.
		/// </summary>
		/// <remarks>Previouly bookInfo.SignLangsToPublish.ForBloomPUB</remarks>
		[JsonProperty("signLangs")]
		public Dictionary<string, InclusionSetting> SignLangs;

		/// <summary>
		/// The image resolution settings for this BloomPUB
		/// </summary>
		[JsonProperty("imageSettings")]
		public ImagePublishSettings ImageSettings;
	}

	public class ImagePublishSettings
	{
		// ENHANCE: I think these should ideally be readonly, but that requires a higher C# version.
		[JsonProperty("maxWidth")]
		public uint MaxWidth;

		[JsonProperty("maxHeight")]
		public uint MaxHeight;

		public ImagePublishSettings()
		{
			// See discussion in BL-5385
			MaxWidth = 600;
			MaxHeight = 600;
		}
	}

	public class EpubSettings
	{
		public EpubSettings()
		{
			Mode = "fixed";
			RemoveFontSizes = false;
		}
		/// <summary>
		/// This item indicates how the user would like Epubs of this book to handle Image Descriptions
		/// Current possibilities are 'None' and 'OnPage'; previous value 'Links' became obsolete in Bloom 4.6.
		/// </summary>
		[JsonProperty("howToPublishImageDescriptions")]
		public BookInfo.HowToPublishImageDescriptions HowToPublishImageDescriptions;

		/// <summary>
		/// This is an obsolete property. For now at least, I'm preserving it in case the book is also
		/// accessed by older versions of Bloom.
		/// It corresponds to a checkbox we used to have indicating that the user wants to use the
		/// eReader's native font sizes, and to the field of the same name in EpubMaker which is used to control.
		/// </summary>
		/// <remarks>Replaced the old BookMetaData property Epub_RemoveFontSizes, which was (unfortunately)
		/// persisted as epub_RemoveFontStyles</remarks>
		[JsonProperty("removeFontSizes")]
		public bool RemoveFontSizes;


		/// <summary>
		/// Currently "fixed" and "flowable" are supported.
		/// </summary>
		[JsonProperty("mode")] public string Mode;

		public EpubSettings Clone()
		{
			var serialized = JsonConvert.SerializeObject(this);
			return JsonConvert.DeserializeObject<EpubSettings>(serialized);
		}
	}

	public class BloomLibrarySettings
	{
		public BloomLibrarySettings()
		{
			TextLangs = new Dictionary<string, InclusionSetting>();
			AudioLangs = new Dictionary<string, InclusionSetting>();
			SignLangs = new Dictionary<string, InclusionSetting>();
			// By default we will publish it as being a comic if it has such data.
			Comic = true;
		}

		/// <summary>
		/// This corresponds to the checkbox values of which languages the user wants to publish the text for.
		/// </summary>
		/// <remarks>Previouly bookInfo.TextLangsToPublish.ForBloomLibrary</remarks>
		[JsonProperty("textLangs")]
		public Dictionary<string, InclusionSetting> TextLangs;

		/// <summary>
		/// This corresponds to the checkbox values of which languages the user wants to publish the audio for
		/// </summary>
		/// <remarks>Previouly bookInfo.AudioLangsToPublish.ForBloomLibrary</remarks>
		[JsonProperty("audioLangs")]
		public Dictionary<string, InclusionSetting> AudioLangs;

		/// <summary>
		/// The sign language(s) -- currently we allow only one -- which the user wants to publish
		/// </summary>
		/// <remarks>Previouly bookInfo.SignLangsToPublish.ForBloomLibrary</remarks>
		[JsonProperty("signLangs")]
		public Dictionary<string, InclusionSetting> SignLangs;

		// For now, the audio language selection is all or nothing for Bloom Library publish
		[JsonIgnore]
		public bool IncludeAudio => AudioLangs.Any(al => al.Value.IsIncluded());

		// Whether to advertise the book as a comic book (if it has any comic pages)
		[JsonProperty("comic")]
		public bool Comic;
	}
}
