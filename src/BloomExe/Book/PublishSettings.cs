using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Publish;
using Bloom.Utils;
using Newtonsoft.Json;
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
			BloomPub = new BloomPubSettings();
			// I'd really like to not have to mess with null testing by initializing
			// all of these here, but considerable pre-existing logic is based on
			//testing whether they are null.
			//{
			//	TextLangs = new Dictionary<string, InclusionSetting>(),
			//	AudioLangs = new Dictionary<string, InclusionSetting>(),
			//	SignLangs = new Dictionary<string, InclusionSetting>()
			//};
			Epub = new EpubSettings();
			BloomLibrary = new BloomLibrarySettings();
			//{
			//	TextLangs = new Dictionary<string, InclusionSetting>(),
			//	AudioLangs = new Dictionary<string, InclusionSetting>(),
			//	SignLangs = new Dictionary<string, InclusionSetting>()
			//};
		}

		[JsonProperty("audioVideo")] public AudioVideoSettings AudioVideo { get; set; }

		[JsonProperty("epub")] public EpubSettings Epub { get; set; }

		[JsonProperty("bloomPUB")] public BloomPubSettings BloomPub { get; set; }

		[JsonProperty("bloomLibrary")] public BloomLibrarySettings BloomLibrary { get; set; }

		public static PublishSettings FromString(string input)
		{
			var result = JsonConvert.DeserializeObject<PublishSettings>(input);
			if (result == null)
			{
				throw new ApplicationException("publish-settings of this book may be corrupt");
			}
			return result;
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
			if (!RobustFile.Exists(publishSettingsPath))
			{
				return MigrateSettings(bookFolderPath);
			}
			if (TryReadSettings(publishSettingsPath, out PublishSettings result))
				return result;

			// We could implement a backup strategy like for MetaData, but I don't
			// think it's worth it. It's not that likely we will lose these, or very critical
			// if we do.
			return new PublishSettings();
		}

		static PublishSettings MigrateSettings(string bookFolderPath)
		{
			// See if we can migrate data from an old metaData.
			var metaDataPath = BookMetaData.MetaDataPath(bookFolderPath);
			var settings = new PublishSettings();
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
				if (metaDataJson.TryGet("features", out string[] features)) {
					if (features != null && features.Any(f => f == "motion"))
					{
						settings.BloomPub.Motion = true;
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
				result.AudioVideo.FixEmptyValues();
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
			FixEmptyValues();
		}

		public void FixEmptyValues()
		{
			if (Format == null)
				Format = "facebook";
			if (PageTurnDelay == 0)
				PageTurnDelay = 3000;
			if (PlayerSettings == null)
				PlayerSettings = "";
		}

		/// <summary>
		/// How long to display pages with no narration (milliseconds)
		/// </summary>
		[JsonProperty("pageTurnDelay")]
		public int PageTurnDelay { get; set; }

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
		public string Format { get; set; }

		/// <summary>
		/// Whether to record the book in landscape mode with full-screen pictures and
		/// animations. Closely related to the BloomPubSettings field of the same name,
		/// but a video can be made of reading the book with text visible and highlighted
		/// and no animations.
		/// </summary>
		[JsonProperty("motion")]
		public bool Motion { get; set; }

		/// <summary>
		/// A string containing the video player settings that are handled entirely by
		/// BloomPlayer. The preview instance of BloomPlayer sends this string when a relevant
		/// control is operated, and the same string is sent to the Recording instance.
		/// Currently it is a URL-encoded JSON string representing the language chosen for
		/// playback and whether to play any image description narration, but the design
		/// is intended to allow more relevant controls to be added to BP without changing
		/// Bloom Editor.
		/// One downside is that quotes in the PlayerSettings JSON must be escaped in the
		/// file. We think is is worth this to keep the content of this string opaque to BE.
		/// </summary>
		[JsonProperty("playerSettings")]
		public string PlayerSettings { get; set; }
	}

	/// <summary>
	/// Settings used by the Android (BloomPUB) publish tab.
	/// Currently incomplete; others should move here eventually.
	/// </summary>
	public class BloomPubSettings
	{
		// Whether to publish the book as a motion book, that can be rotated
		// horizontal to trigger autoplay with animations. Currently this mirrors
		// the Motion feature (and the Feature_Motion property of BookInfo) but here
		// the focus is on storing the publish setting rather than on listing book
		// features in the library.
		[JsonProperty("motion")]
		public bool Motion { get; set; }

		/// <summary>
		/// This corresponds to the checkbox values of which languages the user wants to publish the text for.
		/// </summary>
		/// <remarks>Previouly bookInfo.TextLangsToPublish.ForBloomPUB</remarks>
		[JsonProperty("textLangs")]
		public Dictionary<string, InclusionSetting> TextLangs { get; set; }

		/// <summary>
		/// This corresponds to the checkbox values of which languages the user wants to publish the audio for
		/// </summary>
		/// <remarks>Previouly bookInfo.AudioLangsToPublish.ForBloomPUB</remarks>
		[JsonProperty("audioLangs")]
		public Dictionary<string, InclusionSetting> AudioLangs { get; set; }

		/// <summary>
		/// The sign language(s) -- currently we allow only one -- which the user wants to publish
		/// </summary>
		/// <remarks>Previouly bookInfo.SignLangsToPublish.ForBloomPUB</remarks>
		[JsonProperty("signLangs")]
		public Dictionary<string, InclusionSetting> SignLangs { get; set; }
	}

	public class EpubSettings
	{
		/// <summary>
		/// This item indicates how the user would like Epubs of this book to handle Image Descriptions
		/// Current possibilities are 'None' and 'OnPage'; previous value 'Links' became obsolete in Bloom 4.6.
		/// </summary>
		[JsonProperty("howToPublishImageDescriptions")]
		public BookInfo.HowToPublishImageDescriptions HowToPublishImageDescriptions;

		/// <summary>
		/// This corresponds to a checkbox indicating that the user wants to use the eReader's native font sizes.
		/// </summary>
		/// <remarks>Replaces the old BookMetaData property Epub_RemoveFontSizes, which was (unfortunately)
		/// persisted as epub_RemoveFontStyles</remarks>
		[JsonProperty("removeFontSizes")]
		public bool RemoveFontSizes;
	}

	public class BloomLibrarySettings
	{
		/// <summary>
		/// This corresponds to the checkbox values of which languages the user wants to publish the text for.
		/// </summary>
		/// <remarks>Previouly bookInfo.TextLangsToPublish.ForBloomLibrary</remarks>
		[JsonProperty("textLangs")]
		public Dictionary<string, InclusionSetting> TextLangs { get; set; }

		/// <summary>
		/// This corresponds to the checkbox values of which languages the user wants to publish the audio for
		/// </summary>
		/// <remarks>Previouly bookInfo.AudioLangsToPublish.ForBloomLibrary</remarks>
		[JsonProperty("audioLangs")]
		public Dictionary<string, InclusionSetting> AudioLangs { get; set; }

		/// <summary>
		/// The sign language(s) -- currently we allow only one -- which the user wants to publish
		/// </summary>
		/// <remarks>Previouly bookInfo.SignLangsToPublish.ForBloomLibrary</remarks>
		[JsonProperty("signLangs")]
		public Dictionary<string, InclusionSetting> SignLangs { get; set; }

		// For now, the audio language selection is all or nothing for Bloom Library publish
		[JsonIgnore]
		public bool IncludeAudio => AudioLangs.Any(al => al.Value.IsIncluded());
	}
}
