using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests
{
    /// <summary>
    /// Checks that the C# code only asks L10NSharp for ids that exist in an English XLF file.
    /// An id that is in no XLF file can never be translated, so every user sees English (BL-16747).
    /// </summary>
    [TestFixture]
    public class LocalizationIdTests
    {
        // Ids that are deliberately requested from code without an XLF entry. Each one needs a
        // comment saying why. Keep this empty unless there is a good reason.
        private static readonly HashSet<string> s_allowedMissingIds = new HashSet<string>(
            StringComparer.Ordinal
        );

        // LocalizationManager.GetString( "some.id" — the id must be a plain string literal.
        // A following "+" means the literal is only the start of an id built at runtime, which we
        // can't check, so the lookahead skips those.
        private static readonly Regex s_getStringCall = new Regex(
            @"\bLocalizationManager\s*\.\s*GetString\s*\(\s*""(?<id>[^""\\]+)""(?!\s*\+)",
            RegexOptions.Compiled
        );

        // A trans-unit's id, found by text because the trans-unit is inside an XML comment.
        private static readonly Regex s_idInComment = new Regex(
            @"<trans-unit\s+id=""(?<id>[^""]+)""",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Every literal id passed to LocalizationManager.GetString in src/BloomExe must be the id
        /// of a trans-unit in some DistFiles/localization/en/*.xlf file.
        /// </summary>
        [Test]
        public void GetStringIds_InBloomExe_AllExistInEnglishXlf()
        {
            var englishDir = Path.Combine(
                FileLocationUtilities.GetDirectoryDistributedWithApplication("localization"),
                "en"
            );
            var xlfIds = GetXlfIds(englishDir);
            // Sanity checks that we found the real files, so an empty set can't make the test pass.
            Assert.That(
                xlfIds,
                Does.Contain("ErrorReport.Pre10WindowsNotSupported"),
                $"Did not find the expected XLF entries in {englishDir}"
            );
            Assert.That(xlfIds.Count, Is.GreaterThan(1000));

            var bloomExeDir = Path.Combine(
                FileLocationUtilities.DirectoryOfApplicationOrSolution,
                "src",
                "BloomExe"
            );
            Assert.That(
                Directory.Exists(bloomExeDir),
                Is.True,
                $"Could not find the BloomExe source at {bloomExeDir}"
            );

            var calls = GetLiteralGetStringIds(bloomExeDir).ToList();
            // Sanity check that the scan finds calls at all.
            Assert.That(calls.Count, Is.GreaterThan(300), "The scan found too few GetString calls");

            var missing = calls
                .Where(c => !xlfIds.Contains(c.Id) && !s_allowedMissingIds.Contains(c.Id))
                .Select(c => $"{c.Id}  ({c.Location})")
                .ToList();
            Assert.That(
                missing,
                Is.Empty,
                "These ids are passed to LocalizationManager.GetString but are in no en/*.xlf file, so they can never be translated:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, missing)
            );
        }

        /// <summary>
        /// The ids of all the trans-units in the xlf files in the given folder. This includes
        /// trans-units inside XML comments: Bloom.xlf comments out a block of Team Collection
        /// entries on purpose, to keep them from translators until the feature settles, so code
        /// that asks for one of those ids is not a mistake.
        /// </summary>
        private static HashSet<string> GetXlfIds(string folder)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(folder, "*.xlf"))
            {
                var doc = XDocument.Load(path);
                foreach (var unit in doc.Descendants().Where(e => e.Name.LocalName == "trans-unit"))
                    ids.Add((string)unit.Attribute("id"));
                foreach (var comment in doc.DescendantNodes().OfType<XComment>())
                {
                    foreach (Match match in s_idInComment.Matches(comment.Value))
                        ids.Add(match.Groups["id"].Value);
                }
            }
            return ids;
        }

        /// <summary>
        /// Finds each literal id passed to LocalizationManager.GetString in the .cs files under
        /// the given folder, ignoring calls that are commented out and ids built at runtime.
        /// </summary>
        private static IEnumerable<(string Id, string Location)> GetLiteralGetStringIds(
            string folder
        )
        {
            foreach (var path in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var text = BlankOutComments(File.ReadAllText(path));
                // Matches come in file order, so count lines forward from the previous match.
                var lineNumber = 1;
                var countedTo = 0;
                foreach (Match match in s_getStringCall.Matches(text))
                {
                    for (; countedTo < match.Index; countedTo++)
                    {
                        if (text[countedTo] == '\n')
                            lineNumber++;
                    }
                    yield return (
                        match.Groups["id"].Value,
                        $"{Path.GetFileName(path)}:{lineNumber}"
                    );
                }
            }
        }

        [TestCase("a(); // x(\"id\")", "a();           ")]
        [TestCase("a(); /* x(\"id\") */ b();", "a();               b();")]
        [TestCase("/* one\ntwo */b();", "      \n      b();")]
        [TestCase("s = \"//*[@a]\"; b(); /* c */", "s = \"//*[@a]\"; b();        ")]
        [TestCase("s = @\"a\\\"\"/*\"; b();", "s = @\"a\\\"\"/*\"; b();")]
        [TestCase("s = \"a\\\"//\"; b();", "s = \"a\\\"//\"; b();")]
        [TestCase("c = '\"'; b(); // d", "c = '\"'; b();     ")]
        public void BlankOutComments_BlanksOnlyRealComments(string code, string expected)
        {
            Assert.That(BlankOutComments(code), Is.EqualTo(expected));
        }

        /// <summary>
        /// Replaces the text of // and /* */ comments with spaces, keeping newlines so that line
        /// numbers still match the file. String and character literals are skipped, so an XPath
        /// such as "//*[@class]" is not taken for a comment. (This does not handle raw string
        /// literals or strings nested inside interpolation holes, which BloomExe doesn't need.)
        /// </summary>
        private static string BlankOutComments(string text)
        {
            // Matching literals as well as comments lets the regex step over a literal whole, so
            // nothing inside it can start a comment; only the comment matches get blanked.
            return s_literalOrComment.Replace(
                text,
                m => m.Value[0] == '/' ? Regex.Replace(m.Value, @"[^\n]", " ") : m.Value
            );
        }

        // In order: a verbatim string (quotes doubled), a regular string, a char literal (both
        // with backslash escapes), a line comment, a block comment.
        private static readonly Regex s_literalOrComment = new Regex(
            @"@""(?:[^""]|"""")*""|""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'|//[^\n]*|/\*.*?\*/",
            RegexOptions.Compiled | RegexOptions.Singleline
        );
    }
}
