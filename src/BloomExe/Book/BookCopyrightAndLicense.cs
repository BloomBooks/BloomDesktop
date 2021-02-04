using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Xml;
using Bloom.Collection;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Text;
using SIL.Windows.Forms.ClearShare;

namespace Bloom.Book
{
	/// <summary>
	/// Reads and writes the aspects of the book related to copyright, license, license logo, etc.
	/// That involves three duties:
	/// 1) Serializing/Deserializing a libpalaso.ClearShare.Metadata to/from the bloomDataDiv of the html
	/// 2) Propagating that information into template fields found in the pages of the book (normally just the credits page)
	/// 3) Placing the correct license image into the folder
	/// </summary>
	public class BookCopyrightAndLicense
	{
		/// <summary>
		/// Create a Clearshare.Metadata object by reading values out of the dom's bloomDataDiv
		/// </summary>
		public static Metadata GetMetadata(HtmlDom dom, BookData bookData)
		{
			if (ShouldSetToDefaultCopyrightAndLicense(dom))
			{
				return GetMetadataWithDefaultCopyrightAndLicense();
			}
			return CreateMetadata(dom.GetBookSetting("copyright"), GetLicenseUrl(dom), dom.GetBookSetting("licenseNotes"), bookData);
		}

		public static Metadata GetOriginalMetadata(HtmlDom dom, BookData bookData)
		{
			return CreateMetadata(dom.GetBookSetting("originalCopyright"), dom.GetBookSetting("originalLicenseUrl").GetExactAlternative("*"), dom.GetBookSetting("originalLicenseNotes"), bookData);
		}

		public static Metadata CreateMetadata(MultiTextBase copyright, string licenseUrl, MultiTextBase licenseNotes, BookData bookData)
		{
			var metadata = new Metadata();
			if (!copyright.Empty)
			{
				metadata.CopyrightNotice = GetBestMultiTextBaseValue(copyright, bookData);
			}

			if (string.IsNullOrWhiteSpace(licenseUrl))
			{
				//NB: we are mapping "RightsStatement" (which comes from XMP-dc:Rights) to "LicenseNotes" in the html.
				//custom licenses live in this field, so if we have notes (and no URL) it is a custom one.
				if (!licenseNotes.Empty)
				{
					metadata.License = new CustomLicense { RightsStatement = GetBestMultiTextBaseValue(licenseNotes, bookData) };
				}
				else
				{
					// The only remaining current option is a NullLicense
					metadata.License = new NullLicense(); //"contact the copyright owner
				}
			}
			else // there is a licenseUrl, which means it is a CC license
			{
				try
				{
					metadata.License = CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
				}
				catch (IndexOutOfRangeException)
				{
					// Need to handle urls which do not end with the version number.
					// Simply set it to the default version.
					if (!licenseUrl.EndsWith("/"))
						licenseUrl += "/";
					licenseUrl += CreativeCommonsLicense.kDefaultVersion;
					metadata.License = CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
				}
				catch (Exception e)
				{
					throw new ApplicationException("Bloom had trouble parsing this license url: '" + licenseUrl + "'. (ref BL-4108)", e);
				}
				//are there notes that go along with that?
				if (!licenseNotes.Empty)
				{
					metadata.License.RightsStatement = GetBestMultiTextBaseValue(licenseNotes, bookData);
				}
			}
			return metadata;
		}

		private static string GetBestMultiTextBaseValue(MultiTextBase multiTextBase, BookData bookData)
		{
			string alternative = multiTextBase.GetFirstAlternative();

			if (bookData != null)
			{
				var langs = new List<string>();
				langs.AddRange(bookData.GetAllBookLanguageCodes());
				langs.Add("*");
				langs.Add("en");
				var bestAltString = multiTextBase.GetBestAlternativeString(langs);
				if (!string.IsNullOrEmpty(bestAltString))
					alternative = bestAltString;
			}

			return DecodeAlternative(alternative);
		}

		private static string DecodeAlternative(string alternative)
		{
			return HtmlDom.ConvertHtmlBreaksToNewLines(WebUtility.HtmlDecode(alternative));
		}

		public static string GetLicenseUrl(HtmlDom dom)
		{
			return dom.GetBookSetting("licenseUrl").GetBestAlternativeString(new[] { "*", "en" });
		}

		private static Metadata GetMetadataWithDefaultCopyrightAndLicense()
		{
			var metadata = new Metadata();
			Logger.WriteEvent("For BL-3166 Investigation: GetMetadata() setting to default license");
			metadata.License = new CreativeCommonsLicense(true, true, CreativeCommonsLicense.DerivativeRules.Derivatives);
			return metadata;
		}

