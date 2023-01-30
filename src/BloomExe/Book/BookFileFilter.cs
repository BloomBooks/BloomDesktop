using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Bloom.ToPalaso;
using Microsoft.DotNet.PlatformAbstractions;
using SIL.IO;
using SIL.Xml;

namespace Bloom.Book
{
	public class BookFileFilter
	{
		// Path to the root folder of the book.
		public string BookFolderPath;
		private int _bookFolderPrefixLength; // includes following slash
		private string _theOneHtmlPath;
		private string _theOneHtmlRelativePath; // relative to BookFolderPath

		// Copying for a destination where the copy might be used as the source for
		// editing or translating a book, such as upload or making a bloompack
		public bool ForEdit
		{
			get => _forEdit;
			set
			{
				Debug.Assert(value == true, "We don't support reverting to not-for-edit");
				_forEdit = value;
				ForInteractive = true;
				WantMusic = true;
				// I'm not sure what this is for, possibly storing month names etc. in Wall Calendar,
				// but BookStarter wants it if present.
				_specialCases["configuration.html"] = true;
				// markdown files are sometimes used to describe templates.
				BookLevelFileExtensionsLowerCase.Add(".md");
				// In case the book is a template, we want at least the thumbnails
				// and description files. This is rare enough that I don't think it's
				// worth trying to be more precise.
				_specialGroups.Add(new Regex(@"^templates(/|\\)"));
			}
		}

		/// <summary>
		/// Copying for a destination where the user can interact with the book.
		/// For example, BloomPubs can have activities.
		/// </summary>
		public bool ForInteractive
		{
			get => _forInteractive;
			set
			{
				Debug.Assert(value == true, "We don't support reverting to not-for-interactive");
				_forInteractive = value;
				_specialGroups.Add(new Regex(@"^activities(/|\\)"));
			}
		}

		public bool WantMusic
		{
			get => _wantMusic;
			set
			{
				_wantMusic = value;
				_musicFiles = null;
			}
		}

		public bool WantVideo
		{
			get => _wantVideo;
			set
			{
				_wantVideo = value;
				_videoFiles = null;
			}
		}

		/// <summary>
		/// The specified path (starting from the book folder) should always be
		/// accepted (or always be rejected, if the second argument is false).
		/// This is useful for special cases that don't seem to fit any particular
		/// pattern, like passing a BloomBookOrder for upload.
		/// </summary>
		public void AddException(string path, bool accept = true)
		{
			if (!SIL.PlatformUtilities.Platform.IsLinux)
				path = path.ToLowerInvariant();
			_specialCases[path] = accept;
		}
		Dictionary<string, bool> _specialCases = new Dictionary<string, bool>();
		List<Regex> _specialGroups = new List<Regex>();
		private bool _forEdit;
		private bool _forInteractive;
		private HtmlDom _dom;

		public BookFileFilter(string bookFolderPath)
		{
			BookFolderPath = bookFolderPath;
			_bookFolderPrefixLength = BookFolderPath.Length + 1;
			// enhance: handle not found? But we'll only be doing this on valid books
			_theOneHtmlPath = BookStorage.FindBookHtmlInFolder(BookFolderPath);
			if (!string.IsNullOrEmpty(_theOneHtmlPath))
			{
				_theOneHtmlRelativePath = _theOneHtmlPath.Substring(_bookFolderPrefixLength);
				if (!SIL.PlatformUtilities.Platform.IsLinux)
					_theOneHtmlRelativePath = _theOneHtmlRelativePath.ToLowerInvariant();
			}
		}

		HtmlDom Dom
		{
			get
			{
				if (_dom == null)
				{
					// This is mainly used in unit tests, but could be helpful
					// in some other situation where we don't already have one.
					_dom = new HtmlDom(RobustFile.ReadAllText(_theOneHtmlPath, Encoding.UTF8), true);
				}
				return _dom;
			}
			set
			{
				_dom = value;
			}
		}

		/// <summary>
		/// Given a set of paths, typically of all the files in a bloom book folder,
		/// return the ones that pass the filter.
		/// </summary>
		/// <param name="input"></param>
		/// <returns></returns>
		public string[] FilterPaths(string[] input, string bookFolderPath)
		{
			var length = bookFolderPath.Length +1;
			return input.Where(x => Filter(x.Substring(length))).ToArray();
		}

		/// <summary>
		/// The languages for which we want narration data copied to the output.
		/// Use an empty array to copy nothing, null to copy everything that is used
		/// in the book.
		/// </summary>
		public string[] NarrationLanguages
		{
			get => _narrationLanguages;
			set
			{
				_narrationLanguages = value;
				_narrationFiles = null; // need to recompute when next needed
			}
		}

