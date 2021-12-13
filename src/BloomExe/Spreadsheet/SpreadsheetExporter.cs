using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.web;
using L10NSharp;
using SIL.IO;
using SIL.Xml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Security;
using System.Windows.Forms;
using System.Xml;

namespace Bloom.Spreadsheet
{
	public class SpreadsheetExporter
	{
		InternalSpreadsheet _spreadsheet = new InternalSpreadsheet();
		private IWebSocketProgress _progress;
		private BloomWebSocketServer _webSocketServer;
		private string _outputFolder; // null if not exporting to folder (mainly some unit tests)
		private string _outputImageFolder; // null if not exporting to folder (mainly some unit tests)

		private ILanguageDisplayNameResolver LangDisplayNameResolver { get; set; }


		public delegate SpreadsheetExporter Factory();

		/// <summary>
		/// Constructs a new Spreadsheet Exporter
		/// </summary>
		/// <param name="webSocketServer">The webSockerServer of the instance</param>
		/// <param name="langDisplayNameResolver">The object that will be used to  retrieve the language display names</param>
		public SpreadsheetExporter(BloomWebSocketServer webSocketServer, ILanguageDisplayNameResolver langDisplayNameResolver)
		{
			_webSocketServer = webSocketServer;
			LangDisplayNameResolver = langDisplayNameResolver;
		}

		/// <summary>
		/// Constructs a new Spreadsheet Exporter
		/// </summary>
		/// <param name="webSocketServer">The webSockerServer of the instance</param>
		/// <param name="collectionSettings">The collectionSettings of the book that will be exported. This is used to retrieve the language display names</param>
		public SpreadsheetExporter(BloomWebSocketServer webSocketServer, CollectionSettings collectionSettings)
			:this(webSocketServer, new CollectionSettingsLanguageDisplayNameResolver(collectionSettings))
		{
		}

		public SpreadsheetExporter(ILanguageDisplayNameResolver langDisplayNameResolver)
		{
			Debug.Assert(Bloom.Program.RunningUnitTests,
				"SpreadsheetExporter should be passed a webSocketProgress unless running unit tests that don't need it");

			LangDisplayNameResolver = langDisplayNameResolver;
		}

		//a list of values which, if they occur in the data-book attribute of an element in the bloomDataDiv,
		//indicate that the element content should be treated as an image, even though the element doesn't
		//have a src attribute nor actually contain an img element
		public static List<string> DataDivImagesWithNoSrcAttributes = new List<string>() { "licenseImage" };

		public void ExportToFolderWithProgress(HtmlDom dom, string imagesFolderPath, string outputFolder,
			Action<string> resultCallback)
		{
			var mainShell = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			BrowserProgressDialog.DoWorkWithProgressDialog(_webSocketServer, "spreadsheet-export", () =>
				new ReactDialog("progressDialogBundle",
						// props to send to the react component
						new
						{
							title = "Exporting Spreadsheet",
							titleIcon = "", // enhance: add icon if wanted
							titleColor = "white",
							titleBackgroundColor = Palette.kBloomBlueHex,
							webSocketContext = "spreadsheet-export",
							showReportButton = "if-error"
						}, "Export Spreadsheet")
					// winforms dialog properties
					{ Width = 620, Height = 550 }, (progress, worker) =>
			{
				var spreadsheet = ExportToFolder(dom, imagesFolderPath, outputFolder, out string outputFilePath,
					progress);
				resultCallback(outputFilePath);
				return progress.HaveProblemsBeenReported;
			}, null, mainShell);
		}

		public SpreadsheetExportParams Params = new SpreadsheetExportParams();

