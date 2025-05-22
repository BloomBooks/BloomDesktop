using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.SafeXml;
using Bloom.ToPalaso;
using SIL.IO;
using SIL.Xml;

namespace Bloom.Book
{
    public interface IFilter
    {
        bool ShouldAllow(string fullPath);
    }

    /// <summary>
    /// BookFileFilter is a configurable filter for determining which files should be copied either into a temporary folder or
    /// a zip folder, starting from a Bloom book folder. It primarily uses a white list approach: files in the root directory
    /// are copied if they have certain extensions, audio files are copied if they are used in the book (and, optionally, if
    /// they are narration files for the languages we need, or music files that are wanted). Several properties allow it to
    /// be configured for common copy scenarios like when the copy will be used for further editing (upload, bloompack) or
    /// situations where users can interact (bloomPub). Exceptions can also be added for particular files that are or are not
    /// wanted in a particular situation.
    /// Putting this functionality in a filter class makes it easy to use both for copying folders and for restricting what
    /// is put in a zip file; it also facilitates testing with a minimum of actual file creation.
    /// The Filter class does have specific knowledge of Bloom books, such as how to find the corresponding narration files
    /// for elements in the document.
    /// </summary>
    public class BookFileFilter : IFilter
    {
        // Path to the root folder of the book.
        public string BookFolderPath;
        private int _bookFolderPrefixLength; // includes following slash
        private string _theOneHtmlPath;
        private string _theOneHtmlRelativePath; // relative to BookFolderPath

        // This violates our "white list" policy, but some white-listed rules
        // have exceptions
        private List<Regex> _thingsToExclude = new List<Regex>();

        // Copying for a destination where the copy might be used as the source for
        // editing or translating a book, such as upload or making a bloompack.
        // One might initially think that if we're going to be doing more editing
        // of the book in Bloom, we'd simply want everything, but we're trying to
        // apply the whitelist approach even here, especially since at least one
        // case (uploading to Bloom Library) specifically allows the user to filter
        // out certain languages and their audio. But we also don't need to upload
        // and download backups, files related to TeamCollection, or any junk the
        // user might have happened to put in the folder.
        public bool IncludeFilesForContinuedEditing
        {
            get => _includeFilesForContinuedEditing;
            set
            {
                if (value == _includeFilesForContinuedEditing)
                    return; // no need to do anything (or the debug check!) if not changing.
                Debug.Assert(value == true, "We don't support reverting to not-for-edit");
                _includeFilesForContinuedEditing = value;
                // Since we're trying to include everything we need to go on working on the book,
                // we naturally need everything we need for the most demanding mode of publishing it.
                IncludeFilesNeededForBloomPlayer = true;
                // So far this is only used for storing month names etc. in Wall Calendar,
                // but that's enough reason to include it. BookStarter uses it if present.
                _specialCases["configuration.html"] = true;
                // markdown files are sometimes used to describe templates.
                BookLevelFileExtensionsLowerCase.Add(".md");
                // In case the book is a template, we want at least the thumbnails
                // and description files. This is rare enough that I don't think it's
                // worth trying to be more precise.
                _specialGroups.Add(new Regex(@"^template(/|\\)"));
            }
        }

