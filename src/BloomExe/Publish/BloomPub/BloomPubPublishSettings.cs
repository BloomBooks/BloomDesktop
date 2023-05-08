using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Book;
using Bloom.Publish.BloomLibrary;

namespace Bloom.Publish.BloomPub
{
	// This class is used to pass settings from the PublishToBloomPubApi (or PublishToVideoApi, etc) to the many and varied
	// places that they pass through before getting to BloomPubMaker.
	public class BloomPubPublishSettings
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

		public BloomPubPublishSettings()
		{
			ImagePublishSettings = new ImagePublishSettings();
			LanguagesToInclude = new HashSet<string>();
			AudioLanguagesToExclude = new HashSet<string>();
		}

		// Should we publish as a motion book?
		// Note: rather than a default of false, this should normally be set to the PublishSettings.BloomPub.Motion
		// value stored in the book's BookInfo. This happens automatically if creating one using ForBloomInfo.
		// If you want a different value, for example, AudioVideo.Settings, be sure to set that up.
		public bool PublishAsMotionBookIfApplicable;

		public ImagePublishSettings ImagePublishSettings { get; set; }

		public override bool Equals(object obj)
		{
			if (!(obj is BloomPubPublishSettings))
				return false;
			var other = (BloomPubPublishSettings)obj;
			return LanguagesToInclude.SetEquals(other.LanguagesToInclude) && DistributionTag == other.DistributionTag
				&& PublishAsMotionBookIfApplicable == other.PublishAsMotionBookIfApplicable;

			// REVIEW: why wasn't AudioLanguagesToExclude included here?
		}

		/// <summary>
		/// Return a clone, except that all languages in the passed set are wanted
		/// (and no audio languages excluded).
		/// </summary>
		/// <param name="languages"></param>
		/// <returns></returns>
		public BloomPubPublishSettings WithAllLanguages(IEnumerable<string> languages)
		{
			var result = this.MemberwiseClone() as BloomPubPublishSettings;
			result.AudioLanguagesToExclude = new HashSet<string>(); // exclude none!
			result.LanguagesToInclude = new HashSet<string>(languages);
			return result;
		}

		public override int GetHashCode()
		{
			return LanguagesToInclude.GetHashCode() + DistributionTag.GetHashCode() + (PublishAsMotionBookIfApplicable? 1 : 0);

			// REVIEW: why wasn't AudioLanguagesToExclude included here?
		}

		// BL-10840 When the Harvester is getting BloomPubPublishSettings, we want to use the settings
		// for BloomLibrary, since the book has been uploaded using those settings for text and audio
		// languages. (But, BL-11582, separate BloomPub settings are obsolete, so always use the Library ones,
		// unless they are missing.)
		// Much more complicated before we made BloomPub language settings obsolete and BloomLibrary settings
		// never null; see BL-11582 and before that BL-10840.
		private static HashSet<string> GetLanguagesToInclude(PublishSettings settings)
		{
			var dictToUse = settings.BloomLibrary.TextLangs;

			ThrowIfLanguagesNotInitialized(dictToUse);

			return new HashSet<string>(dictToUse.Where(kvp => kvp.Value.IsIncluded()).Select(kvp => kvp.Key));
		}

		// Get the set of languages in libraryDict that are excluded. (This was more complicated before we
		// made BloomPub language settings obsolete. In particular, if we reinstate those, this function
		// still needs to yield library language settings when harvesting; see BL-10840.
		private static HashSet<string> GetLanguagesToExclude(Dictionary<string, InclusionSetting> libraryDict)
		{
			ThrowIfLanguagesNotInitialized(libraryDict);
			return new HashSet<string>(libraryDict.Where(kvp => !kvp.Value.IsIncluded()).Select(kvp => kvp.Key));
		}

		private static void ThrowIfLanguagesNotInitialized(Dictionary<string, InclusionSetting> langDictionary)
		{
			if (langDictionary.Count == 0)
			{
				// This should never happen. If we are running from the UI, we will have initialized things
				// when going into the publish screen. If we are running from the command line, we will
				// have called GetPublishSettingsForBook which guarantees we are properly initialized.
				throw new ApplicationException("Trying to use PublishSettings languages which have not been initialized");
			}
		}

		public static BloomPubPublishSettings FromBookInfo(BookInfo bookInfo)
		{
			var libraryTextLangs = bookInfo.PublishSettings.BloomLibrary.TextLangs;
			var libraryAudioLangs = bookInfo.PublishSettings.BloomLibrary.AudioLangs;
			var languagesToInclude = GetLanguagesToInclude(bookInfo.PublishSettings);

			HashSet<string> audioLanguagesToExclude;
			if (libraryAudioLangs.Count == 0)
			{
				if (libraryTextLangs.Count == 0)
				{
					// We really want to exclude all of them, but we don't know what all the possibilities are.
					audioLanguagesToExclude = new HashSet<string>();
				}
				else
				{
					// We want to exclude the audio files for the languages that we are not publishing the text of.
					// We aren't sure if we need this, or if AudioLangs is only null when there is no audio in the book at all.
					audioLanguagesToExclude = GetLanguagesToExclude(libraryTextLangs);
				}
			}
			else
			{
				// We do have some settings for the audio languages, choose the ones that have been explicitly marked as excluded
				audioLanguagesToExclude = GetLanguagesToExclude(libraryAudioLangs);
			}

			return new BloomPubPublishSettings()
			{
				// Note - we want it such that even if the underlying data changes, this settings object won't.
				// (Converting the IEnumerable to a HashSet above happens to accomplish that)
				LanguagesToInclude = languagesToInclude,
				AudioLanguagesToExclude = audioLanguagesToExclude,
				// All the paths that use this are making settings for BloomPub, not Video.
				PublishAsMotionBookIfApplicable = bookInfo.PublishSettings.BloomPub.PublishAsMotionBookIfApplicable,
				ImagePublishSettings = bookInfo.PublishSettings.BloomPub.ImageSettings
			};
		}

		public static BloomPubPublishSettings GetPublishSettingsForBook(BookServer bookServer, BookInfo bookInfo)
		{
			// Normally this is setup by the Publish (or library) screen, but if you've never visited such a screen for this book,
			// perhaps because you are doing a bulk or command line publish, then one of them might be empty. In that case, initialize it here.
			if (bookInfo.PublishSettings.BloomLibrary.TextLangs.Count == 0 || bookInfo.PublishSettings.BloomLibrary.AudioLangs.Count == 0)
			{
				var book = bookServer.GetBookFromBookInfo(bookInfo);
				BloomLibraryPublishModel.InitializeLanguages(book);
			}
			return FromBookInfo(bookInfo);
		}
	}
}
