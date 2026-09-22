using Bloom.Edit;
using NUnit.Framework;

namespace BloomTests.Edit
{
    /// <summary>
    /// Tests for EditingView.ClipboardContentsSuggestImage, which decides (when the clipboard
    /// yielded no loadable image) whether the user nevertheless probably intended to paste an
    /// image, so we can show the more helpful "Bloom failed to interpret..." message instead of
    /// the generic "copy an image onto your clipboard first" one.
    /// </summary>
    [TestFixture]
    public class EditingViewTests
    {
        [TestCase("corrupt image.png")]
        [TestCase("photo.JPG")]
        [TestCase("drawing.webp")]
        public void ClipboardContentsSuggestImage_FileDropWithImageExtension_ReturnsTrue(
            string fileName
        )
        {
            var paths = new[]
            {
                @"C:\Users\someone\Documents\notes.txt",
                @"C:\Users\someone\Downloads\" + fileName,
            };
            Assert.That(EditingView.ClipboardContentsSuggestImage(paths, null), Is.True);
        }

        [Test]
        public void ClipboardContentsSuggestImage_FileDropWithoutImageExtension_ReturnsFalse()
        {
            var paths = new[] { @"C:\Users\someone\Documents\report.docx" };
            Assert.That(EditingView.ClipboardContentsSuggestImage(paths, null), Is.False);
        }

        [TestCase(@"C:\pictures\bird.png")]
        [TestCase(@"""C:\pictures\my bird.jpg""")] // Explorer "Copy as path" wraps in quotes
        [TestCase("https://example.com/images/bird.gif")]
        [TestCase("https://example.com/images/bird.png?width=800")] // URL query string ignored
        [TestCase("https://example.com/images/bird.gif#preview")] // URL fragment ignored
        [TestCase(@"C:\pictures\my#photo.png")] // '#' is legal in a Windows file name
        [TestCase(@"  C:\pictures\bird.tif  ")]
        public void ClipboardContentsSuggestImage_TextLooksLikeImagePath_ReturnsTrue(string text)
        {
            Assert.That(EditingView.ClipboardContentsSuggestImage(null, text), Is.True);
        }

        [TestCase("Just some ordinary text somebody copied")]
        [TestCase(@"C:\documents\story.docx")]
        [TestCase("https://example.com/some/page")]
        [TestCase("")]
        [TestCase(null)]
        public void ClipboardContentsSuggestImage_TextNotImageLike_ReturnsFalse(string text)
        {
            Assert.That(EditingView.ClipboardContentsSuggestImage(null, text), Is.False);
        }

        [Test]
        public void ClipboardContentsSuggestImage_NoFilesNoText_ReturnsFalse()
        {
            Assert.That(EditingView.ClipboardContentsSuggestImage(null, null), Is.False);
        }
    }
}
