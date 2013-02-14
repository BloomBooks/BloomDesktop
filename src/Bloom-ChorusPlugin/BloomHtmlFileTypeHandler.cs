using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Chorus.FileTypeHanders;
using Chorus.VcsDrivers.Mercurial;
using Chorus.merge;
using Chorus.merge.xml.generic;
using Palaso.Code;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Xml;

namespace Bloom_ChorusPlugin
{
	public class BloomHtmlFileTypeHandler : IChorusFileTypeHandler
	{
		/// <summary>
		/// Do a 3-file merge, placing the result over the "ours" file and returning an error status
		/// </summary>
		/// <remarks>Implementations can exit with an exception, which the caller will catch and deal with.
		/// The must not have any UI, no interaction with the user.</remarks>
		public void Do3WayMerge(MergeOrder mergeOrder)
		{
			Guard.AgainstNull(mergeOrder, "mergeOrder");

			if (mergeOrder == null)
				throw new ArgumentNullException("mergeOrder");

			var merger = new XmlMerger(mergeOrder.MergeSituation);
			SetupElementStrategies(merger);

			merger.EventListener = mergeOrder.EventListener;

			using(var oursXml = new HtmlFileForMerging(mergeOrder.pathToOurs))
			using(var theirsXml = new HtmlFileForMerging(mergeOrder.pathToTheirs))
			using (var ancestorXml = new HtmlFileForMerging(mergeOrder.pathToCommonAncestor))
			{
				var result = merger.MergeFiles(oursXml.GetPathToXHtml(), theirsXml.GetPathToXHtml(), ancestorXml.GetPathToXHtml());

				CarefullyWriteOutResultingXml(oursXml, result);

				//now convert back to html
				oursXml.SaveHtml();
			}
		}

		private static void CarefullyWriteOutResultingXml(HtmlFileForMerging oursXml, NodeMergeResult result)
		{
//REVIEW: it's not clear whether we need all this fancy xml cannonicalization, when we're going to run
			//it through html tidy to make html anyhow. This is just from the code we copied from.
			//Note it also is doing something careful with unicode.

			using (var writer = XmlWriter.Create(oursXml.GetPathToXHtml(), CanonicalXmlSettings.CreateXmlWriterSettings()))
			{
				var nameSpaceManager = new XmlNamespaceManager(new NameTable());
				//nameSpaceManager.AddNamespace("palaso", "urn://palaso.org/ldmlExtensions/v1");

				var readerSettings = new XmlReaderSettings
										{
											NameTable = nameSpaceManager.NameTable,
											IgnoreWhitespace = true,
											ConformanceLevel = ConformanceLevel.Auto,
											ValidationType = ValidationType.None,
											XmlResolver = null,
											CloseInput = true,
											ProhibitDtd = false
										};
				using (
					var nodeReader = XmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(result.MergedNode.OuterXml)),
													  readerSettings))
				{
					writer.WriteNode(nodeReader, false);
				}
			}
		}

		private static void SetupElementStrategies(XmlMerger merger)
		{
			merger.MergeStrategies.ElementToMergeStrategyKeyMapper = new BloomElementToStrategyKeyMapper();

			merger.MergeStrategies.SetStrategy("html", ElementStrategy.CreateSingletonElement());
			merger.MergeStrategies.SetStrategy("head", ElementStrategy.CreateSingletonElement());
			merger.MergeStrategies.SetStrategy("body", ElementStrategy.CreateSingletonElement());

			merger.MergeStrategies.SetStrategy("DataDiv", new ElementStrategy(true)
			{
				IsAtomic = false,
				OrderIsRelevant = false,
				MergePartnerFinder = new FindByKeyAttribute("id"), //yes, it's a singleton of sorts, but by the id, not the tag
			});

			merger.MergeStrategies.SetStrategy("BookDataItem", new ElementStrategy(true)
			{
				IsAtomic = true,
				OrderIsRelevant = false,
				MergePartnerFinder = new FindByMultipleKeyAttributes(new List<string>(new string[] {"data-book", "lang"}))
			});

			merger.MergeStrategies.SetStrategy("PageDiv", new ElementStrategy(true)
														{
															IsAtomic = true, //we're not trying to merge inside pages
															MergePartnerFinder = new FindByKeyAttribute("id"),

														});
		}

		public bool CanMergeFile(string pathToFile)
		{
			if (string.IsNullOrEmpty(pathToFile))
				return false;
			if (!File.Exists(pathToFile))
				return false;
			var extension = Path.GetExtension(pathToFile);
			if (string.IsNullOrEmpty(extension))
				return false;
			if (extension[0] != '.')
				return false;

			return FileUtils.CheckValidPathname(pathToFile, ".htm");
		}
		public bool CanDiffFile(string pathToFile)
		{
			return CanMergeFile(pathToFile);
		}
		public bool CanPresentFile(string pathToFile)
		{
			return false;
		}
		public bool CanValidateFile(string pathToFile)
		{
				return false;
		}

		public string ValidateFile(string pathToFile, IProgress progress)
		{
			return null;
		}

		public IEnumerable<IChangeReport> Find2WayDifferences(FileInRevision parent, FileInRevision child, HgRepository repository)
		{
			throw new NotImplementedException();
		}

		public IChangePresenter GetChangePresenter(IChangeReport report, HgRepository repository)
		{
			throw new NotImplementedException();
		}

		public IEnumerable<IChangeReport> DescribeInitialContents(FileInRevision fileInRevision, TempFile file)
		{
			return new IChangeReport[] { new DefaultChangeReport(fileInRevision, "Added") };
		}

		public IEnumerable<string> GetExtensionsOfKnownTextFileTypes()
		{
			return new[] {"htm"};
		}

		public uint MaximumFileSize
		{
			get { return UInt32.MaxValue; }
		}

	}
}