        /// <summary>
        /// Copying for a destination where a reader can interact with the book.
        /// For example, BloomPubs can have activities. This is basically everything
        /// needed in any kind of publication of the book, but this is a little less
        /// than the set needed to go on working on it (see IncludeFilesForContinuedEditing above).
        /// These files are of course also needed for lots of other things, like Upload,
        /// BloomPacks, and editing; filters for those purposes set IncludeFilesForContinuedEditing,
        /// which sets this.
        /// </summary>
        public bool IncludeFilesNeededForBloomPlayer
        {
            get => _includeFilesNeededForBloomPlayer;
            set
            {
                if (value == _includeFilesNeededForBloomPlayer)
                    return; // no need to do anything (or the debug check!) if not changing.
                Debug.Assert(value == true, "We don't support reverting to not-for-interactive");
                _includeFilesNeededForBloomPlayer = value;
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

        private string normalizePath(string path)
        {
            if (SIL.PlatformUtilities.Platform.IsLinux)
                return path;
            return path.ToLowerInvariant();
        }

        public void AlwaysAccept(string path)
        {
            AddException(path, true);
        }

        public void AlwaysReject(string path)
        {
            AddException(path, false);
        }

        public void AlwaysReject(Regex re)
        {
            _thingsToExclude.Add(re);
        }

        /// <summary>
        /// The specified path (starting from the book folder) should always be
        /// accepted (or always be rejected, if the second argument is false).
        /// This is useful for special cases that don't seem to fit any particular
        /// pattern.
        /// </summary>
        private void AddException(string path, bool accept = true)
        {
            _specialCases[normalizePath(path)] = accept;
        }

        Dictionary<string, bool> _specialCases = new Dictionary<string, bool>();

        // File paths that match one of these are wanted.
        // It could plausibly be a Dictionary like _specialCases where false indicates
        // that a matching file is NOT wanted; but (a) unless there is no overlap in
        // which patterns match, we'd have to worry about order; and (b) our goal is
        // to whitelist what we DO want rather than blacklisting what we don't.
        List<Regex> _specialGroups = new List<Regex>();
        private bool _includeFilesForContinuedEditing;
        private bool _includeFilesNeededForBloomPlayer;
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
                _theOneHtmlRelativePath = normalizePath(_theOneHtmlRelativePath);
            }
            // We don't ever need placeHolders. In publications we don't want to display
            // them, and BloomEditor already has this image.
            _thingsToExclude.Add(new Regex(normalizePath("^placeHolder")));
        }

        HtmlDom Dom
        {
            get
            {
                if (_dom == null)
                {
                    // This is mainly used in unit tests, but could be helpful
                    // in some other situation where we don't already have one.
                    _dom = new HtmlDom(
                        XmlHtmlConverter.GetXmlDomFromHtmlFile(_theOneHtmlPath, false)
                    );
                }
                return _dom;
            }
            set { _dom = value; }
        }

