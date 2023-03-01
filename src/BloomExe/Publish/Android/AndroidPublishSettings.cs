using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Book;

namespace Bloom.Publish.Android
{
	// This class is used to pass settings from the PublishToAndroidApi (or PublishToVideoApi, etc) to the many and varied
	// places that they pass through before getting to BloomReaderFileMaker.
	public class AndroidPublishSettings
	{
		// A distribution tag goes into analytics as a way of measuring the impact of various distribution efforts. E.g.,
		// sd cards vs. web vs. distributed devices. Note, while we store this in the meta.json so that we don't lose it,
		// in the actual BloomPub we also create a .distribution file containing it. That's a bit confusing, and maybe it
		// is largely for historical reasons (client had to do this themselves, and creating a text file is easier than editing json).
		public string DistributionTag;

		public string BookshelfTag;

		// Specifies the languages whose text should be included in the published book.
		public HashSet<string> LanguagesToInclude;

		// Specifies the languages for which narration audio should not be included, even if their text is included.
		// NOTE: It's more natural for consumers to think about what languages they want to EXCLUDE, rather than what languages they want to INCLUDE.
		public HashSet<string> AudioLanguagesToExclude;

		public string[] AudioLanguagesToInclude
		{
			get
			{
				if (LanguagesToInclude == null)
					return Array.Empty<string>();
				if (AudioLanguagesToExclude == null)
					return LanguagesToInclude.ToArray();
				return LanguagesToInclude.Except(AudioLanguagesToExclude).ToArray();
			}
		}

		// Triggers both not deleting them...they are harmless when making a video...
		// and sending a list of them over a web socket.
		public bool WantPageLabels;

		// True to remove activities, quiz pages...stuff that's inappropriate for making videos.
		public bool RemoveInteractivePages;

		public AndroidPublishSettings()
		{
			ImagePublishSettings = new ImagePublishSettings();
			LanguagesToInclude = new HashSet<string>();
			AudioLanguagesToExclude = new HashSet<string>();
		}

		// Should we publish as a motion book?
		// Note: rather than a default of false, this should normally be set to the PublishSettings.BloomPub.Motion
		// value stored in the book's BookInfo. This happens automatically if creating one using ForBloomInfo.
		// If you want a different value, for example, AudioVideo.Settings, be sure to set that up.
		public bool Motion;

		public ImagePublishSettings ImagePublishSettings { get; set; }

		public override bool Equals(object obj)
		{
			if (!(obj is AndroidPublishSettings))
				return false;
			var other = (AndroidPublishSettings) obj;
			return LanguagesToInclude.SetEquals(other.LanguagesToInclude) && DistributionTag == other.DistributionTag
				&& Motion == other.Motion;

			// REVIEW: why wasn't AudioLanguagesToExclude included here?
		}

		/// <summary>
		/// Return a clone, except that all languages in the passed set are wanted
		/// (and no audio languages excluded).
		/// </summary>
		/// <param name="languages"></param>
		/// <returns></returns>
		public AndroidPublishSettings WithAllLanguages(IEnumerable<string> languages)
		{
			var result = this.MemberwiseClone() as AndroidPublishSettings;
			result.AudioLanguagesToExclude = new HashSet<string>(); // exclude none!
			result.LanguagesToInclude = new HashSet<string>(languages);
			return result;
		}

		public override int GetHashCode()
		{
			return LanguagesToInclude.GetHashCode() + DistributionTag.GetHashCode() + (Motion? 1 : 0);

			// REVIEW: why wasn't AudioLanguagesToExclude included here?
		}

		// BL-10840 When the Harvester is getting AndroidPublishSettings, we want to use the settings
		// for BloomLibrary, since the book has been uploaded using those settings for text and audio
		// languages.
		private static HashSet<string> GetLanguagesToInclude(PublishSettings settings)
		{
			var dictToUse = Program.RunningHarvesterMode ? settings.BloomLibrary.TextLangs : settings.BloomPub.TextLangs;
			// The following problem can happen if running in Harvester and 'ForBloomLibrary' isn't set up.
			// So just use 'ForBloomPUB', which should be set up.
			if (dictToUse == null)
				dictToUse = settings.BloomPub.TextLangs;
			// If it's still null, bail.
			if (dictToUse == null)
				return new HashSet<string>();
			return new HashSet<string>(dictToUse.Where(kvp => kvp.Value.IsIncluded()).Select(kvp => kvp.Key));

		}

