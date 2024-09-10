using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish.BloomLibrary;
using BloomTemp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Publish
{
    public class BloomLibraryPublishModelTests
    {
        [Test]
        public void SortConflictingBooksFromServer_JustOne_ReturnsWithoutCallingFunction()
        {
            var book1 = JObject.Parse(
                @"
{
      ""id"": ""OOgCG25FoW"",
      ""titleFromUpload"": ""hello world"",
      ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24cf"",
      ""uploader"": {

        ""email"": ""bob.someone@example.com"",
      },
      
    }
"
            );
            var booksIn = new[] { book1 };
            var books = BloomLibraryPublishModel.SortConflictingBooksFromServer(
                booksIn,
                "",
                "",
                "",
                (id) =>
                {
                    Assert.Fail("Should not call function for one book");
                    return true;
                }
            );
            Assert.That(books[0], Is.EqualTo(book1));
            Assert.That(books.Length, Is.EqualTo(1));
        }

        [Test]
        public void SortConflictingBooksFromServer_Several_ReturnsExpectedSequence()
        {
            // Should be first when we have a downloadForEdit file.
            // We make it the book whose known databaseId and instanceId this collection was made for,
            // though it does not have the expected phash, so when we don't have the file, it will be a
            // peer of rightUserWrongPhash
            var matchesCollection = JObject.Parse(
                @"
{
      ""id"": ""matchesCollection"",
      ""titleFromUpload"": ""hello world"",
      ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""email"": ""bob.someone@example.com"",
      },
    ""phashOfFirstContentImage"": ""hash2"",
      
    }
"
            );

            // Comes second because it is uploaded by the current user and has the right phash.
            // (Or first when we don't have the downloadForEdit file)
            var rightUserAndPhash = JObject.Parse(
                @"
{
      ""id"": ""rightUserAndPhash"",
      ""titleFromUpload"": ""hello world"",
      ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        
        ""email"": ""bob.someone@example.com"",
      },
      ""phashOfFirstContentImage"": ""hashWeWant"",
    }
"
            );

            // Comes third because it is not the collection book and has the wrong phash, but belongs to the right user
            // (Or second when we don't have the downloadForEdit file, because it is before matchesCollection in the input.)
            var rightUserWrongPhash = JObject.Parse(
                @"
{
      ""id"": ""rightUserWrongPhash"",
      ""titleFromUpload"": ""hello world"",
      ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        
        ""email"": ""bob.someone@example.com"",
      },
      ""phashOfFirstContentImage"": ""hash3"",
    }
"
            );

            // Should be fourth. It has a different uploader and is not the collection book,
            // but it has the same phash and we're allowed to edit it
            var editableRightPhash = JObject.Parse(
                @"
{
      ""id"": ""editableRightPhash"",
      ""titleFromUpload"": ""hello world"",
      ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""email"": ""fred_james@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hashWeWant"",
      
    }
"
            );

            // Should be fifth. It's not the collection book and not uploaded by the current user
            // and has the wrong phash, but it is one we can edit.
            var editableWrongPhash = JObject.Parse(
                @"
{
      ""id"": ""editableWrongPhash"",
      ""titleFromUpload"": ""hello world"",
      ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""email"": ""friend@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hash2"",
      
    }
"
            );

            // Should be next to last. everything about it is wrong except the instanceId and the phash
            var onlyPhashRight = JObject.Parse(
                @"
{
      ""id"": ""onlyPhashRight"",
      ""titleFromUpload"": ""hello world"",
      ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""email"": ""joe_blow@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hashWeWant"",
      
    }
"
            );
            // Should be last. everything about it is wrong (except the instanceId)
            var nothingRight = JObject.Parse(
                @"
{
      ""id"": ""nothingRight"",
      ""titleFromUpload"": ""hello world"",
      ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""email"": ""sally_problem@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hash2"",
      
    }
"
            );

            using (
                var tempFolder = new TemporaryFolder(
                    "SortConflictingBooksFromServer_Several_ReturnsExpectedSequence"
                )
            )
            {
                var book2FolderPath = Path.Combine(tempFolder.FolderPath, "hello world");
                var downloadForEditPath = Path.Combine(
                    tempFolder.FolderPath,
                    BloomLibraryPublishModel.kNameOfDownloadForEditFile
                );
                RobustFile.WriteAllText(
                    downloadForEditPath,
                    @"{""databaseId"": ""matchesCollection"", ""instanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",""bookFolder"":"""
                        + book2FolderPath.Replace("\\", "/")
                        + @"""}"
                );
                var booksIn = new[]
                {
                    rightUserWrongPhash,
                    rightUserAndPhash,
                    matchesCollection,
                    nothingRight,
                    onlyPhashRight,
                    editableWrongPhash,
                    editableRightPhash
                };

                var canUpload = (string id) =>
                {
                    switch (id)
                    {
                        case "editableWrongPhash":
                        case "editableRightPhash":
                            return true;
                        case "matchesCollection":
                        case "rightUserWrongPhash":
                        case "rightUserAndPhash":
                            Assert.Fail(
                                "Should not need to ask permissions on the books with the right uploader"
                            );
                            return true;
                        case "nothingRight":
                        case "onlyPhashRight":
                            return false;
                        default:
                            Assert.Fail("Unexpected id");
                            return false;
                    }
                };

                var books = BloomLibraryPublishModel.SortConflictingBooksFromServer(
                    booksIn,
                    book2FolderPath,
                    "bob.someone@example.com",
                    "hashWeWant",
                    canUpload
                );
                Assert.That(books.Length, Is.EqualTo(7));
                Assert.That(books[0], Is.EqualTo(matchesCollection));
                Assert.That(books[1], Is.EqualTo(rightUserAndPhash));
                Assert.That(books[2], Is.EqualTo(rightUserWrongPhash));
                Assert.That(books[3], Is.EqualTo(editableRightPhash));
                Assert.That(books[4], Is.EqualTo(editableWrongPhash));
                Assert.That(books[5], Is.EqualTo(onlyPhashRight));
                Assert.That(books[6], Is.EqualTo(nothingRight));

                // now try again with the downloadForEdit file missing
                RobustFile.Delete(downloadForEditPath);

                books = BloomLibraryPublishModel.SortConflictingBooksFromServer(
                    booksIn,
                    book2FolderPath,
                    "bob.someone@example.com",
                    "hashWeWant",
                    canUpload
                );
                Assert.That(books.Length, Is.EqualTo(7));
                // matchesCollection doesn't any more, because we deleted the file that tells us it's the collection book.
                // Since it's phash is wrong, it's on a par with rightUserWrongPhash.
                // For things that are equal, we keep the original order.
                Assert.That(books[0], Is.EqualTo(rightUserAndPhash));
                Assert.That(books[1], Is.EqualTo(rightUserWrongPhash));
                Assert.That(books[2], Is.EqualTo(matchesCollection));
                Assert.That(books[3], Is.EqualTo(editableRightPhash));
                Assert.That(books[4], Is.EqualTo(editableWrongPhash));
                Assert.That(books[5], Is.EqualTo(onlyPhashRight));
                Assert.That(books[6], Is.EqualTo(nothingRight));
            }
        }

        [Test]
        public void ConvertLanguageCodesAndPointersToNames_Works()
        {
            dynamic data = JObject.Parse(
                @"{""languages"":[
        {
        
          ""tag"": ""en"",
          ""name"": ""English""
        },
        {
          
          ""tag"": ""fr"",
          ""name"": ""Francais""
        },
        {
         
          ""tag"": ""abc"",
          ""name"": ""Sign1""
        },
        {
          
          ""tag"": ""de"",
          ""name"": ""German""
        }]}"
            );
            var databaseLanguages = data.languages;
            var langCodes = new[] { "fr", "en", "de", "tpi" };
            var collectionSettings = new CollectionSettings { Language1Tag = "en" };
            var dom = new HtmlDom(
                @"<html><head><div id='bloomDataDiv'>" + "</div></head><body></body></html>"
            );
            var bookData = new BookData(dom, collectionSettings, (x) => { });
            var ws = new WritingSystem(() => "en");
            collectionSettings.SignLanguage = ws;
            ws.Tag = "abc";
            ws.SetName("Sign", true);

            BloomLibraryPublishModel.ConvertLanguageListsToNames(
                langCodes,
                bookData,
                databaseLanguages,
                ws,
                out string[] newLanguages,
                out string[] existingLanguages
            );
            // There are four codes in langCodes, but sign language gets included also.
            Assert.That(newLanguages.Length, Is.EqualTo(5));
            // French is the default name for fr, but the name "from Blorg" is different, so show both
            Assert.That(newLanguages[0], Is.EqualTo("French/Francais"));
            Assert.That(newLanguages[1], Is.EqualTo("English"));
            Assert.That(newLanguages[2], Is.EqualTo("German"));
            Assert.That(newLanguages[3], Is.EqualTo("Tok Pisin"));
            // Here we gave the fake sign language a custom name, and since it is different from
            // the name in the "downloaded" object data, we show both.
            Assert.That(newLanguages[4], Is.EqualTo("Sign/Sign1"));

            // One for each language object in the 'downloaded' data.
            Assert.That(existingLanguages.Length, Is.EqualTo(4));
            Assert.That(existingLanguages[0], Is.EqualTo("English"));
            Assert.That(existingLanguages[1], Is.EqualTo("French/Francais"));
            Assert.That(existingLanguages[2], Is.EqualTo("Sign/Sign1"));
            Assert.That(existingLanguages[3], Is.EqualTo("German"));
        }
    }
}
