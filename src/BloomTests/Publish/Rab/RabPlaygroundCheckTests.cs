using System;
using Bloom.Book;
using Bloom.Publish.Rab;
using BloomTests.Book;
using NUnit.Framework;

namespace BloomTests.Publish.Rab
{
    /// <summary>
    /// Publish > Apps must refuse to put a book made from the Playground template into an app (BL-16855),
    /// as every other publish path refuses to publish such a book.
    /// </summary>
    [TestFixture]
    public class RabPlaygroundCheckTests : BookTestsBase
    {
        private const string kPlaygroundTemplateId = BookInfo.kPlaygroundTemplateId;

        protected override string GetTestFolderName() => "RabPlaygroundCheckTests";

        [Test]
        public void EnsureNoPlaygroundBooks_PlaygroundBook_ThrowsNamingBook()
        {
            var book = CreateBook();
            book.BookInfo.BookLineage = kPlaygroundTemplateId;
            // Sanity check that the lineage really makes this a Playground book.
            Assert.That(book.IsPlayground, Is.True);

            var exception = Assert.Throws<ApplicationException>(() =>
                RabProjectService.EnsureNoPlaygroundBooks(
                    new[] { book.BookInfo },
                    bookInfo => "My Playground"
                )
            );

            Assert.That(exception.Message, Does.Contain("Playground template"));
            Assert.That(exception.Message, Does.Contain("My Playground"));
        }

        [Test]
        public void EnsureNoPlaygroundBooks_OrdinaryBook_DoesNotThrow()
        {
            var book = CreateBook();
            Assert.That(book.IsPlayground, Is.False);

            Assert.DoesNotThrow(() =>
                RabProjectService.EnsureNoPlaygroundBooks(
                    new[] { book.BookInfo },
                    bookInfo =>
                    {
                        Assert.Fail("Titles should only be looked up for Playground books.");
                        return "";
                    }
                )
            );
        }
    }
}
