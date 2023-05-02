using System;

namespace Bloom.Api
{

	public enum InMemoryHtmlFileSource
	{
		Normal,     // Normal page preview
		Pub,        // PDF preview
		Thumb,      // Thumbnailer
		Pagelist,   // Initial list of page thumbs
		JustCheckingPage, // Currently used in epub/BloomPub page checking. Causes Server to replace videos with placeholders.
		Nav,        // Navigating to a new page?
		Preview,    // Preview whole book
		Frame       // Editing View is updating single displayed page
	}


	/// <summary>
	/// A InMemoryHtmlFile is used in connection with simulating a current-page file that needs
	/// to (seem to) be in the book folder so local hrefs work. We don't actually put files there
	/// (see BloomServer.MakeInMemoryHtmlFileInBookFolder for more), but rather
	/// store some data in the our file server object.
	/// The particular purpose of the InMemoryHtmlFile is to manage the lifetime for which
	/// the in memory page is kept in the server. It can be passed to the Browser which will
	/// Dispose() it when no longer needed.
	/// (In that regard, it is used in rather the same way as a TempFile object is used to
	/// make sure that a temp file gets deleted when the Browser no longer needs it.)
	/// </summary>
	public class InMemoryHtmlFile : IDisposable
	{
		public void Dispose()
		{
			BloomServer.RemoveInMemoryHtmlFile(Key);
		}

		/// <summary>
		/// The key under which the server stores the data that should be discarded
		/// when this object gets disposed.
		/// </summary>
		public string Key { get; set; }

		public static string GetTitleForProcessExplorer(InMemoryHtmlFileSource source)
		{
			switch (source)
			{
				case InMemoryHtmlFileSource.Normal:
					return "Normal";
				case InMemoryHtmlFileSource.Pub:
					return "Pub";
				case InMemoryHtmlFileSource.Thumb:
					return "Thumb";
				case InMemoryHtmlFileSource.Pagelist:
					return "PageList";
				case InMemoryHtmlFileSource.Preview:
					return "Preview";
				case InMemoryHtmlFileSource.Frame:
					return "Frame";
				default: return "Other"; // I got bored of listing them
			}
		}
	}
}
