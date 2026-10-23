using System;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Collection;
using Bloom.web.controllers;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Edit
{
    /// <summary>
    /// This class is a remnant of an earlier model in which each tool had a C# subclass of ToolboxTool.
    /// A few static methods relating to DecodableReader Settings are all that remain.
    /// </summary>
    static class DecodableReaderToolSettings
    {
        public const string AllowedWordsFolderName = "Allowed Words";

        /// <summary>
        /// We keep reader tool settings for each language in %localappdata%/SIL/Bloom.
        /// e.g., for language kaj, we would expect to find
        /// ReaderToolsSettings-kaj.json
        /// ReaderToolsWords-kaj.json
        /// and possibly a folder with allowed words lists for stages:
        /// Allowed Words-kaj.
        /// A typical reader tools BloomPack only has one of each, but we allow for the possibility of more.
        /// </summary>
        /// <param name="newlyAddedFolderOfThePack"></param>
        internal static void CopyReaderToolsSettingsToWhereTheyBelong(
            string newlyAddedFolderOfThePack
        )
        {
            var destFolder = ProjectContext.GetBloomAppDataFolder();
            foreach (
                var readerSettingsFile in Directory
                    .GetFiles(newlyAddedFolderOfThePack, ReaderToolsSettingsPrefix + "*.json")
                    .Concat(
                        Directory.GetFiles(newlyAddedFolderOfThePack, "ReaderToolsWords-*.json")
                    )
            )
            {
                try
                {
                    var readerSettingsFileName = Path.GetFileName(readerSettingsFile);
                    RobustFile.Copy(
                        readerSettingsFile,
                        Path.Combine(destFolder, readerSettingsFileName),
                        true
                    );
                    if (
                        readerSettingsFileName.StartsWith(
                            ReaderToolsSettingsPrefix,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        var langCode = Path.GetFileNameWithoutExtension(
                            readerSettingsFileName.Substring(ReaderToolsSettingsPrefix.Length)
                        );

                        var allowedWordsSource = Path.Combine(
                            newlyAddedFolderOfThePack,
                            AllowedWordsFolderName
                        );
                        var allowedWordsDest = Path.Combine(
                            destFolder,
                            AllowedWordsFolderName + "-" + langCode
                        );
                        CopyAllowedWords(allowedWordsSource, allowedWordsDest);
                    }
                }
                catch (IOException e)
                {
                    ProblemReportApi.ShowProblemDialog(
                        null,
                        e,
                        "Problem copying Reader Tools Settings from an installed BloomPack.",
                        "nonfatal"
                    );
                }
            }
        }

        private static void CopyAllowedWords(string allowedWordsSource, string allowedWordsDest)
        {
            if (Directory.Exists(allowedWordsSource))
            {
                var sourcePath = "";
                var destPath = "";
                try
                {
                    Directory.CreateDirectory(allowedWordsDest);
                    foreach (var allowedWordsFile in Directory.GetFiles(allowedWordsSource))
                    {
                        sourcePath = allowedWordsFile;
                        destPath = Path.Combine(
                            allowedWordsDest,
                            Path.GetFileName(allowedWordsFile)
                        );
                        RobustFile.Copy(allowedWordsFile, destPath, true);
                    }
                }
                catch (IOException e)
                {
                    var msg = $"Cannot copy {sourcePath} to {destPath}.";
                    ProblemReportApi.ShowProblemDialog(null, e, msg, "nonfatal");
                }
            }
        }

        public const string ReaderToolsSettingsPrefix = "ReaderToolsSettings-";

        /// <summary>
        /// The file (currently at a fixed location in every settings folder) where we store any settings
        /// related to Decodable and Leveled Readers.
        /// </summary>
        public static string GetReaderToolsSettingsFilePath(CollectionSettings settings)
        {
            return Path.Combine(
                Path.GetDirectoryName(settings.SettingsFilePath),
                DecodableReaderToolSettings.ReaderToolsSettingsPrefix
                    + settings.Language1.Tag
                    + ".json"
            );
        }

        /// <summary>
        /// The reader tools settings a collection starts with, for a first language with the given
        /// tag. The literal string here defines our default reader settings for a collection.
        /// </summary>
        public static string GetDefaultSettingsJson(string languageTag)
        {
            return "{\"writingSystemTag\": \""
                + languageTag
                + "\", "
                + "\"letters\":\"a b c d e f g h i j k l m n o p q r s t u v w x y z\","
                + $"\"lang\":\"{languageTag}\","
                + "\"moreWords\":\"\","
                + "\"stages\":[{\"letters\":\"\",\"sightWords\":\"\"}],"
                + "\"levels\":[{\"maxWordsPerSentence\":2,\"maxWordsPerPage\":2,\"maxWordsPerBook\":20,\"maxUniqueWordsPerBook\":0,\"thingsToRemember\":[]},"
                + "{\"maxWordsPerSentence\":5,\"maxWordsPerPage\":5,\"maxWordsPerBook\":23,\"maxUniqueWordsPerBook\":8,\"thingsToRemember\":[]},"
                + "{\"maxWordsPerSentence\":7,\"maxWordsPerPage\":10,\"maxWordsPerBook\":72,\"maxUniqueWordsPerBook\":16,\"thingsToRemember\":[]},"
                + "{\"maxWordsPerSentence\":8,\"maxWordsPerPage\":18,\"maxWordsPerBook\":206,\"maxUniqueWordsPerBook\":32,\"thingsToRemember\":[]},"
                + "{\"maxWordsPerSentence\":12,\"maxWordsPerPage\":25,\"maxWordsPerBook\":500,\"maxUniqueWordsPerBook\":64,\"thingsToRemember\":[]},"
                + "{\"maxWordsPerSentence\":20,\"maxWordsPerPage\":50,\"maxWordsPerBook\":1000,\"maxUniqueWordsPerBook\":0,\"thingsToRemember\":[]}]}";
        }

        /// <summary>
        /// If the collection has no reader tools at all, or if ones that came with the program are newer,
        /// copy the ones that came with the program.
        /// This is language-dependent, we'll typically only overwrite settings for an English collection.
        /// Or, if the language came as a bloompack, we may copy updated settings for a newer bloompack.
        /// Basically this copies the same set of files as CopyReaderToolsSettingsToWhereTheyBelong creates
        /// into the book's own folder.
        /// </summary>
        public static void CopyRelevantNewReaderSettings(CollectionSettings settings)
        {
            var readerToolsPath = GetReaderToolsSettingsFilePath(settings);
            var bloomFolder = ProjectContext.GetBloomAppDataFolder();
            var readerSettingsFileName = Path.GetFileName(readerToolsPath);
            var newReaderTools = Path.Combine(bloomFolder, readerSettingsFileName);
            if (!RobustFile.Exists(newReaderTools))
                return;
            if (
                RobustFile.Exists(readerToolsPath)
                && RobustFile.GetLastWriteTime(readerToolsPath)
                    > RobustFile.GetLastWriteTime(newReaderTools)
            )
                return; // don't overwrite newer existing settings?
            RobustFile.Copy(newReaderTools, readerToolsPath, true);
            // If the settings file is being updated, we should update the corresponding allowed words, if any.
            var langCode = Path.GetFileNameWithoutExtension(
                readerSettingsFileName.Substring(ReaderToolsSettingsPrefix.Length)
            );

            CopyAllowedWords(
                Path.Combine(bloomFolder, AllowedWordsFolderName + "-" + langCode),
                Path.Combine(Path.GetDirectoryName(readerToolsPath), AllowedWordsFolderName)
            );
        }

        /// <remarks>About this file (e.g. ReaderToolsWords-en.json).
        /// In one sense, it is a confusing name because if you look in it, it's much more than words (e.g. orthography).
        /// On the other hand, if I (JH) am reading things correctly, only the sample word aspect of this is used by Bloom.
        /// See the API handler for more remarks on it.
        /// </remarks>
        public const string kSynphonyLanguageDataFileNameFormat = "ReaderToolsWords-{0}.json";
    }
}
