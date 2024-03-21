using Bloom.ErrorReporter;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Bloom.Utils
{
    public class LocalizationHelper
    {
        /// <summary>
        /// Store the item complained about, so we don't keep warning about it.
        /// </summary>
        static List<string> _warningsIssued = new List<string>();

        /// <summary>
        /// Check whether a desired localization file is missing or unreadable, and warn the user if so.
        /// This check is only done if the language used for the description is not the first priority.
        /// </summary>
        /// <remarks>
        /// See BL-13115, BL-13167, and BL-13168 for user reports of a known localization not being found.
        /// </remarks>
        public static void CheckForMissingLocalization(
            List<string> langPriorities,
            string languageUsedForDescription,
            string fileNameToCheck
        )
        {
            if (langPriorities[0] == languageUsedForDescription)
                return; // no need to check further
            // If the language used for the description is not the first priority, we need to check if a localization file
            // is missing or unreadable.  The final value in langPriorities is "en", so we don't need to check it.
            string localizationFolder = null;
            string[] folders;
            var errorMsg = LocalizationManager.GetDynamicString(
                "BloomMediumPriority",
                "Localization.CannotReadFile",
                "Bloom was unable to read a localization file.",
                "Error message for when Bloom is unable to read a file from the localization folder."
            );
            try
            {
                localizationFolder = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                    true,
                    "localization"
                );
                folders = Directory.GetDirectories(localizationFolder); // check that the overall localization folder is readable.
            }
            catch (Exception e)
            {
                if (!_warningsIssued.Contains(localizationFolder))
                {
                    _warningsIssued.Add(localizationFolder);
                    // Warn the user that the localization folder is missing or unreadable.
                    BloomErrorReport.NotifyUserOfProblem(
                        errorMsg + "<br>" + e.Message,
                        null,
                        new NotifyUserOfProblemSettings(AllowSendReport.Disallow)
                    );
                }
                return;
            }
            for (int i = 0; i < langPriorities.Count - 1; i++)
            {
                var lang = langPriorities[i];
                if (lang == languageUsedForDescription)
                    return; // no need to check further
                var folder = localizationFolder.CombineForPath(lang);
                if (folders.Contains(folder))
                {
                    var file = folder.CombineForPath(fileNameToCheck);
                    try
                    {
                        var content = File.ReadAllText(file); // Check that the file exists and is readable.
                        var doc = new XmlDocument();
                        doc.LoadXml(content); // check that the file is valid XML.
                    }
                    catch (XmlException e)
                    {
                        if (!_warningsIssued.Contains(file))
                        {
                            _warningsIssued.Add(file);
                            // Warn the user that the localization file is corrupted.
                            BloomErrorReport.NotifyUserOfProblem(
                                errorMsg + $"<br>{file}<br>{e.Message}",
                                null,
                                new NotifyUserOfProblemSettings(AllowSendReport.Disallow)
                            );
                        }
                    }
                    catch (Exception e)
                    {
                        if (!_warningsIssued.Contains(file))
                        {
                            _warningsIssued.Add(file);
                            // Warn the user that the localization file does not exist or is unreadable.
                            ErrorReport.NotifyUserOfProblem(
                                errorMsg + "<br>" + e.Message,
                                null,
                                new NotifyUserOfProblemSettings(AllowSendReport.Disallow)
                            );
                        }
                    }
                    return;
                }
            }
        }
    }
}
