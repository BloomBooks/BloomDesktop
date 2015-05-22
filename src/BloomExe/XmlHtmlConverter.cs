using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using NuGet;
using Palaso.Xml;
using TidyManaged;
using Palaso.IO;


namespace Bloom
{
	public class XmlHtmlConverter
	{
		/// <summary>
		///
		/// </summary>
		/// <param name="content"></param>
		/// <exception cref="">Throws if there are parsing errors</exception>
		/// <returns></returns>
		public static XmlDocument GetXmlDomFromHtmlFile(string path, bool includeXmlDeclaration = false)
		{
			return GetXmlDomFromHtml(File.ReadAllText(path), includeXmlDeclaration);
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="content"></param>
		/// <param name="includeXmlDeclaration"></param>
		/// <exception cref="">Throws if there are parsing errors</exception>
		/// <returns></returns>
		public static XmlDocument GetXmlDomFromHtml(string content, bool includeXmlDeclaration = false)
		{
			var dom = new XmlDocument();
			content = AddFillerToKeepTidyFromRemovingEmptyElements(content);

			//in BL-2250, we had a report of these REMOVEWHITEPACE's hanging around. It's conceivable that they got in there 
			//from some previous release that maybe had bug, but with the version in the screenshot that they provided, 
			//we could not reproduce. So there is some doubt. Meanwhile, we needed to give them a way to clean up existing books.

			content = content.Replace(@"REMOVEWHITESPACE", "");

			// It also likes to insert newlines before <b>, <u>, and <i>, and convert any existing whitespace
			// there to a space.
			content = content.Replace(@"<b>", "REMOVEWHITESPACE<b>");
			content = content.Replace(@"<i>", "REMOVEWHITESPACE<i>");
			content = content.Replace(@"<u>", "REMOVEWHITESPACE<u>");

			// fix for <br></br> tag doubling
			content = content.Replace("<br></br>", "<br />");

			//using (var temp = new TempFile())
			var temp = new TempFile();
			{
				File.WriteAllText(temp.Path, content, Encoding.UTF8);
				using (var tidy = TidyManaged.Document.FromFile(temp.Path))
				{
					tidy.ShowWarnings = false;
					tidy.Quiet = true;
					tidy.WrapAt = 0; // prevents textarea wrapping.
					tidy.AddTidyMetaElement = false;
					tidy.OutputXml = true;
					tidy.CharacterEncoding = EncodingType.Utf8;
					tidy.InputCharacterEncoding = EncodingType.Utf8;
					tidy.OutputCharacterEncoding = EncodingType.Utf8;
					tidy.DocType = DocTypeMode.Omit; //when it supports html5, then we will let it out it
					//maybe try this? tidy.Markup = true;

					tidy.AddXmlDeclaration = includeXmlDeclaration;

					//NB: this does not prevent tidy from deleting <span data-libray='somethingImportant'></span>
					tidy.MergeSpans = AutoBool.No;
					tidy.DropEmptyParagraphs = false;
					tidy.MergeDivs = AutoBool.No;


					var errors = tidy.CleanAndRepair();
					if (!string.IsNullOrEmpty(errors))
					{
						throw new ApplicationException(string.Format("{0}{2}{2}{1}", errors, content, Environment.NewLine));
					}
					var newContents = tidy.Save();
					try
					{
						newContents = RemoveFillerInEmptyElements(newContents);

						newContents = newContents.Replace("&nbsp;", "&#160;");
						//REVIEW: 1) are there others? &amp; and such are fine.  2) shoul we to convert back to &nbsp; on save?

						// The regex here is mainly for the \s as a convenient way to remove whatever whitespace TIDY
						// has inserted. It's a fringe benefit that we can use the[bi] to deal with both elements in one replace.
						newContents = Regex.Replace(newContents, @"REMOVEWHITESPACE\s*\<([biu])\>", "<$1>");

						Debug.Assert(!newContents.Contains("REMOVEWHITESPACE"), "BL-2250 Reproduction");

						// remove blank lines at the end of style blocks
						newContents = Regex.Replace(newContents, @"\s+\<\/style\>", "</style>");

						dom.LoadXml(newContents);
					}
					catch (Exception e)
					{
						var exceptionWithHtmlContents = new Exception(string.Format("{0}{2}{2}{1}",
							e.Message, newContents, Environment.NewLine));
						throw exceptionWithHtmlContents;
					}
				}
			}
			try
			{
				//It's a mystery but http://jira.palaso.org/issues/browse/BL-46 was reported by several people on Win XP, even though a look at html tidy dispose indicates that it does dispose (and thus close) the stream.
				// Therefore, I'm moving the dispose to an explict call so that I can catch the error and ignore it, leaving an extra file in Temp.

				temp.Dispose();
					//enhance... could make a version of this which collects up any failed deletes and re-attempts them with each call to this
			}
			catch (Exception error)
			{
				//swallow
				Debug.Fail("Repro of http://jira.palaso.org/issues/browse/BL-46 ");
			}


			//this is a hack... each time we write the content, we add a new <meta http-equiv="Content-Type" content="text/html; charset=utf-8">
			//so for now, we remove it when we read it in. It'll get added again when we write it out
			RemoveAllContentTypesMetas(dom);

			return dom;
		}

		/// <summary>
		/// Tidy is over-zealous. This is a work-around. After running Tidy, then call RemoveFillerInEmptyElements() on the same text
		/// </summary>
		/// <returns></returns>
		private static string AddFillerToKeepTidyFromRemovingEmptyElements(string content)
		{
			//hack. tidy deletes <span data-libray='somethingImportant'></span>
			// and also (sometimes...apparently only the first child in a parent) <i some-important-attributes></i>
			content = content.Replace("></span>", ">REMOVEME</span>");
			content = content.Replace("></i>", ">REMOVEME</i>");
			content = content.Replace("></b>", ">REMOVEME</b>");
			content = content.Replace("></u>", ">REMOVEME</u>");
			// This handles empty elements in the form of XML contractions like <i />
			content = new Regex(@"<([ubi]|span)\s*/>").Replace(content, "<$1>REMOVEME</$1>");
			return content;
		}

		/// <summary>
		/// This is to be run after running tity
		/// </summary>
		private static string RemoveFillerInEmptyElements(string contents)
		{
			return contents.Replace("REMOVEME", "").Replace("\0", "");
		}




		/// <summary>
		/// Beware... htmltidy doesn't consider such things as a second <body> element to warrant any more than a "warning", so this won't throw!
		/// </summary>
		/// <param name="content"></param>
		public static void ThrowIfHtmlHasErrors(string content)
		{
			using (var tidy = TidyManaged.Document.FromString(content))
			{
				tidy.ShowWarnings = false;
				tidy.Quiet = true;
				tidy.AddTidyMetaElement = false;
				tidy.OutputXml = true;
				tidy.DocType = DocTypeMode.Omit; //when it supports html5, then we will let it out it

				using (var log = new MemoryStream())
				{
					tidy.CleanAndRepair(log);
					string errors = ASCIIEncoding.ASCII.GetString(log.ToArray());
					if (!string.IsNullOrEmpty(errors))
					{
						throw new ApplicationException(errors);
					}
				}
			}
		}


		/// <summary>
		/// If an element has empty contents, like <textarea></textarea>, browsers will sometimes drop the end tag, so that now, when we read it back into xml,
		/// anything following the <textarea> will be interpreted as part of the <textarea>!  This method makes sure such tags are never totally empty.
		/// </summary>
		/// <param name="dom"></param>
		public static void MakeXmlishTagsSafeForInterpretationAsHtml(XmlDocument dom)
		{
			foreach (XmlElement node in dom.SafeSelectNodes("//textarea"))
			{
				if (!node.HasChildNodes)
				{
					node.AppendChild(node.OwnerDocument.CreateTextNode(""));
				}
			}
			foreach (XmlElement node in dom.SafeSelectNodes("//div"))
			{
				if (!node.HasChildNodes)
				{
					node.AppendChild(node.OwnerDocument.CreateTextNode(""));
				}
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//p"))
				//without  this, an empty paragraph suddenly takes over the subsequent elements. Browser sees <p></p> and thinks... let's just make it <p>, shall we? Stupid optional-closing language, html is....
			{
				if (!node.HasChildNodes)
				{
					node.AppendChild(node.OwnerDocument.CreateTextNode(""));
				}
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//span"))
			{
				if (!node.HasChildNodes)
				{
					node.AppendChild(node.OwnerDocument.CreateTextNode(""));
				}
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//script"))
			{
				if (string.IsNullOrEmpty(node.InnerText) && node.ChildNodes.Count == 0)
				{
					node.InnerText = " ";
				}
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//style"))
			{
				if (string.IsNullOrEmpty(node.InnerText) && node.ChildNodes.Count == 0)
				{
					node.InnerText = " ";
				}
			}
		}

		/// <summary>
		/// Convert the DOM (which is expected to be XHTML5) to HTML5
		/// </summary>
		public static string SaveDOMAsHtml5(XmlDocument dom, string targetPath)
		{
			// I got tired of seeing data-book elements with formatButton divs saved in them.
			RemoveUiElements(dom);
			using (var xmlFile = new TempFile())
			{
				// First we write the DOM out to string

				var settings = new XmlWriterSettings {Indent = true, CheckCharacters = true, OmitXmlDeclaration = true};
				var xmlStringBuilder = new StringBuilder();
				using (var writer = XmlWriter.Create(xmlStringBuilder, settings))
				{
					dom.WriteContentTo(writer);
					writer.Close();
				}

				// HTML Tidy will mess that xml up, so we have this work around to make it "safe from libtidy"
				var xml = xmlStringBuilder.ToString();
				xml = AddFillerToKeepTidyFromRemovingEmptyElements(xml);

				// Now re-write as html, indented nicely
				string html;
				using (var tidy = TidyManaged.Document.FromString(xml))
				{
					tidy.ShowWarnings = false;
					tidy.Quiet = true;
					tidy.AddTidyMetaElement = false;
					tidy.OutputXml = false;
					tidy.OutputHtml = true;
					tidy.DocType = DocTypeMode.Html5;
					tidy.MergeDivs = AutoBool.No;
					tidy.MergeSpans = AutoBool.No;
					tidy.PreserveEntities = true;
					tidy.JoinStyles = false;
					tidy.IndentBlockElements = AutoBool.Auto; //instructions say avoid 'yes'
					tidy.WrapAt = 9999;
					tidy.IndentSpaces = 4;
					tidy.CharacterEncoding = EncodingType.Utf8;
					tidy.CleanAndRepair();
					using (var stream = new MemoryStream())
					{
						tidy.Save(stream);
						stream.Flush();
						stream.Seek(0L, SeekOrigin.Begin);
						using (var sr = new StreamReader(stream, Encoding.UTF8))
							html = sr.ReadToEnd();
					}
				}

				// Now revert the stuff we did to make it "safe from libtidy"
				html = RemoveFillerInEmptyElements(html);
				File.WriteAllText(targetPath, html, Encoding.UTF8);
			}

			return targetPath;
		}

		private static void RemoveUiElements(XmlDocument dom)
		{
			var allDivs = dom.GetElementsByTagName("div");
			var toBeRemoved = allDivs.Cast<XmlNode>().Where(div => div.GetOptionalStringAttribute("class", "").Contains("bloom-ui")).ToList();
			foreach (var node in toBeRemoved)
				node.ParentNode.RemoveChild(node);
		}

		public static void RemoveAllContentTypesMetas(XmlDocument dom)
		{
			foreach (XmlElement n in dom.SafeSelectNodes("//head/meta[@http-equiv='Content-Type']"))
			{
				n.ParentNode.RemoveChild(n);
			}
		}
	}
}
