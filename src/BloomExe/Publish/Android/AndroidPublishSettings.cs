using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bloom.Book;

namespace Bloom.Publish.Android
{
	// This class is used to pass settings from the PublishToAndroidApi to the many and varied
	// places that they pass through before getting to BloomReaderFileMaker.
	// Although there is only one setting as yet, we will in future be able to add more by
	// just extending this class.
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

		// Specifies the languages for which narration audio should not be included, even if their text is include
		// NOTE: It's more natural for consumers to think about what languages they want to EXCLUDE, rather than what languages they want to INCLUDE
		public HashSet<string> AudioLanguagesToExclude;

		public override bool Equals(object obj)
		{
			if (!(obj is AndroidPublishSettings))
				return false;
			var other = (AndroidPublishSettings) obj;
			return LanguagesToInclude.SetEquals(other.LanguagesToInclude) && DistributionTag == other.DistributionTag;

			// REVIEW: why wasn't AudioLanguagesToExclude included here?
		}

		public override int GetHashCode()
		{
			return LanguagesToInclude.GetHashCode() + DistributionTag.GetHashCode();

			// REVIEW: why wasn't AudioLanguagesToExclude included here?
		}

		public static AndroidPublishSettings FromBookInfo(BookInfo bookInfo)
		{
			var languagesToInclude = bookInfo.MetaData.TextLangsToPublish != null
				? new HashSet<string>(bookInfo.MetaData.TextLangsToPublish.ForBloomPUB
					.Where(kvp => kvp.Value.IsIncluded()).Select(kvp => kvp.Key))
				: new HashSet<string>();

			HashSet<string> audioLanguagesToExclude;
			if (bookInfo.MetaData.AudioLangsToPublish == null)
			{
				if (bookInfo.MetaData.TextLangsToPublish == null)
				{
					// We really want to exclude all of them, but we don't know what all the possibilities are.
					audioLanguagesToExclude = new HashSet<string>();
				}
				else
				{
					// We want to exclude the audio files for the languages that we are not publishing the text of.
					// We aren't sure if we need this, or if AudioLangsToPublish is only null when there is no audio in the book at all.
					audioLanguagesToExclude = new HashSet<string>(bookInfo.MetaData.TextLangsToPublish.ForBloomPUB
						.Where(kvp => !kvp.Value.IsIncluded()).Select(kvp => kvp.Key));
				}
			}
			else
			{
				// We do have some settings for the audio languages, choose the ones that have been explicitly marked as excluded
				audioLanguagesToExclude = new HashSet<string>(bookInfo.MetaData.AudioLangsToPublish.ForBloomPUB
					.Where(kvp => !kvp.Value.IsIncluded()).Select(kvp => kvp.Key));
			}

			return new AndroidPublishSettings()
			{
				// Note - we want it such that even if the underlying data changes, this settings object won't.
				// (Converting the IEnumerable to a HashSet above happens to accomplish that)
				LanguagesToInclude = languagesToInclude,
				AudioLanguagesToExclude = audioLanguagesToExclude
			};
		}

		public static AndroidPublishSettings GetPublishSettingsForBook(BookServer bookServer, BookInfo bookInfo)
		{
			// Normally this is setup by the Publish screen, but if you've never visited the Publish screen for this book,
			// then this will be null. In that case, initialize it here.
			if (bookInfo.MetaData.TextLangsToPublish == null)
			{
				var book = bookServer.GetBookFromBookInfo(bookInfo);
				var allLanguages = book.AllPublishableLanguages(includeLangsOccurringOnlyInXmatter: true);
				PublishToAndroidApi.InitializeLanguagesInBook(bookInfo, allLanguages, book.CollectionSettings);
			}
			return FromBookInfo(bookInfo);
		}

	}
}