		/// <summary>
		/// Call this when we have a new set of metadata to use. It
		/// 1) sets the bloomDataDiv with the data,
		/// 2) causes any template fields in the book to get the new values
		/// 3) updates the license image on disk
		/// </summary>
		public static void SetMetadata(Metadata metadata, HtmlDom dom, string bookFolderPath, BookData bookData,
			bool useOriginalCopyright)
		{
			dom.SetBookSetting("copyright","*", ConvertNewLinesToHtmlBreaks(metadata.CopyrightNotice));
			dom.SetBookSetting("licenseUrl","*",metadata.License.Url);
			// This is for backwards compatibility. The book may have  licenseUrl in 'en' created by an earlier version of Bloom.
			// For backwards compatibility, GetMetaData will read that if it doesn't find a '*' license first. So now that we're
			// setting a licenseUrl for '*', we must make sure the 'en' one is gone, because if we're setting a non-CC license,
			// the new URL will be empty and the '*' one will go away, possibly exposing the 'en' one to be used by mistake.
			// See BL-3166.
			dom.SetBookSetting("licenseUrl", "en", null);
			string languageUsedForDescription;

			//This part is unfortunate... the license description, which is always localized, doesn't belong in the datadiv; it
			//could instead just be generated when we update the page. However, for backwards compatibility (prior to 3.6),
			//we localize it and place it in the datadiv.
			dom.RemoveBookSetting("licenseDescription");
			var description = metadata.License.GetDescription(bookData.GetLanguagePrioritiesForLocalizedTextOnPage(), out languageUsedForDescription);
			dom.SetBookSetting("licenseDescription", languageUsedForDescription, ConvertNewLinesToHtmlBreaks(description));

			// Book may have old licenseNotes, typically in 'en'. This can certainly show up again if licenseNotes in '*' is removed,
			// and maybe anyway. Safest to remove it altogether if we are setting it using the new scheme.
			dom.RemoveBookSetting("licenseNotes");
			dom.SetBookSetting("licenseNotes", "*", ConvertNewLinesToHtmlBreaks(metadata.License.RightsStatement));

			// we could do away with licenseImage in the bloomDataDiv, since the name is always the same, but we keep it for backward compatibility
			if (metadata.License is CreativeCommonsLicense)
			{
				dom.SetBookSetting("licenseImage", "*", "license.png");
			}
			else
			{
				//CC licenses are the only ones we know how to show an image for
				dom.RemoveBookSetting("licenseImage");
			}

			UpdateDomFromDataDiv(dom, bookFolderPath, bookData, useOriginalCopyright);
		}

		private static string ConvertNewLinesToHtmlBreaks(string s)
		{
			return string.IsNullOrEmpty(s) ? s : s.Replace("\r", "").Replace("\n", "<br/>");
		}

		/// <summary>
		/// Propagating the copyright and license information in the bloomDataDiv to template fields
		/// found in the pages of the book (normally just the credits page).
		/// </summary>
		/// <remarks>This is "internal" just as a convention, that it is accessible for testing purposes only</remarks>
		internal static void UpdateDomFromDataDiv(HtmlDom dom, string bookFolderPath, BookData bookData, bool useOriginalCopyright)
		{
			CopyItemToFieldsInPages(dom, "copyright");
			CopyItemToFieldsInPages(dom, "licenseUrl");
			CopyItemToFieldsInPages(dom, "licenseDescription", languagePreferences:bookData.GetLanguagePrioritiesForLocalizedTextOnPage().ToArray());
			CopyItemToFieldsInPages(dom, "licenseNotes");
			CopyItemToFieldsInPages(dom, "licenseImage", valueAttribute:"src");
			// If we're using the original copyright, we don't need to show it separately.
			// See https://issues.bloomlibrary.org/youtrack/issue/BL-7381.
			CopyStringToFieldsInPages(dom, "originalCopyrightAndLicense",
				useOriginalCopyright ? null : GetOriginalCopyrightAndLicenseNotice(bookData, dom), "*");

			if (!String.IsNullOrEmpty(bookFolderPath)) //unit tests may not be interested in checking this part
				UpdateBookLicenseIcon(GetMetadata(dom, bookData), bookFolderPath);
		}

		private static void CopyStringToFieldsInPages(HtmlDom dom, string key, string val, string lang)
		{
			foreach (XmlElement target in dom.SafeSelectNodes("//*[@data-derived='" + key + "']"))
			{
				if (target == null) // don't think this can happen, but something like it seemed to in one test...
					continue;
				if (string.IsNullOrEmpty(val))
				{
					target.RemoveAttribute("lang");
					target.InnerText = "";
				}
				else
				{
					HtmlDom.SetElementFromUserStringSafely(target, val);
					target.SetAttribute("lang", lang);
				}
			}
		}

