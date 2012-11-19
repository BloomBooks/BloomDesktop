using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Xml;
using Bloom.Properties;
using Palaso.Reporting;

namespace Bloom.Book
{
	public class ErrorBook : Book
	{
		public readonly Exception Exception;
		private readonly string _folderPath;
		private bool _canDelete;

		/// <summary>
		/// this is a bit of a hack to handle representing a book for which we got an exception while loading the storage... a better architecture wouldn't have this...
		/// </summary>
		public ErrorBook(Exception exception, string folderPath, bool canDelete)
		{
			Exception = exception;
			_folderPath = folderPath;
			Id = folderPath;
			_canDelete = canDelete;
			Logger.WriteEvent("Created ErrorBook with exception message: " + Exception.Message);
		}

		public override string Title
		{
			get
			{
				return Path.GetFileName(FolderPath);//actually gives us the leaf directory name
			}
		}
		public override string FolderPath
		{
			get { return _folderPath; }
		}

		public override bool CanDelete
		{
			get { return _canDelete; }
		}


		public override void GetThumbNailOfBookCoverAsync(bool drawBorderDashed, Action<Image> callback, Action<Exception> errorCallback)
		{
			callback(Resources.Error70x70);
		}

		public XmlDocument GetEditableHtmlDomForPage(IPage page)
		{
			return GetErrorDOM();
		}

		public override bool CanUpdate
		{
			get { return false; }
		}

		public override bool HasFatalError
		{
			get { return true; }
		}

		public override bool Delete()
		{
			var didDelete= Palaso.UI.WindowsForms.FileSystem.ConfirmRecycleDialog.Recycle(_folderPath);
			if(didDelete)
				Logger.WriteEvent("After ErrorBook.Delete({0})", _folderPath);
			return didDelete;
		}

		private XmlDocument GetErrorDOM()
		{
			var dom = new XmlDocument();
			var builder = new StringBuilder();
			builder.Append("<html><body>");
			builder.AppendLine("<p>This book (" + FolderPath + ") has errors.");
			builder.AppendLine(
				"This doesn't mean your work is lost, but it does mean that something is out of date or has gone wrong, and that someone needs to find and fix the problem (and your book).</p>");

			builder.Append(Exception.Message.Replace(Environment.NewLine,"<br/>"));

			builder.Append("</body></html>");
			dom.LoadXml(builder.ToString());
			return dom;
		}

		public override XmlDocument GetPreviewHtmlFileForWholeBook()
		{
			return GetErrorDOM();
		}

		public override XmlDocument RawDom
		{
			get
			{
				throw new ApplicationException("An ErrorBook was asked for a RawDom. The ErrorBook's exception message is "+Exception.Message);
			}
		}

		public override void SetTitle(string t)
		{
			Logger.WriteEvent("An ErrorBook was asked to set title.  The ErrorBook's exception message is "+Exception.Message);
		}
	}
}