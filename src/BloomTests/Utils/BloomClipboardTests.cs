using Bloom.Utils;
using NUnit.Framework;

namespace BloomTests.Utils
{
    [TestFixture]
    public class BloomClipboardTests
    {
        // Windows programs put CRLF on the clipboard, and the .NET read hands it back verbatim --
        // unlike the browser's own clipboard read, which the front end used before BloomClipboard
        // existed and which normalized line endings. Pasted text goes straight into the editor,
        // where a stray CR can surface as an extra line break, so this normalizing is what keeps a
        // multi-line paste looking the way it did.
        [TestCase("one\r\ntwo", "one\ntwo", TestName = "NormalizeLineEndings_Crlf_BecomesLf")]
        [TestCase("one\rtwo", "one\ntwo", TestName = "NormalizeLineEndings_LoneCr_BecomesLf")]
        [TestCase("one\ntwo", "one\ntwo", TestName = "NormalizeLineEndings_AlreadyLf_Unchanged")]
        [TestCase(
            "a\r\nb\rc\nd",
            "a\nb\nc\nd",
            TestName = "NormalizeLineEndings_Mixed_AllBecomeLf"
        )]
        [TestCase("no breaks", "no breaks", TestName = "NormalizeLineEndings_NoBreaks_Unchanged")]
        [TestCase("", "", TestName = "NormalizeLineEndings_Empty_StaysEmpty")]
        public void NormalizeLineEndings_ProducesLfOnly(string input, string expected)
        {
            Assert.That(BloomClipboard.NormalizeLineEndings(input), Is.EqualTo(expected));
        }

        // An unreadable clipboard is reported by returning false, and callers then treat the text
        // as empty; a null must not escape as null and trip them up.
        [Test]
        public void NormalizeLineEndings_Null_GivesEmptyString()
        {
            Assert.That(BloomClipboard.NormalizeLineEndings(null), Is.EqualTo(""));
        }

        // Guards the CRLF pair against being turned into two breaks by a naive fix.
        [Test]
        public void NormalizeLineEndings_CrlfPair_DoesNotBecomeTwoBreaks()
        {
            Assert.That(BloomClipboard.NormalizeLineEndings("a\r\n\r\nb"), Is.EqualTo("a\n\nb"));
        }
    }
}
