using System;
using System.IO;
using System.Text;
using System.Xml;
using Bloom.SafeXml;
using SIL.Reporting;

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
            // ENHANCE: Address that a Guard fails when this constructor is called.
            // This class inherits from Book. So it calls Book's default constructor here.
            // But Book's default constructor has a Guard that says it's only supposed to be called from the unit tests.
            // One potential route is to create an interface... IBook or ISimpleBook
            Exception = exception;
            _folderPath = folderPath;
            _canDelete = canDelete;
            Logger.WriteEvent("Created ErrorBook with exception message: " + Exception.Message);
            BookInfo = new ErrorBookInfo(folderPath, exception);
        }

        public override Layout GetLayout()
        {
            return Layout.A5Portrait;
        }

        public override string TitleBestForUserDisplay
        {
            get { return Title; }
        }

        public override string Title
        {
            get
            {
                return Path.GetFileName(FolderPath); //actually gives us the leaf directory name
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

        public override bool HasFatalError
        {
            get { return true; }
        }

        //        public override bool Delete()
        //        {
        //            var didDelete= ConfirmRecycleDialog.Recycle(_folderPath);
        //            if(didDelete)
        //                Logger.WriteEvent("After ErrorBook.Delete({0})", _folderPath);
        //            return didDelete;
        //        }

        //		private HtmlDom GetErrorDOM()
        //		{
        //
        //			var dom = SafeXmlDocument.Create();
        //			var builder = new StringBuilder();
        //			builder.Append("<html><body>");
        //			builder.AppendLine("<p>This book (" + FolderPath + ") has errors.");
        //			builder.AppendLine(
        //				"This doesn't mean your work is lost, but it does mean that something is out of date or has gone wrong, and that someone needs to find and fix the problem (and your book).</p>");
        //
        //			builder.Append(Exception.Message.Replace(Environment.NewLine,"<br/>"));
        //
        //			builder.Append("</body></html>");
        //			return new HtmlDom(builder.ToString());
        //		}

        //		public override HtmlDom GetPreviewHtmlFileForWholeBook()
        //		{
        //			return GetErrorDOM();
        //		}

        public override SafeXmlDocument RawDom
        {
            get
            {
                throw new ApplicationException(
                    "An ErrorBook was asked for a RawDom. The ErrorBook's exception message is "
                        + Exception.Message
                );
            }
        }
    }
}