		// BL-10840 When the Harvester is getting AndroidPublishSettings, we want to use the settings
		// for BloomLibrary, since the book has been uploaded using those settings for text and audio
		// languages.
		private static HashSet<string> GetLanguagesToExclude(Dictionary<string, InclusionSetting> bloomPubDict, Dictionary<string, InclusionSetting> libraryDict)
		{
			var dictToUse = Program.RunningHarvesterMode ? libraryDict : bloomPubDict;
			// The following problem can happen if running in Harvester and 'ForBloomLibrary' isn't set up.
			// So just use 'ForBloomPUB', which should be set up.
			if (dictToUse == null)
				dictToUse = bloomPubDict;
			// If it's still null, bail.
			if (dictToUse == null)
				return new HashSet<string>();
			return new HashSet<string>(dictToUse.Where(kvp => !kvp.Value.IsIncluded()).Select(kvp =>kvp.Key));
		}

		public static AndroidPublishSettings FromBookInfo(BookInfo bookInfo)
		{
			var bloomPubTextLangs = bookInfo.PublishSettings.BloomPub.TextLangs;
			var libraryTextLangs = bookInfo.PublishSettings.BloomLibrary.TextLangs;
			var bloomPubAudioLangs = bookInfo.PublishSettings.BloomPub.AudioLangs;
			var libraryAudioLangs = bookInfo.PublishSettings.BloomLibrary.AudioLangs;
			var languagesToInclude = GetLanguagesToInclude(bookInfo.PublishSettings);

			HashSet<string> audioLanguagesToExclude;
			if (bloomPubAudioLangs.Count == 0 && libraryAudioLangs.Count == 0)
			{
				if (bloomPubTextLangs.Count == 0 && libraryTextLangs.Count == 0)
				{
					// We really want to exclude all of them, but we don't know what all the possibilities are.
					audioLanguagesToExclude = new HashSet<string>();
				}
				else
				{
					// We want to exclude the audio files for the languages that we are not publishing the text of.
					// We aren't sure if we need this, or if AudioLangs is only null when there is no audio in the book at all.
					audioLanguagesToExclude = GetLanguagesToExclude(bloomPubTextLangs, libraryTextLangs);
				}
			}
			else
			{
				// We do have some settings for the audio languages, choose the ones that have been explicitly marked as excluded
				audioLanguagesToExclude = GetLanguagesToExclude(bloomPubAudioLangs, libraryAudioLangs);
			}

			return new AndroidPublishSettings()
			{
				// Note - we want it such that even if the underlying data changes, this settings object won't.
				// (Converting the IEnumerable to a HashSet above happens to accomplish that)
				LanguagesToInclude = languagesToInclude,
				AudioLanguagesToExclude = audioLanguagesToExclude,
				// All the paths that use this are making settings for BloomPub, not Video.
				Motion = bookInfo.PublishSettings.BloomPub.Motion,
				ImagePublishSettings = bookInfo.PublishSettings.BloomPub.ImageSettings
			};
		}

		public static AndroidPublishSettings GetPublishSettingsForBook(BookServer bookServer, BookInfo bookInfo)
		{
			// Normally this is setup by the Publish screen, but if you've never visited the Publish screen for this book,
			// then this will be empty. In that case, initialize it here.
			if (bookInfo.PublishSettings.BloomPub.TextLangs.Count == 0) // Review Feb 2022: previously it would be null, now count==0. Not exactly the same, since you could say "no text languages"?
			{
				var book = bookServer.GetBookFromBookInfo(bookInfo);
				var allLanguages = book.AllPublishableLanguages(includeLangsOccurringOnlyInXmatter: true);
				PublishToAndroidApi.InitializeLanguagesInBook(bookInfo, allLanguages, book.CollectionSettings);
			}
			return FromBookInfo(bookInfo);
		}

	}
}
