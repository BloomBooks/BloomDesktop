using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Bloom.Api;
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
		/// <param name="brandingNameOrFolderPath"> Normally, the branding is just a name, which we look up in the official branding folder
		//but unit tests can instead provide a path to the folder.
		/// </param>
		public static Metadata GetMetadata(HtmlDom dom, string brandingNameOrFolderPath = "")
		{
			if (ShouldSetToDefaultCopyrightAndLicense(dom))
			{
				return GetMetadataWithDefaultCopyrightAndLicense(brandingNameOrFolderPath);
			}
			return CreateMetadata(dom.GetBookSetting("copyright"), GetLicenseUrl(dom), dom.GetBookSetting("licenseNotes"));
		}

		public static Metadata GetOriginalMetadata(HtmlDom dom, string brandingNameOrFolderPath = "")
		{
			return CreateMetadata(dom.GetBookSetting("originalCopyright"), dom.GetBookSetting("originalLicenseUrl").GetExactAlternative("*"), dom.GetBookSetting("originalLicenseNotes"));
		}

		public static Metadata CreateMetadata(MultiTextBase copyright, string licenseUrl, MultiTextBase licenseNotes)
		{
			var metadata = new Metadata();
			if (!copyright.Empty)
			{
				metadata.CopyrightNotice = WebUtility.HtmlDecode(copyright.GetFirstAlternative());
			}

			if (string.IsNullOrWhiteSpace(licenseUrl))
			{
				//NB: we are mapping "RightsStatement" (which comes from XMP-dc:Rights) to "LicenseNotes" in the html.
				//custom licenses live in this field, so if we have notes (and no URL) it is a custom one.
				if (!licenseNotes.Empty)
				{
					metadata.License = new CustomLicense {RightsStatement = WebUtility.HtmlDecode(licenseNotes.GetFirstAlternative())};
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
					var s = WebUtility.HtmlDecode(licenseNotes.GetFirstAlternative());
					metadata.License.RightsStatement = HtmlDom.ConvertHtmlBreaksToNewLines(s);
				}
			}
			return metadata;
		}

		private static string GetLicenseUrl(HtmlDom dom)
		{
			return dom.GetBookSetting("licenseUrl").GetBestAlternativeString(new[] { "*", "en" });
		}

		private static Metadata GetMetadataWithDefaultCopyrightAndLicense(string brandingNameOrPath)
		{
			var metadata = new Metadata();
			Logger.WriteEvent("For BL-3166 Investigation: GetMetadata() setting to default license");
			metadata.License = new CreativeCommonsLicense(true, true, CreativeCommonsLicense.DerivativeRules.Derivatives);

			//OK, that's all we need, the rest is blank. That is, unless we are we are working with a brand
			//that has declared some defaults in a settings.json file:
			var settings = BrandingApi.GetSettings(brandingNameOrPath);
			if(settings != null)
			{
				if(!string.IsNullOrEmpty(settings.CopyrightNotice))
				{
					metadata.CopyrightNotice = settings.CopyrightNotice;
				}
				if(!string.IsNullOrEmpty(settings.LicenseUrl))
				{
					metadata.License = CreativeCommonsLicense.FromLicenseUrl(settings.LicenseUrl);
				}
				if(!string.IsNullOrEmpty(settings.LicenseUrl))
				{
					metadata.License.RightsStatement = settings.LicenseRightsStatement;
				}
			}
			return metadata;
		}

		/// <summary>
		/// Call this when we have a new set of metadata to use. It
		/// 1) sets the bloomDataDiv with the data,
		/// 2) causes any template fields in the book to get the new values
		/// 3) updates the license image on disk
		/// </summary>
		public static void SetMetadata(Metadata metadata, HtmlDom dom, string bookFolderPath, CollectionSettings collectionSettings)
		{
			dom.SetBookSetting("copyright","*",metadata.CopyrightNotice);
			dom.SetBookSetting("licenseUrl","*",metadata.License.Url);
			// This is for backwards compatibility. The book may have  licenseUrl in 'en' created by an earlier version of Bloom.
			// For backwards compatibiilty, GetMetaData will read that if it doesn't find a '*' license first. So now that we're
			// setting a licenseUrl for '*', we must make sure the 'en' one is gone, because if we're setting a non-CC license,
			// the new URL will be empty and the '*' one will go away, possibly exposing the 'en' one to be used by mistake.
			// See BL-3166.
			dom.SetBookSetting("licenseUrl", "en", null);
			string languageUsedForDescription;

			//This part is unfortunate... the license description, which is always localized, doesn't belong in the datadiv; it
			//could instead just be generated when we update the page. However, for backwards compatibility (prior to 3.6),
			//we localize it and place it in the datadiv.
			dom.RemoveBookSetting("licenseDescription");
			var description = metadata.License.GetDescription(collectionSettings.LicenseDescriptionLanguagePriorities, out languageUsedForDescription);
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

			UpdateDomFromDataDiv(dom, bookFolderPath, collectionSettings);
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
		internal static void UpdateDomFromDataDiv(HtmlDom dom, string bookFolderPath, CollectionSettings collectionSettings)
		{
			CopyItemToFieldsInPages(dom, "copyright");
			CopyItemToFieldsInPages(dom, "licenseUrl");
			CopyItemToFieldsInPages(dom, "licenseDescription", languagePreferences:collectionSettings.LicenseDescriptionLanguagePriorities.ToArray());
			CopyItemToFieldsInPages(dom, "licenseNotes");
			CopyItemToFieldsInPages(dom, "licenseImage", valueAttribute:"src");
			CopyStringToFieldsInPages(dom, "originalCopyrightAndLicense", GetOriginalCopyrightAndLicenseNotice(collectionSettings, dom), "*");

			if (!String.IsNullOrEmpty(bookFolderPath)) //unit tests may not be interested in checking this part
				UpdateBookLicenseIcon(GetMetadata(dom), bookFolderPath);
		}

		private static void CopyStringToFieldsInPages(HtmlDom dom, string key, string val, string lang)
		{
			var target = dom.SelectSingleNode("//*[@data-derived='" + key + "']");
			if (target == null)
				return;
			if (string.IsNullOrEmpty(val))
			{
				target.RemoveAttribute("lang");
				target.InnerText = "";
			}
			else
			{
				HtmlDom.SetElementFromUserStringPreservingLineBreaks(target, val);
				target.SetAttribute("lang", lang);
			}
		}

		private static void CopyItemToFieldsInPages(HtmlDom dom, string key, string valueAttribute = null, string[] languagePreferences= null)
		{
			if (languagePreferences == null)
				languagePreferences = new[] {"*", "en"};

            MultiTextBase source = dom.GetBookSetting(key);

			var target = dom.SelectSingleNode("//*[@data-derived='" + key + "']");
			if (target == null)
			{
				return;
			}


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
					HtmlDom.SetElementFromUserStringPreservingLineBreaks(target, decoded);
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
						SIL.IO.RobustIO.SaveImage(licenseImage, fs, ImageFormat.Png);
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

		private static bool ShouldSetToDefaultCopyrightAndLicense(HtmlDom dom)
		{
			var hasCopyright = !dom.GetBookSetting("copyright").Empty;
			var hasLicenseUrl = !dom.GetBookSetting("licenseUrl").Empty;
			var hasLicenseNotes = !dom.GetBookSetting("licenseNotes").Empty;

			//Enhance: this logic is perhaps overly restrictive?
			return !hasCopyright && !hasLicenseUrl && !hasLicenseNotes;
		}

		public static void LogMetdata(HtmlDom dom)
		{
			Logger.WriteEvent("LicenseUrl: " + dom.GetBookSetting("licenseUrl"));
			Logger.WriteEvent("LicenseNotes: " + dom.GetBookSetting("licenseNotes"));
			Logger.WriteEvent("");
		}

		/// <summary>
		/// Copy the copyright & license info to the originalCopyrightAndLicense,
		/// then remove the copyright so the translator can put in their own if they
		/// want. We retain the license, but the translator is allowed to change that.
		/// If the source is already a translation (already has original copyright or license)
		/// we keep them unchanged.
		/// </summary>
		public static void SetOriginalCopyrightAndLicense(HtmlDom dom, BookData bookData, CollectionSettings collectionSettings)
		{
			// At least one of these should exist if the source was a derivative, since we don't allow a
			// book to have no license, nor to be uploaded without copyright...unless of course it was derived
			// before 3.9, when we started doing this. In that case the best we can do is record the earliest
			// information we have for this and later adaptations.
			if (bookData.GetMultiTextVariableOrEmpty("originalLicenseUrl").Count > 0
				|| bookData.GetMultiTextVariableOrEmpty("originalLicenseNotes").Count > 0
				|| bookData.GetMultiTextVariableOrEmpty("originalCopyright").Count > 0)
			{
				return; //leave the original there.
			}
			bookData.Set("originalLicenseUrl", GetLicenseUrl(dom), "*");
			bookData.Set("originalCopyright", System.Web.HttpUtility.HtmlEncode(BookCopyrightAndLicense.GetMetadata(dom).CopyrightNotice), "*");
			bookData.Set("originalLicenseNotes", dom.GetBookSetting("licenseNotes").GetFirstAlternative(), "*");
			bookData.RemoveAllForms("copyright");  // RemoveAllForms does modify the dom
		}

		internal static string GetOriginalCopyrightAndLicenseNotice(CollectionSettings collectionSettings, HtmlDom dom)
		{
			return GetOriginalCopyrightAndLicenseNotice(collectionSettings, GetOriginalMetadata(dom));
		}
		private static string GetOriginalCopyrightAndLicenseNotice(CollectionSettings collectionSettings, Metadata metadata)
		{
			string idOfLanguageUsed;
			var languagePriorityIds = collectionSettings.LicenseDescriptionLanguagePriorities;

			//TODO HOW DO I GET THESE IN THE NATIONAL LANGUAGE INSTEAD OF THE UI LANGUAGE?

			var license = metadata.License.GetMinimalFormForCredits(languagePriorityIds, out idOfLanguageUsed);
			string originalLicenseSentence;
			var preferredLanguageIds = new[] {collectionSettings.Language2Iso639Code, LocalizationManager.UILanguageId, "en"};
			if (metadata.License is CustomLicense)
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
					preferredLanguageIds, out idOfLanguageUsed);
				originalLicenseSentence = string.IsNullOrWhiteSpace(license) ? "" : string.Format(licenseSentenceTemplate, license);
				originalLicenseSentence = originalLicenseSentence.Replace("..", "."); // in case had notes which also had a period.
			}

			var copyrightNotice = "";
			if (string.IsNullOrWhiteSpace(metadata.CopyrightNotice))
			{
				var noCopyrightSentence = LocalizationManager.GetString("EditTab.FrontMatter.OriginalHadNoCopyrightSentence",
					"Adapted from original without a copyright notice.",
					"On the Credits page of a book being translated, Bloom shows this if the original book did not have a copyright notice.",
					preferredLanguageIds, out idOfLanguageUsed);

				copyrightNotice = noCopyrightSentence + " " + originalLicenseSentence;
			}
			else
			{
				var originalCopyrightSentence = LocalizationManager.GetString("EditTab.FrontMatter.OriginalCopyrightSentence",
					"Adapted from original, {0}.",
					"On the Credits page of a book being translated, Bloom shows the original copyright. Put {0} in the translation where the copyright notice should go. For example in English, 'Adapted from original, {0}.' comes out like 'Adapted from original, Copyright 2011 SIL'.",
					preferredLanguageIds, out idOfLanguageUsed);
				copyrightNotice = String.Format(originalCopyrightSentence, metadata.CopyrightNotice.Trim()) + " " +
				                  originalLicenseSentence;
			}

			// The copyright string has to be encoded because it's fed eventually into the XmlNode.InnerXml method, and some people
			// like to use & in their copyright notices.  metaData.CopyrightNotice is not Html encoded, so it can give us bare &s.
			// See http://issues.bloomlibrary.org/youtrack/issue/BL-4585.
			return copyrightNotice.Trim();
		}
	}
}
