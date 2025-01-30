using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Xml;
using Bloom.SafeXml;

namespace Bloom.Book
{
    /// <summary>
    /// NB: html class names are case sensitive! In this code, we want to accept stuff regardless of case, but always generate Capitalized paper size and orientation names
    /// </summary>
    public class SizeAndOrientation
    {
        public string PageSizeName;

        public bool IsLandScape { get; set; }

        public bool IsSquare =>
            IsLandScape
            && Regex.IsMatch(
                PageSizeName,
                @"(Cm|In)\d+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );

        public SizeAndOrientation() { }

        public string OrientationName
        {
            get { return IsLandScape ? "Landscape" : "Portrait"; }
        }

        public override string ToString()
        {
            return PageSizeName + OrientationName;
        }

        public static SizeAndOrientation FromString(string name)
        {
            var nameLower = name.ToLowerInvariant();
            var startOfOrientationName = Math.Max(
                nameLower.IndexOf("landscape"),
                nameLower.IndexOf("portrait")
            );
            if (startOfOrientationName == -1)
            {
                Debug.Fail("No orientation name found in '" + nameLower + "'");
                return new SizeAndOrientation() { IsLandScape = false, PageSizeName = "A5" };
            }

            return new SizeAndOrientation()
            {
                IsLandScape = nameLower.Contains("landscape"),
                PageSizeName = ExtractPageSizeName(name, startOfOrientationName),
            };
        }

        private static string ExtractPageSizeName(string nameLower, int startOfOrientationName)
        {
            var name = nameLower.Substring(0, startOfOrientationName).ToUpperFirstLetter();
            //these are needed so that "HalfLetter" doesn't come out "Halfletter"
            name = name.Replace("letter", "Letter");
            name = name.Replace("legal", "Legal");
            name = name.Replace("folio", "Folio");
            name = name.Replace("Uscomic", "USComic");
            return name;
        }

        public static void AddClassesForLayout(HtmlDom dom, Layout layout)
        {
            UpdatePageSizeAndOrientationClasses(dom.RawDom, layout);
        }

        public static IEnumerable<Layout> GetSizeAndOrientationChoices(
            HtmlDom dom,
            IFileLocator fileLocator
        )
        {
            //here we walk through all the stylesheets, looking for one with the special comment which tells us which page/orientations it supports
            foreach (SafeXmlElement link in dom.SafeSelectNodes("//link[@rel='stylesheet']"))
            {
                var fileName = link.GetAttribute("href");
                if (
                    fileName.ToLowerInvariant().Contains("mode")
                    || fileName.ToLowerInvariant().Contains("page")
                    || fileName.ToLowerInvariant().Contains("languagedisplay")
                    || fileName.ToLowerInvariant().Contains("origami")
                    || fileName.ToLowerInvariant().Contains("defaultlangstyles")
                    || fileName.ToLowerInvariant().Contains("customcollectionstyles")
                    ||
                    // Ignore this obsolete styles file as well.  See https://issues.bloomlibrary.org/youtrack/issue/BL-9128.
                    fileName
                        .ToLowerInvariant()
                        .EndsWith(
                            Book.kOldCollectionStyles.ToLowerInvariant(),
                            StringComparison.InvariantCulture
                        )
                )
                    continue;

                fileName = fileName.Replace("file://", "").Replace("%5C", "/").Replace("%20", " ");
                fileName = fileName.Replace("\\", "/");
                var path = fileLocator.LocateFile(fileName);
                if (string.IsNullOrEmpty(path) && fileName.StartsWith("../"))
                    path = fileLocator.LocateFile(fileName.Substring(3));
                if (string.IsNullOrEmpty(path))
                {
                    // We're looking for a block of json that is typically found in Basic Book.css or a comparable place for
                    // a book based on some other template. Calling code is prepared for not finding this block.
                    // It seems safe to ignore a reference to some missing style sheet.
                    var fileNameLower = fileName.ToLowerInvariant();
                    if (
                        fileNameLower.Contains("branding")
                        || // these don't contain page size info, anyhow.
                        fileNameLower.Contains("readerstyles")
                        || // these don't contain page size info, anyhow.
                        fileNameLower.Contains("appearance")
                        || fileNameLower.Contains("custombookstyles")
                    ) // Even if these did contain size info (hopefully not...), the derivative won't have the file.
                        continue;
                    NonFatalProblem.Report(
                        ModalIf.None,
                        PassiveIf.Alpha,
                        $"Could not find {fileName} while looking for size choices"
                    );
                    continue;
                }
                var contents = RobustFile.ReadAllText(path);
                var start = contents.IndexOf("STARTLAYOUTS", StringComparison.InvariantCulture);
                if (start < 0)
                    continue; //move on to the next stylesheet
                start += "STARTLAYOUTS".Length;
                var end = contents.IndexOf("ENDLAYOUTS", start, StringComparison.InvariantCulture);
                var s = contents.Substring(start, end - start);

                IEnumerable<Layout> layouts = null;

                try
                {
                    layouts = Layout.GetConfigurationsFromConfigurationOptionsString(s);
                }
                catch (Exception e)
                {
                    throw new ApplicationException(
                        "Problem parsing the 'layouts' comment of "
                            + fileName
                            + ". The contents were\r\n"
                            + s,
                        e
                    );
                }

                foreach (var p in layouts)
                {
                    yield return p;
                }
                yield break;
            }

            // default set of Layouts (These used to be given in 'Basic Book.less'.)
            // See https://silbloom.myjetbrains.com/youtrack/issue/BL-6125.
            yield return new Layout { SizeAndOrientation = FromString("A5Portrait") };
            yield return new Layout { SizeAndOrientation = FromString("A5Landscape") };
            yield return new Layout { SizeAndOrientation = FromString("A6Portrait") };
            yield return new Layout { SizeAndOrientation = FromString("A6Landscape") };
            yield return new Layout { SizeAndOrientation = FromString("A4Portrait") };
            yield return new Layout { SizeAndOrientation = FromString("A4Landscape") };
            yield return new Layout { SizeAndOrientation = FromString("A3Portrait") };
            yield return new Layout { SizeAndOrientation = FromString("A3Landscape") };
            yield return new Layout { SizeAndOrientation = FromString("B5Portrait") };
            yield return new Layout { SizeAndOrientation = FromString("LetterPortrait") };
            yield return new Layout { SizeAndOrientation = FromString("LetterLandscape") };
            yield return new Layout { SizeAndOrientation = FromString("LegalPortrait") };
            yield return new Layout { SizeAndOrientation = FromString("LegalLandscape") };
            yield return new Layout { SizeAndOrientation = FromString("HalfLetterPortrait") };
            yield return new Layout { SizeAndOrientation = FromString("HalfLetterLandscape") };
            yield return new Layout { SizeAndOrientation = FromString("HalfFolioPortrait") };
            yield return new Layout { SizeAndOrientation = FromString("QuarterLetterPortrait") };
            yield return new Layout { SizeAndOrientation = FromString("QuarterLetterLandscape") };
            yield return new Layout { SizeAndOrientation = FromString("Device16x9Portrait") };
            yield return new Layout { SizeAndOrientation = FromString("Device16x9Landscape") };
            yield return new Layout { SizeAndOrientation = FromString("Cm13Landscape") }; // actually square, but acts more like landscape than portrait
            yield return new Layout { SizeAndOrientation = FromString("USComicPortrait") };
            yield return new Layout { SizeAndOrientation = FromString("Size6x9Portrait") };
            yield return new Layout { SizeAndOrientation = FromString("Size6x9Landscape") };
        }

        public static SizeAndOrientation GetSizeAndOrientation(
            SafeXmlDocument dom,
            string defaultIfMissing
        )
        {
            var firstPage = dom.SelectSingleNode(
                "descendant-or-self::div[contains(@class,'bloom-page')]"
            );
            if (firstPage == null)
                return FromString(defaultIfMissing);
            string sao = defaultIfMissing;
            foreach (var part in firstPage.GetAttribute("class").SplitTrimmed(' '))
            {
                if (
                    part.ToLowerInvariant().Contains("portrait")
                    || part.ToLowerInvariant().Contains("landscape")
                )
                {
                    sao = part;
                    break;
                }
            }
            return FromString(sao);
        }

        public static void UpdatePageSizeAndOrientationClasses(SafeXmlNode node, Layout layout)
        {
            foreach (
                SafeXmlElement pageDiv in node.SafeSelectNodes(
                    "descendant-or-self::div[contains(@class,'bloom-page')]"
                )
            )
            {
                RemoveClassesContaining(pageDiv, "layout-");
                RemoveClassesContaining(pageDiv, "Landscape");
                RemoveClassesContaining(pageDiv, "Portrait");

                foreach (var cssClassName in layout.ClassNames)
                {
                    pageDiv.AddClass(cssClassName);
                }
            }
        }

        public string ClassName
        {
            get { return PageSizeName + (IsLandScape ? "Landscape" : "Portrait"); }
        }

        private static void RemoveClassesContaining(SafeXmlElement xmlElement, string substring)
        {
            var classes = xmlElement.GetAttribute("class");
            if (string.IsNullOrEmpty(classes))
                return;
            var parts = classes.SplitTrimmed(' ');

            classes = "";
            foreach (var part in parts)
            {
                if (!part.ToLowerInvariant().Contains(substring.ToLower()))
                    classes += part + " ";
            }
            xmlElement.SetAttribute("class", classes.Trim());
        }
    }
}
