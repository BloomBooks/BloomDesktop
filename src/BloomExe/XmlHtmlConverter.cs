using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using Bloom.Book;
using BloomTemp;
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
			//hack. tidy deletes <span data-libray='somethingImportant'></span>
            // and also (sometimes...apparently only the first child in a parent) <i some-important-attributes></i>
			content = content.Replace("></span>", ">REMOVEME</span>");
            content = content.Replace("></i>", ">REMOVEME</i>");

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
						throw new ApplicationException(errors + "\r\n\r\n" + content);
					}
					var newContents = tidy.Save();
					try
					{
						newContents = newContents.Replace("&nbsp;", "&#160;");
							//REVIEW: 1) are there others? &amp; and such are fine.  2) shoul we to convert back to &nbsp; on save?
						newContents = newContents.Replace("REMOVEME", "");
						dom.LoadXml(newContents);
					}
					catch (Exception e)
					{
						var exceptionWithHtmlContents = new Exception(e.Message + "\r\n\r\n" + newContents);
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
		public static string SaveDOMAsHtml5(XmlDocument dom, string tempPath)
		{
			var initialOutputPath = Path.GetTempFileName();

			XmlWriterSettings settings = new XmlWriterSettings();
			settings.Indent = true;
			settings.CheckCharacters = true;
			settings.OmitXmlDeclaration = true;

			using (var writer = XmlWriter.Create(initialOutputPath, settings))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}

			//now re-write, indented nicely
			using (var tidy = TidyManaged.Document.FromFile(initialOutputPath))
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
				tidy.Save(tempPath);
			}
			File.Delete(initialOutputPath);
			return tempPath;
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
