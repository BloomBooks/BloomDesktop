using Bloom.SafeXml;
using SIL.Extensions;
using SIL.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Bloom.Utils
{
    public class LocalizationHelper
    {
        /// <summary>
        /// Check whether a desired localization file is missing or unreadable, and throw an exception if so.
        /// This check is done only if the language desired is "fr" or "es".
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
            if (Program.RunningUnitTests)
                return; // We don't want to check for missing localizations during unit tests.
            var localizationFolder = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                true,
                "localization"
            );
            // The last item in the langPriorities list is always "en", so we don't need to check it.
            for (int i = 0; i < langPriorities.Count - 1; i++)
            {
                var lang = langPriorities[i];
                if (lang == languageUsedForDescription)
                    return; // no need to check further, we got back the localization we wanted.
                if (lang != "fr" && lang != "es")
                    continue; // we check only these languages: others may not have translations.
                var filepath = localizationFolder.CombineForPath(lang, fileNameToCheck);
                Exception except = null;
                try
                {
                    var content = RobustFile.ReadAllText(filepath); // Check that the file exists and is readable.
                    var doc = SafeXmlDocument.Create();
                    doc.LoadXml(content); // check that the file is valid XML.
                }
                catch (Exception e)
                {
                    except = e;
                }
                // Even without an exception, it's bad to arrive at this point.
                throw new Exception(
                    $"Unexpected failure to get localized string for {lang} from {filepath}",
                    except
                );
            }
        }
    }
}