		public InternalSpreadsheet Export(HtmlDom dom, string imagesFolderPath, IWebSocketProgress progress = null)
		{
			_progress = progress ?? new NullWebSocketProgress();
			_spreadsheet.Params = Params;
			var pages = dom.GetPageElements();

			//Get xmatter
			var dataDiv = GetDataDiv(dom);
			AddDataDivData(dataDiv, imagesFolderPath);

			var iContentPage = 0;
			foreach (var page in pages)
			{
				var pageNumber = page.Attributes["data-page-number"]?.Value ?? "";
				// For now we will ignore all un-numbered pages, particularly xmatter,
				// which was handled above by exporting data div data.
				if (pageNumber == "")
					continue;

				//Each page alternates colors
				var colorForPage = iContentPage++ % 2 == 0 ? InternalSpreadsheet.AlternatingRowsColor1 : InternalSpreadsheet.AlternatingRowsColor2;
				AddContentRows(page, pageNumber, imagesFolderPath, colorForPage);
			}
			_spreadsheet.SortHiddenContentRowsToTheBottom();
			return _spreadsheet;
		}

		private void AddContentRows(XmlElement page, string pageNumber, string imagesFolderPath, Color colorForPage)
		{
			var imageContainers = GetImageContainers(page);
			var groups = TranslationGroupManager.SortedGroupsOnPage(page, true);
						
			var pageContentTuples = imageContainers.MapUnevenPairs(groups, (imageContainer, group) => (imageContainer, group));
			foreach(var pageContent in pageContentTuples)
			{
				var row = new ContentRow(_spreadsheet);
				row.SetCell(InternalSpreadsheet.RowTypeColumnLabel, InternalSpreadsheet.PageContentRowLabel);
				row.SetCell(InternalSpreadsheet.PageNumberColumnLabel, pageNumber);

				if (pageContent.imageContainer != null)
				{
					var image = (XmlElement)pageContent.imageContainer.SafeSelectNodes(".//img").Item(0);
					var imagePath = ImagePath(imagesFolderPath, image.GetAttribute("src"));
					row.SetCell(InternalSpreadsheet.ImageSourceColumnLabel, Path.Combine("images", Path.GetFileName(imagePath)));
					CopyImageFileToSpreadsheetFolder(imagePath);
				}

				if (pageContent.group != null)
				{
					foreach (var editable in pageContent.group.SafeSelectNodes("./*[contains(@class, 'bloom-editable')]").Cast<XmlElement>())
					{
						var langCode = editable.Attributes["lang"]?.Value ?? "";
						if (langCode == "z" || langCode == "")
							continue;
						var index = GetOrAddColumnForLang(langCode);
						var content = editable.InnerXml;
						// Don't just test content, it typically contains paragraph markup.
						if (String.IsNullOrWhiteSpace(editable.InnerText))
						{
							content = InternalSpreadsheet.BlankContentIndicator;
						}
						row.SetCell(index, content);
					}
				}

				row.BackgroundColor = colorForPage;
			}
		}

		private XmlElement GetDataDiv(HtmlDom elementOrDom)
		{
			return elementOrDom.SafeSelectNodes(".//div[@id='bloomDataDiv']").Cast<XmlElement>().First();
		}

		private XmlElement[] GetImageContainers(XmlElement elementOrDom)
		{
			return elementOrDom.SafeSelectNodes(".//*[contains(@class,'bloom-imageContainer')]").Cast<XmlElement>()
				.ToArray();
		}

		private string ImagePath(string imagesFolderPath, string imageSrc)
		{
			return Path.Combine(imagesFolderPath, UrlPathString.CreateFromUrlEncodedString(imageSrc).NotEncoded);
		}

		/// <summary>
		/// Get the column for a language. If no column exists, one will be added
		/// </summary>
		/// <remarks>If the column does not exist it will be added.
		/// The friendly name used for the column will be the display name for that language according to {this.LangDisplayNameResolver}</remarks>
		/// If the column already exists, its index will be returned. The column, including the column friendly name, will not be modified
		/// <param name="langCode">The language code to look up, as specified in the header</param>
		/// <returns>The index of the column</returns>
		private int GetOrAddColumnForLang(string langCode)
		{
			// Check if a column already exists for this column
			var colIndex = _spreadsheet.GetOptionalColumnForLang(langCode);
			if (colIndex >= 0)
			{
				return colIndex;
			}

			// Doesn't exist yet. Let's add a column for it.
			var langFriendlyName = LangDisplayNameResolver.GetLanguageDisplayName(langCode);
			return _spreadsheet.AddColumnForLang(langCode, langFriendlyName);
		}

