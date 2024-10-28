using Bloom.Utils;
using NUnit.Framework;
using SIL.WritingSystems;

namespace BloomTests.Utils
{
    [TestFixture]
    class MiscUtilsTests
    {
        [Test]
        public void EscapeForCmd_DoubleQuotedString_WrappedInDoubleQuotes()
        {
            string inputCommand =
                "\"C:\\src\\Bloom Desktop 2\\output\\Debug\\Bloom.exe\" upload \"C:\\Bloom Collections\\Collection Name\" -u username@domain.com -d dev";
            var result = MiscUtils.EscapeForCmd(inputCommand);

            Assert.That(
                result,
                Is.EqualTo(
                    "\"\"C:\\src\\Bloom Desktop 2\\output\\Debug\\Bloom.exe\" upload \"C:\\Bloom Collections\\Collection Name\" -u username@domain.com -d dev\""
                )
            );
        }

        [Test]
        public void NormalizeLanguageTagCapitalization_Works()
        {
            if (!Sldr.IsInitialized)
                Sldr.Initialize();
            // Check that valid (normalized) language tags stay the same.
            var result = MiscUtils.NormalizeLanguageTagCapitalization("en");
            Assert.That(result, Is.EqualTo("en"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-US");
            Assert.That(result, Is.EqualTo("en-US"));
            // The IetfLanguageTag class is clever and removes the default script tag.
            // Hence, the tests use Cyrl instead of Latn for checking scripts.
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-Cyrl");
            Assert.That(result, Is.EqualTo("en-Cyrl"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-Cyrl-US");
            Assert.That(result, Is.EqualTo("en-Cyrl-US"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("kwy");
            Assert.That(result, Is.EqualTo("kwy"));

            // Check that valid language tags with incorrect capitalization are corrected.
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-us");
            Assert.That(result, Is.EqualTo("en-US"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-cyrl");
            Assert.That(result, Is.EqualTo("en-Cyrl"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-cyrl-us");
            Assert.That(result, Is.EqualTo("en-Cyrl-US"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("En-cyrl-Us");
            Assert.That(result, Is.EqualTo("en-Cyrl-US"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("EN-CYRL-us");
            Assert.That(result, Is.EqualTo("en-Cyrl-US"));
            // The very next test is the actual use case discovered in BL-14038.
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy");
            Assert.That(result, Is.EqualTo("kwy"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy-Cyrl");
            Assert.That(result, Is.EqualTo("kwy-Cyrl"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy-cyrl");
            Assert.That(result, Is.EqualTo("kwy-Cyrl"));

            // The following test the variant subtag.
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy-Cyrl-x-variant");
            Assert.That(result, Is.EqualTo("kwy-Cyrl-x-variant"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy-Cyrl-x-Variant");
            Assert.That(result, Is.EqualTo("kwy-Cyrl-x-Variant"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Qaa-x-Language");
            Assert.That(result, Is.EqualTo("qaa-x-Language"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("x-language");
            Assert.That(result, Is.EqualTo("x-language"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("x-Latn");
            Assert.That(result, Is.EqualTo("x-Latn"));

            // The following don't parse, so the original string is returned.
            result = MiscUtils.NormalizeLanguageTagCapitalization("En-");
            Assert.That(result, Is.EqualTo("En-"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("E");
            Assert.That(result, Is.EqualTo("E"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Four");
            Assert.That(result, Is.EqualTo("Four"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("nonsense");
            Assert.That(result, Is.EqualTo("nonsense"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("This is a test!");
            Assert.That(result, Is.EqualTo("This is a test!"));
        }
    }
}
