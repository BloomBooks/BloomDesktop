using System;
using System.Collections.Generic;
using System.IO;

namespace Bloom.Utils
{
	public static class OutputFilenames
	{
		// map from the book ID and file type (extension + possible extra tag) to the most recent full file pathname
		static Dictionary<Tuple<string, string>, string> _savedOutputFilePaths = new Dictionary<Tuple<string, string>, string>();
		// map from the file type (extension only) to the most recently used folder for that file type
		static Dictionary<string, string> _savedOutputFolders = new Dictionary<string, string>();

		/// <summary>
		/// Get the output file path for the given book and output type provided by the extension (plus extraTag)
		/// The default is to use the folder name from the book for the filename (with the extension added) and
		/// the user's Documents folder for where to put the file.
		/// Call this before invoking a file open/save dialog to provide an initial path.
		/// </summary>
		/// <param name="book">The Book object</param>
		/// <param name="extension">output type extension (".pdf", ".epub", ".mp4", etc.</param>
		/// <param name="proposedName">A name proposed for the book's output file (optional).  Should not include the extension.  Not used if a path is remembered.</param>
		/// <param name="extraTag">Additional information for variant of output such as language tag or PDF type of output (optional).  Must match corresponding RememberOutputFilePath call.</param>
		/// <param name="proposedFolder">A folder proposed for the book's output file (optional).  It must exist to be used.  Not used if a path is remembered.</param>
		/// <returns>full path of a proposed output file, possibly remembered from an earlier call to RememberOutputFilePath</returns>
		/// <remarks>
		/// This method does not store any information, but does look up any relevent stored data.
		/// </remarks>
		public static string GetOutputFilePath(Book.Book book, string extension,
			string proposedName = null, string extraTag = "", string proposedFolder = null)
		{
			if (_savedOutputFilePaths.TryGetValue(Tuple.Create(book.ID, $"{extraTag}{extension}"), out string path))
				return path;
			string startingFolder;
			if (!_savedOutputFolders.TryGetValue(extension, out startingFolder))
			{
				if (!String.IsNullOrEmpty(proposedFolder) && Directory.Exists(proposedFolder))
					startingFolder = proposedFolder;
				else
					startingFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			}
			if (!String.IsNullOrEmpty(proposedName))
				return (Path.Combine(startingFolder, $"{proposedName}{extension}"));
			return Path.Combine(startingFolder, $"{Path.GetFileName(book.FolderPath)}{extension}");
		}

		/// <summary>
		/// Remember an output file path for the given book and output type provided by the extension (plus extraTag).
		/// Call this with the value returned from a file open/save dialog.
		/// </summary>
		/// <param name="book">The Book Object</param>
		/// <param name="extension">output type extension (".pdf", ".epub", ".mp4", etc.</param>
		/// <param name="outputFilePath">full path of the output file including directories and extension</param>
		/// <param name="extraTag">Additional information for variant of output such as language tag or PDF type of output (optional).  Must match corresponding GetOutputFilePath call.</param>
		/// <remarks>
		/// extraTag is used as part of the key for remembering the full file path, but NOT for remembering the folder.
		/// I'm not sure what the best approach is for granularity of remembering the folder, and opted for less granular.
		/// It doesn't even use the book's ID in the key, just the extension.
		/// </remarks>
		public static void RememberOutputFilePath(Book.Book book, string extension, string outputFilePath, string extraTag = "")
		{
			_savedOutputFilePaths[Tuple.Create(book.ID, $"{extraTag}{extension}")] = outputFilePath;
			_savedOutputFolders[extension] = Path.GetDirectoryName(outputFilePath);
		}

		public static string GetOutputFilePath(Collection.BookCollection collection, string extension)
		{
			if (_savedOutputFilePaths.TryGetValue(Tuple.Create(collection.PathToDirectory, extension), out string path))
				return path;
			string startingFolder;
			if (!_savedOutputFolders.TryGetValue(extension, out startingFolder))
			{
				startingFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			}
			return Path.Combine(startingFolder, $"{collection.Name}{extension}");
		}

		public static void RememberOutputFilePath(Collection.BookCollection collection, string extension, string outputFilePath)
		{
			_savedOutputFilePaths[Tuple.Create(collection.PathToDirectory, extension)] = outputFilePath;
			_savedOutputFolders[extension] = Path.GetDirectoryName(outputFilePath);
		}

		public static void ResetOutputFilenamesMemory()
		{
			if (!Program.RunningUnitTests)
				throw new ApplicationException("ResetOutputFilenamesMemory() can be called only by unit tests!");
			_savedOutputFilePaths.Clear();
			_savedOutputFolders.Clear();
		}
	}
}