		private void AddDataDivData(XmlNode node, string imagesFolderPath)
		{
			var dataBookNodeList = node.SafeSelectNodes("./div[@data-book]").Cast<XmlElement>().ToList();
			//Bring the ones with the same data-book value together so we can easily make a single row for each data-book value
			dataBookNodeList.Sort((a, b) => a.GetAttribute("data-book").CompareTo(b.GetAttribute("data-book")));
			string prevDataBookLabel = null;
			SpreadsheetRow row = null;
			foreach (XmlElement dataBookElement in dataBookNodeList)
			{
				var langCode = dataBookElement.GetAttribute("lang");
				if (langCode == "z")
				{
					continue;
				}

				var dataBookLabel = dataBookElement.GetAttribute("data-book");

				//The first time we see this tag:
				if (!dataBookLabel.Equals(prevDataBookLabel))
				{
					row = new ContentRow(_spreadsheet);
					var label = "[" + dataBookLabel.Trim() + "]";
					if (label != InternalSpreadsheet.BookTitleRowLabel && label != InternalSpreadsheet.CoverImageRowLabel)
						row.Hidden = true;
					row.SetCell(InternalSpreadsheet.RowTypeColumnLabel, label);

					var imageSrcAttribute = dataBookElement.GetAttribute("src").Trim();

					if (IsDataDivImageElement(dataBookElement, dataBookLabel))
					{
						if (imageSrcAttribute.Length > 0
						    && dataBookElement.InnerText.Trim().Length > 0
						    && !imageSrcAttribute.Equals(dataBookElement.InnerText.Trim()))
						{
							//Some data-book items redundantly store the src of the image which they capture in both their content and
							//src attribute. We haven't yet found any case in which they are different, so are only storing one in the
							//spreadsheet. This test is to make sure that we notice if we come across a case where it might be necessary
							//to save both.
							_progress.MessageWithParams("Spreadsheet.DataDivConflictWarning", "",
								"Export warning: Found differing 'src' attribute and element text for data-div element {0}. The 'src' attribute will be ignored.",
								ProgressKind.Warning, dataBookLabel);
						}

						string imageSource;
						string childSrc = ChildImgElementSrc(dataBookElement);
						if (childSrc.Length > 0)
						{
							// We've lost track of what was 'incomplete' about our handling of data-book elements
							// that have an image child and don't have branding in their key. But the message
							// was a nuisance. Keeping the code in case it reminds us of a problem at some point.
							//if (! dataBookElement.GetAttribute("data-book").Contains("branding"))
							//{
							//	var msg = LocalizationManager.GetString("Spreadsheet:DataDivNonBrandingImageElment",
							//		"Export warning: Found a non-branding image in an <img> element for " + dataBookLabel
							//		+ ". This is not fully handled yet.");
							//	NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
							//}
							// Don't think we ever have data-book elements with more than one image. But if we encounter one,
							// I think it's worth warning the user that we don't handle it.
							if (dataBookElement.ChildNodes
								    .Cast<XmlNode>().Count(n =>
									    n.Name == "img" && string.IsNullOrEmpty(((XmlElement)n).GetAttribute("src"))) >
							    1)
							{
								_progress.MessageWithParams("Spreadsheet.MultipleImageChildren", "",
									"Export warning: Found multiple images in data-book element {0}. Only the first will be exported.",
									ProgressKind.Warning, dataBookLabel);
							}

							imageSource = childSrc;
						}
						else
						{
							//We determined that whether or not a data-book div has a src attribute, it is the innerText
							//of the item that is used to set the src of the image in the actual pages of the document.
							//So that's what we want to capture in the spreadsheet.
							imageSource = dataBookElement.InnerText.Trim();
						}

						row.SetCell(InternalSpreadsheet.ImageSourceColumnLabel, Path.Combine("images", imageSource));
						CopyImageFileToSpreadsheetFolder(ImagePath(imagesFolderPath, imageSource));
						prevDataBookLabel = dataBookLabel;
						continue;
					}
				}

				if (IsDataDivImageElement(dataBookElement, dataBookLabel))
				{
					_progress.MessageWithParams("Spreadsheet.DataDivImageMultiple", "",
						"Export warning: Found multiple elements for image element {0}. Only the first will be exported.",
						ProgressKind.Warning, dataBookLabel);
					continue;
				}

				var colIndex = GetOrAddColumnForLang(langCode);
				row.SetCell(colIndex, dataBookElement.InnerXml.Trim());
				prevDataBookLabel = dataBookLabel;
			}
		}