		private static void CopyItemToFieldsInPages(HtmlDom dom, string key, string valueAttribute = null, string[] languagePreferences= null)
		{
			if (languagePreferences == null)
				languagePreferences = new[] {"*", "en"};

            MultiTextBase source = dom.GetBookSetting(key);

			if (key == "copyright")
			{
				// For CC0, we store the "copyright", but don't display it in the text of the book.
				var licenseUrl = dom.GetBookSetting("licenseUrl").GetExactAlternative("*");
				if (licenseUrl == CreativeCommonsLicense.CC0Url)
					source = new MultiTextBase();
			}

			foreach (XmlElement target in dom.SafeSelectNodes("//*[@data-derived='" + key + "']"))
			{
				//just put value into the text of the element
				if (string.IsNullOrEmpty(valueAttribute))
				{
					//clear out what's there now
					target.RemoveAttribute("lang");
					target.InnerText = "";

					var form = source.GetBestAlternative(languagePreferences);
					if (form != null && !string.IsNullOrWhiteSpace(form.Form))
					{
						// HtmlDom.GetBookSetting(key) returns the result of XmlNode.InnerXml which will be Html encoded (&amp; &lt; etc).
						// HtmlDom.SetElementFromUserStringPreservingLineBreaks() calls XmlNode.InnerText, which Html encodes if necessary.
						// So we need to decode here to prevent double encoding.  See http://issues.bloomlibrary.org/youtrack/issue/BL-4585.
						// Note that HtmlDom.SetElementFromUserStringPreservingLineBreaks() handles embedded <br/> elements, but makes no
						// effort to handle p or div elements.
						var decoded = System.Web.HttpUtility.HtmlDecode(form.Form);
						HtmlDom.SetElementFromUserStringSafely(target, decoded);
						target.SetAttribute("lang", form.WritingSystemId); //this allows us to set the font to suit the language
					}
				}
				else //Put the value into an attribute. The license image goes through this path.
				{
					target.SetAttribute(valueAttribute, source.GetBestAlternativeString(languagePreferences));
					if (source.Empty)
					{
						//if the license image is empty, make sure we don't have some alternative text
						//about the image being missing or slow to load
						target.SetAttribute("alt", "");
						//over in javascript land, @alt will get set appropriately when the image url is not empty.
					}
				}
			}
		}

		/// <summary>
		/// Get the license from the metadata and save it.
		/// </summary>
		private static void UpdateBookLicenseIcon(Metadata metadata, string bookFolderPath)
		{
			var licenseImage = metadata.License.GetImage();
			var imagePath = bookFolderPath.CombineForPath("license.png");
			// Don't try to overwrite the license image for a template book.  (See BL-3284.)
			if (RobustFile.Exists(imagePath) && BloomFileLocator.IsInstalledFileOrDirectory(imagePath))
				return;
			try
			{
				if(licenseImage != null)
				{
					using(Stream fs = new FileStream(imagePath, FileMode.Create))
					{
						RobustImageIO.SaveImage(licenseImage, fs, ImageFormat.Png);
					}
				}
				else
				{
					if(RobustFile.Exists(imagePath))
						RobustFile.Delete(imagePath);
				}
			}
			catch(Exception error)
			{
				// BL-3227 Occasionally get The process cannot access the file '...\license.png' because it is being used by another process.
				// That's worth a toast, since the user might like a hint why the license image isn't up to date.
				// However, if the problem is a MISSING icon in the installed templates, which on Linux or if an allUsers install
				// the system will never let us write, is not worth bothering the user at all. We can't fix it. Too bad.
				if (BloomFileLocator.IsInstalledFileOrDirectory(imagePath))
					return;
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Could not update license image (BL-3227).", "Image was at" +imagePath, exception: error);
			}
		}

		public static void RemoveLicense(BookStorage storage)
		{
			storage.Dom.RemoveBookSetting("licenseUrl");
			storage.Dom.RemoveBookSetting("licenseDescription");
			storage.Dom.RemoveBookSetting("licenseNotes");
		}

		public static IEnumerable<string> SettingsToCheckForDefaultCopyright => new[] {"copyright", "licenseUrl", "licenseNotes"};

		private static bool ShouldSetToDefaultCopyrightAndLicense(HtmlDom dom)
		{
			//Enhance: this logic is perhaps overly restrictive?
			foreach (var setting in SettingsToCheckForDefaultCopyright)
			{
				if (!dom.GetBookSetting(setting).Empty)
					return false;
			}
			return true;
		}

		public static void LogMetdata(HtmlDom dom)
		{
			Logger.WriteEvent("LicenseUrl: " + dom.GetBookSetting("licenseUrl"));
			Logger.WriteEvent("LicenseNotes: " + dom.GetBookSetting("licenseNotes"));
			Logger.WriteEvent("");
		}