        /// <summary>
        /// Given a set of paths, typically of all the files in a bloom book folder,
        /// return the ones that pass the filter.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public string[] AllowedPaths(string[] input, string bookFolderPath)
        {
            var length = bookFolderPath.Length + 1;
            return input.Where(x => ShouldAllowRelativePath(x.Substring(length))).ToArray();
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
        internal readonly HashSet<string> BookLevelFileExtensionsLowerCase = new HashSet<string>(
            new[]
            {
                ".svg",
                ".png",
                ".jpg",
                ".jpeg",
                ".gif",
                ".tif",
                ".tiff",
                ".bmp",
                ".otf",
                ".ttf",
                ".woff",
                ".woff2",
                ".css",
                ".json",
                ".txt",
                ".js",
                ".distribution"
            }
        );

        /// <summary>
        /// Return true if the file at fullPath should be included in the output.
        /// </summary>
        public bool ShouldAllow(string fullPath)
        {
            var pathFromRootFolder = fullPath.Substring(BookFolderPath.Length + 1);
            return ShouldAllowRelativePath(pathFromRootFolder);
        }

        /// <summary>
        /// This overload is more convenient for testing, and is also the core of the implementation.
        /// </summary>

        public bool ShouldAllowRelativePath(string pathFromRootFolder)
        {
            pathFromRootFolder = normalizePath(pathFromRootFolder);
            if (_specialCases.TryGetValue(pathFromRootFolder, out bool isWanted))
                return isWanted;
            foreach (var regex in _specialGroups)
            {
                if (regex.IsMatch(pathFromRootFolder))
                    return true;
            }

            foreach (var regex in _thingsToExclude)
            {
                if (regex.IsMatch(pathFromRootFolder))
                    return false;
            }
            var path = pathFromRootFolder.Split(Path.DirectorySeparatorChar);
            if (path.Length > 1)
            {
                if (path[0] == "audio" && path.Length == 2)
                {
                    if (NarrationFiles.Contains(path[1]))
                        return true;
                    if (IncludeFilesNeededForBloomPlayer && SpecialAudioFiles.Contains(path[1]))
                        return true;
                    return WantMusic && MusicFiles.Contains(path[1]);
                }
                if (path[0] == "video" && path.Length == 2 && WantVideo)
                    return VideoFiles.Contains(path[1]);
                // templates and activities are handled, when wanted, by a pattern in _specialGroups.
                return false;
            }
            var extension = Path.GetExtension(pathFromRootFolder);
            if (BookLevelFileExtensionsLowerCase.Contains(extension))
                return true;
            if (extension == ".htm")
            {
                return pathFromRootFolder == _theOneHtmlRelativePath;
            }

            return false;
        }

        private HashSet<string> _narrationFiles;
        private string[] _narrationLanguages = Array.Empty<string>();
        private HashSet<string> _specialAudioFiles;

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
                    var narrationElements = HtmlDom.SelectRealChildNarrationAudioElements(
                        Dom.Body,
                        true
                    );
                    if (NarrationLanguages != null)
                    {
                        var narrationLangs = new HashSet<string>(NarrationLanguages);
                        narrationElements = narrationElements.Where(
                            node =>
                                narrationLangs.Contains(
                                    node.ParentOrSelfWithClass("bloom-editable")
                                        .GetOptionalStringAttribute("lang", null)
                                )
                        );
                    }

                    _narrationFiles = new HashSet<string>(
                        narrationElements
                            .Select(node => node.GetOptionalStringAttribute("id", null))
                            .Where(id => id != null)
                            .Select(id => id + ".mp3")
                    );
                }
                return _narrationFiles;
            }
        }

        /// <summary>
        /// Various elements (currently only in Bloom Games) can have data-X attributes which specify a sound
        /// (implicitly in the audio directory). This method finds all such sounds and adds them to the set
        /// so we can include them when appropriate.
        /// </summary>
        HashSet<string> SpecialAudioFiles
        {
            get
            {
                if (_specialAudioFiles == null)
                {
                    _specialAudioFiles = new HashSet<string>();
                    foreach (
                        SafeXmlElement soundElt in Dom.Body.SafeSelectNodes(".//div[@data-sound]")
                    )
                    {
                        AddAttrFilenameValueToSet(soundElt, "data-sound", _specialAudioFiles);
                    }

                    foreach (var page in Dom.GetPageElements())
                    {
                        AddAttrFilenameValueToSet(page, "data-correct-sound", _specialAudioFiles);
                        AddAttrFilenameValueToSet(page, "data-wrong-sound", _specialAudioFiles);
                    }
                }
                return _specialAudioFiles;
            }
        }

        /// <summary>
        /// Get a filename from an optional attribute, and if present, store the normalized form
        /// of the filename in the set.
        /// </summary>
        void AddAttrFilenameValueToSet(SafeXmlElement elt, string attrName, HashSet<string> set)
        {
            var value = elt.GetAttribute(attrName);
            if (!String.IsNullOrEmpty(value))
                set.Add(normalizePath(value));
        }

        private HashSet<string> _musicFiles;
        private bool _wantMusic;
        private bool _wantVideo;
        private HashSet<string> _videoFiles;

        // Items in this set are simple file names (all from the audio folder)
        HashSet<string> MusicFiles
        {
            get
            {
                if (_musicFiles == null)
                {
                    _musicFiles = new HashSet<string>(
                        Dom.GetBackgroundMusicFileNamesReferencedInBook()
                            .Select(s => normalizePath(s))
                    );
                }
                return _musicFiles;
            }
        }

        // Items in this set are paths from the root, typically video/filename.mp4
        HashSet<string> VideoFiles
        {
            get
            {
                if (_videoFiles == null)
                {
                    var element = Dom.RawDom.DocumentElement;
                    var usedVideoPaths = BookStorage.GetVideoPathsRelativeToBook(element);
                    _videoFiles = new HashSet<string>(
                        usedVideoPaths
                            .Select(s => Path.GetFileName(s))
                            .Select(s => normalizePath(s))
                    );
                }
                return _videoFiles;
            }
        }

        /// <summary>
        /// Get all the files, recursively, in the specified folder. This supports a recursive copy
        /// with a filter applied.
        /// </summary>
        /// <note>It could be more efficient to exclude entire directories we don't want. But it is relatively
        /// rare to have such directories at all, and much cleaner to separate enumerating from filtering.</note>
        public static string[] GetAllFilePaths(string folderPath)
        {
            return Directory
                .EnumerateFiles(folderPath)
                .Concat(Directory.EnumerateDirectories(folderPath).SelectMany(GetAllFilePaths))
                .ToArray();
        }

        public void CopyBookFolderFiltered(string destinationFolder)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (var path in AllowedPaths(GetAllFilePaths(BookFolderPath), BookFolderPath))
            {
                var pathFromBookFolder = path.Substring(_bookFolderPrefixLength);
                var dest = Path.Combine(destinationFolder, pathFromBookFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                RobustFile.Copy(path, dest);
            }
        }
    }
}