		private void CopyImageFileToSpreadsheetFolder(string imageSourcePath)
		{
			if (_outputImageFolder != null)
			{
				if (!RobustFile.Exists(imageSourcePath))
				{
					_progress.MessageWithParams("Spreadsheet.MissingImage", "",
						"Export warning: did not find the image {0}. It will be missing from the export folder.",
						ProgressKind.Warning, imageSourcePath);
					return;
				}

				var destPath = Path.Combine(_outputImageFolder, Path.GetFileName(imageSourcePath));
				RobustFile.Copy(imageSourcePath, destPath, true);
			}
		}

		private bool IsDataDivImageElement(XmlElement dataBookElement, string dataBookLabel)
		{
			var imageSrc = dataBookElement.GetAttribute("src").Trim();
			//Unfortunately, in the current state of Bloom, we have at least three ways of representing in the bloomDataDiv things that are
			//images in the main document.Some can be identified by having a src attribute on the data-book element itself. Some actually contain
			//an img element. And some don't have any identifying mark at all, so to recognize them we just have to hard-code a list.
			return imageSrc.Length > 0
			       || ChildImgElementSrc(dataBookElement).Length > 0
			       || DataDivImagesWithNoSrcAttributes.Contains(dataBookLabel);
		}

		private string ChildImgElementSrc(XmlElement node)
		{
			foreach (XmlNode childNode in node.ChildNodes)
			{
				if (childNode.Name.Equals("img") && ((XmlElement)childNode).HasAttribute("src"))
				{
					return ((XmlElement)childNode).GetAttribute("src");
				}
			}

			return "";
		}