		// These file extensions are the default ones that will be included in the compressed version
		// at the top book level.
		internal readonly HashSet<string> BookLevelFileExtensionsLowerCase =
			new HashSet<string>(new[] { ".svg", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".otf", ".ttf", ".woff", ".css", ".json", ".txt", ".js", ".distribution" });

		public bool Filter(string pathFromRootFolder)
		{
			if (!SIL.PlatformUtilities.Platform.IsLinux)
			{
				pathFromRootFolder = pathFromRootFolder.ToLowerInvariant();
			}
			if (_specialCases.TryGetValue(pathFromRootFolder, out bool isWanted))
				return isWanted;
			foreach (var regex in _specialGroups)
			{
				if (regex.IsMatch(pathFromRootFolder))
					return true;
			}
			var path = pathFromRootFolder.Split(Path.DirectorySeparatorChar);
			if (path.Length > 1)
			{
				if (path[0] == "audio" && path.Length == 2)
				{
					if (NarrationFiles.Contains(path[1]))
						return true;
					return WantMusic && MusicFiles.Contains(path[1]);
				}
				if (path[0] == "video" && path.Length == 2 && WantVideo)
					return VideoFiles.Contains(path[1]);
				return false;
			}
			var extension = Path.GetExtension(pathFromRootFolder);
			if(BookLevelFileExtensionsLowerCase.Contains(extension))
				return true;
			if (extension == ".htm")
			{
				return pathFromRootFolder == _theOneHtmlRelativePath;
			}

			return false;
		}

		private HashSet<string> _narrationFiles;
		private string[] _narrationLanguages = Array.Empty<string>();

		HashSet<string> NarrationFiles
		{
			get
			{
				if (_narrationFiles == null)
				{
					// The algorithm here is uncomfortably similar to BookStorage.GetNarrationAudioFileNamesReferencedInBook.
					// But here we have a list of language to INclude, and we never want .wav files
					// I'm not sure whether we need includeSplitTextBoxAudio to be true.
					// But it only makes a difference in an obsolete case, and it just might be wanted
					// if it occurs, at least if we do further editing of the book, so it's simplest to just include it.
					var narrationElements = HtmlDom.SelectChildNarrationAudioElements(Dom.Body, true).Cast<XmlElement>();
					if (NarrationLanguages != null)
					{
						var narrationLangs = new HashSet<string>(NarrationLanguages);
						narrationElements = narrationElements
							.Where(node => narrationLangs.Contains(node.ParentWithClass("bloom-editable")
								.GetOptionalStringAttribute("lang", null)));
					}

					_narrationFiles = new HashSet<string>(narrationElements
						.Select(node => node.GetOptionalStringAttribute("id", null))
						.Where(id => id != null)
						.Select(id => id + ".mp3"));
				}
				return _narrationFiles;
			}
		}

		private HashSet<string> _musicFiles;
		private bool _wantMusic;
		private bool _wantVideo;
		private HashSet<string> _videoFiles;

		HashSet<string> MusicFiles
		{
			get
			{
				if (_musicFiles == null)
				{
					_musicFiles = new HashSet<string>(Dom.GetBackgroundMusicFileNamesReferencedInBook()
						.Select(s => SIL.PlatformUtilities.Platform.IsLinux ? s : s.ToLowerInvariant()));
				}
				return _musicFiles;
			}
		}

		HashSet<string> VideoFiles
		{
			get
			{
				if (_videoFiles == null)
				{
					var element = Dom.RawDom.DocumentElement;
					var usedVideoPaths = BookStorage.GetVideoPathsRelativeToBook(element);
					_videoFiles = new HashSet<string>(usedVideoPaths
						.Select(s =>Path.GetFileName(s))
						.Select(s => SIL.PlatformUtilities.Platform.IsLinux ? s : s.ToLowerInvariant()));
				}
				return _videoFiles;
			}
		}

		public string[] GetAllFilePaths(string folderPath)
		{
			return Directory.EnumerateFiles(folderPath).Concat(
				Directory.EnumerateDirectories(folderPath).SelectMany(GetAllFilePaths)).ToArray();
		}

		public void CopyBookFolderFiltered(string destinationFolder)
		{
			Directory.CreateDirectory(destinationFolder);
			foreach (var path in FilterPaths(GetAllFilePaths(BookFolderPath), BookFolderPath))
			{
				var pathFromBookFolder = path.Substring(_bookFolderPrefixLength);
				var dest = Path.Combine(destinationFolder, pathFromBookFolder);
				Directory.CreateDirectory(Path.GetDirectoryName(dest));
				RobustFile.Copy(path, dest);
			}
		}

		// Todo: Similar function that makes a zip file.
	}
}