		public static bool IsDerivative(Metadata originalMetadata)
		{
			// Checking for a license which is not a NullLicense is not sufficient because that indicates the user has selected
			// "Contact the copyright holder..." for the license. But in order to do so, he must have entered a copyright.
			return !String.IsNullOrEmpty(originalMetadata.CopyrightNotice) || !(originalMetadata.License is NullLicense);
		}

		internal static string GetOriginalCopyrightAndLicenseNotice(BookData bookData, HtmlDom dom)
		{
			var originalMetadata = GetOriginalMetadata(dom, bookData);

			// As of BL-7898, we are using the existence of an original copyright/license to determine if we are working with a derivative.
			if (!IsDerivative(originalMetadata))
				return null;

			var languagePriorityIds = bookData.GetLanguagePrioritiesForLocalizedTextOnPage(true);

			var license = originalMetadata.License.GetMinimalFormForCredits(languagePriorityIds, out _);
			string originalLicenseSentence;
			languagePriorityIds = bookData.GetLanguagePrioritiesForLocalizedTextOnPage(false);
			// The originalTitle strategy used here is not ideal. We would prefer to have a placeholder specifically for it
			// in both EditTab.FrontMatter.OriginalCopyrightSentence and EditTab.FrontMatter.OriginalHadNoCopyrightSentence.
			// But we don't want to require a new set of translations if we can avoid it.
			string encodedTitle = dom.GetBookSetting("originalTitle")?.GetExactAlternative("*");
			string originalTitle = HttpUtility.HtmlDecode(encodedTitle);

			// Used when we insert into the no-copyright string, typically "Adapted from an original with no copyright"
			var originalTitleBeforePeriod = ", <cite data-book=\"originalTitle\">" + originalTitle + "</cite>";
			// Used when we insert into the usual string, typicall "Adapted from original, {0}" ahead of the copyright and license
			// as part of the {0} replacement.
			var originalTitleAfterComma = "<cite data-book=\"originalTitle\">" + originalTitle + "</cite>, ";
			if (string.IsNullOrEmpty(originalTitle))
			{
				// We need to add the "missingOriginalTitle class.
				originalTitleBeforePeriod = ", <cite data-book=\"originalTitle\" class=\"missingOriginalTitle\"></cite>";
				originalTitleAfterComma = "<cite data-book=\"originalTitle\" class=\"missingOriginalTitle\"></cite>, ";
			}
			if (originalMetadata.License is CustomLicense)
			{
				// I can imagine being more fancy... something like "Licensed under custom license:", and get localizations
				// for that... but sheesh, these are even now very rare in Bloom-land and should become more rare.
				// So for now, let's just print the custom license contents.
				originalLicenseSentence = license;
			}
			else
			{
				var licenseSentenceTemplate = LocalizationManager.GetString("EditTab.FrontMatter.OriginalLicenseSentence",
					"Licensed under {0}.",
					"On the Credits page of a book being translated, Bloom puts texts like 'Licensed under CC-BY', so that we have a record of what the license was for the original book. Put {0} in the translation, where the license should go in the sentence.",
					languagePriorityIds, out _);
				originalLicenseSentence = string.IsNullOrWhiteSpace(license) ? "" : string.Format(licenseSentenceTemplate, license);
				originalLicenseSentence = originalLicenseSentence.Replace("..", "."); // in case had notes which also had a period.
			}

			string copyrightNotice;
			if (string.IsNullOrWhiteSpace(originalMetadata.CopyrightNotice))
			{
				var noCopyrightSentence = LocalizationManager.GetString("EditTab.FrontMatter.OriginalHadNoCopyrightSentence",
					"Adapted from original without a copyright notice.",
					"On the Credits page of a book being translated, Bloom shows this if the original book did not have a copyright notice.",
					languagePriorityIds, out _);

					noCopyrightSentence = noCopyrightSentence.Substring(0, noCopyrightSentence.Length - 1) +
					                      originalTitleBeforePeriod + ".";

					copyrightNotice = noCopyrightSentence + " " + originalLicenseSentence;
			}
			else
			{
				var originalCopyrightSentence = LocalizationManager.GetString("EditTab.FrontMatter.OriginalCopyrightSentence",
					"Adapted from original, {0}.",
					"On the Credits page of a book being translated, Bloom shows the original copyright. Put {0} in the translation where the copyright notice should go. For example in English, 'Adapted from original, {0}.' comes out like 'Adapted from original, Copyright 2011 SIL'.",
					languagePriorityIds, out _);
				copyrightNotice = String.Format(originalCopyrightSentence, originalTitleAfterComma + originalMetadata.CopyrightNotice.Trim()) + " " +
				                  originalLicenseSentence;
			}

			return copyrightNotice.Trim();
		}
	}
}