		/// <summary>
		/// Output the specified DOM to the specified outputFolder (after deleting any existing content, if
		/// permitted...depends on overwrite param and possibly user input).
		/// Returns the intermediate spreadsheet object created, and also outputs the path to the xlsx file created.
		/// Looks for images in the specified imagesFolderPath (typically the book folder) and copies them to an
		/// images subdirectory of the outputFolder.
		/// Currently the xlsx file created will have the same name as the outputFolder, typically copied from
		/// the input book folder.
		/// <returns>the internal spreadsheet, or null if not permitted to overwrite.</returns>
		/// </summary>
		public InternalSpreadsheet ExportToFolder(HtmlDom dom, string imagesFolderPath, string outputFolder,
			out string outputPath,
			IWebSocketProgress progress = null, OverwriteOptions overwrite = OverwriteOptions.Ask)
		{
			outputPath = Path.Combine(outputFolder,
				Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(outputFolder) + ".xlsx"));
			_outputFolder = outputFolder;
			_outputImageFolder = Path.Combine(_outputFolder, "images");
			try
			{
				if (Directory.Exists(outputFolder))
				{
					if (overwrite == OverwriteOptions.Quit)
					{
						// I'm assuming someone working with a command-line can cope with English.
						// Don't think it's worth cluttering the XLF with this.
						Console.WriteLine($"Output folder ({_outputFolder}) exists. Use --overwrite to overwrite.");
						outputPath = null;
						return null;
					}

					var appearsToBeBloomBookFolder = Directory.EnumerateFiles(outputFolder, "*.htm").Any();
					var msgTemplate = LocalizationManager.GetString("Spreadsheet.Overwrite",
						"You are about to replace the existing folder named {0}");
					var msg = string.Format(msgTemplate, outputFolder);
					var messageBoxButtons = new[]
					{
						new MessageBoxButton() { Text = "Overwrite", Id = "overwrite" },
						new MessageBoxButton() { Text = "Cancel", Id = "cancel", Default = true }
					};
					if (appearsToBeBloomBookFolder)
					{
						if (overwrite == OverwriteOptions.Overwrite)
						{
							// Assume we can't UI in this mode. But we absolutely must not overwrite the book folder!
							// So quit anyway.
							Console.WriteLine(
								$"Output folder ({_outputFolder}) exists and appears to be a Bloom book, not a previous export. If you really mean to export there, you'll have to delete the folder first.");
							outputPath = null;
							return null;
						}

						msgTemplate = LocalizationManager.GetString("Spreadsheet.OverwriteBook",
							"The folder named {0} already exists and looks like it might be a Bloom book folder!");
						msg = string.Format(msgTemplate, outputFolder);
						messageBoxButtons = new[] { messageBoxButtons[1] }; // only cancel
					}

					if (overwrite == OverwriteOptions.Ask)
					{
						var formToInvokeOn = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
						string result = null;
						formToInvokeOn.Invoke((Action)(() =>
						{
							result = BloomMessageBox.Show(formToInvokeOn, msg, messageBoxButtons,
								MessageBoxIcon.Warning);
						}));
						if (result != "overwrite")
						{
							outputPath = null;
							return null;
						}
					} // if it's not Ask, at this point it must be Overwrite, so go ahead.
				}

				// In case there's a previous export, get rid of it.
				SIL.IO.RobustIO.DeleteDirectoryAndContents(_outputFolder);
				Directory.CreateDirectory(_outputImageFolder); // also (re-)creates its parent, outputFolder
				var spreadsheet = Export(dom, imagesFolderPath, progress);
				spreadsheet.WriteToFile(outputPath, progress);
				return spreadsheet;
			}
			catch (Exception e) when (e is IOException || e is SecurityException || e is UnauthorizedAccessException)
			{
				progress.MessageWithParams("Spreadsheet.WriteFailed", "",
					"Bloom had problems writing files to that location ({0}). Check that you have permission to write there.",
					ProgressKind.Error, _outputFolder);
			}

			outputPath = null;
			return null; // some error occurred and was caught
		}
	}

	public enum OverwriteOptions
	{
		Overwrite,
		Quit,
		Ask
	}

	/// <summary>
	/// An interface for SpreadsheetExporter to be able to convert language ISO codes to their display names.
	/// This allows unit tests to use mocks to handle this functionality instead of figuring out how to construct a concrete resolver
	/// </summary>
	public interface ILanguageDisplayNameResolver
	{
		/// <summary>
		/// Given a language code, returns the friendly name of that language (according to the dictionary passed into the constructor)
		/// </summary>
		/// <param name="langCode"></param>
		/// <returns>Returns the friendly name if available. If not, returns the language code unchanged.</returns>
		string GetLanguageDisplayName(string langCode);
	}

	/// <summary>
	/// Resolves language codes to language display names based on the book's CollectionSettings
	/// </summary>
	class CollectionSettingsLanguageDisplayNameResolver : ILanguageDisplayNameResolver
	{
		private CollectionSettings CollectionSettings;
		public CollectionSettingsLanguageDisplayNameResolver(CollectionSettings collectionSettings)
		{
			this.CollectionSettings = collectionSettings;
		}

		public string GetLanguageDisplayName(string langCode)
		{
			return this.CollectionSettings.GetDisplayNameForLanguage(langCode);
		}
	}

	// Note: You can also resolve these from the book.BookInfo.MetaData.DisplayNames dictionary, but
	// that seems to have fewer entries than CollectionSetting's or BookData's GetDisplayNameForLanguage() function
}
