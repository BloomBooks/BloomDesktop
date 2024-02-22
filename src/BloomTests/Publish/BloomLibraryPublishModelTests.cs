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
      ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24cf"",
      ""uploader"": {
        ""objectId"": ""Vs8Ksc1BXV"",
        ""email"": ""john_thomson@sil.org"",
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
            // Comes third because it is not the collection book and has the wrong phash, but belongs to the right user
            var rightUserWrongPhash = JObject.Parse(
                @"
{
      ""id"": ""rightUserWrongPhash"",
      ""titleFromUpload"": ""hello world"",
      ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""objectId"": ""Vs8Ksc1BXV"",
        ""email"": ""john_thomson@sil.org"",
      },
      ""phashOfFirstContentImage"": ""hash3"",
    }
"
            );
            // Comes second because it is uploaded by the current user and has the right phash.
            var rightUserAndPhash = JObject.Parse(
                @"
{
      ""id"": ""rightUserAndPhash"",
      ""titleFromUpload"": ""hello world"",
      ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""objectId"": ""Vs8Ksc1BXV"",
        ""email"": ""john_thomson@sil.org"",
      },
      ""phashOfFirstContentImage"": ""hashWeWant"",
    }
"
            );
            // Should be first. We make it the book whose known objectId and instanceId this collection was made for,
            // though it does not have the expected phash
            var matchesCollection = JObject.Parse(
                @"
{
      ""id"": ""matchesCollection"",
      ""titleFromUpload"": ""hello world"",
      ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""objectId"": ""Vs8Ksc1BXV"",
        ""email"": ""john_thomson@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hash2"",
      
    }
"
            );
            // Should be last. everything about it is wrong (except the instanceId)
            var nothingRight = JObject.Parse(
                @"
{
      ""id"": ""nothingRight"",
      ""titleFromUpload"": ""hello world"",
      ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""objectId"": ""Vs8Ksc1BXV"",
        ""email"": ""sally_problem@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hash2"",
      
    }
"
            );
            // Should be fifth. everything about it is wrong except the instanceId and the phash
            var onlyPhashRight = JObject.Parse(
                @"
{
      ""id"": ""onlyPhashRight"",
      ""titleFromUpload"": ""hello world"",
      ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""objectId"": ""Vs8Ksc1BXV"",
        ""email"": ""joe_blow@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hashWeWant"",
      
    }
"
            );
            // Should be fourth. It's not the collection book and not uploaded by the current user
            // and has the wrong phash, but it is one we can edit.
            var editableWrongPhash = JObject.Parse(
                @"
{
      ""id"": ""editableWrongPhash"",
      ""titleFromUpload"": ""hello world"",
      ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""objectId"": ""Vs8Ksc1BXV"",
        ""email"": ""friend@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hash2"",
      
    }
"
            );
            // Should be third. It has a different uploader and is not the collection book,
            // but it has the same phash and we're allowed to edit it
            var editableRightPhash = JObject.Parse(
                @"
{
      ""id"": ""editableRightPhash"",
      ""titleFromUpload"": ""hello world"",
      ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",
      ""uploader"": {
        ""objectId"": ""abc"",
        ""email"": ""fred_james@sil.org"",
      },
    ""phashOfFirstContentImage"": ""hashWeWant"",
      
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
                File.WriteAllText(
                    downloadForEditPath,
                    @"{""databaseId"": ""matchesCollection"", ""bookInstanceId"": ""c2e3212c-ee59-4e75-bd98-41e4784e24ca"",""bookFolder"":"""
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

                var books = BloomLibraryPublishModel.SortConflictingBooksFromServer(
                    booksIn,
                    book2FolderPath,
                    "john_thomson@sil.org",
                    "hashWeWant",
                    (id) =>
                    {
                        switch (id)
                        {
                            case "rightUserWrongPhash":
                            case "rightUserAndPhash":
                            case "editableWrongPhash":
                            case "editableRightPhash":
                                return true; // Review: or should we not ask if it is the same user?
                            case "matchesCollection":
                                Assert.Fail(
                                    "Should not need to ask permissions on the collection book"
                                );
                                return true;
                            case "nothingRight":
                            case "onlyPhashRight":
                                return false;
                            default:
                                Assert.Fail("Unexpected id");
                                return false;
                        }
                    }
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
                    "john_thomson@sil.org",
                    "hashWeWant",
                    (id) =>
                    {
                        switch (id)
                        {
                            case "rightUserWrongPhash":
                            case "rightUserAndPhash":
                            case "editableWrongPhash":
                            case "editableRightPhash":
                                return true; // Review: or should we not ask if it is the same user?
                            case "matchesCollection":
                                return true;
                            case "nothingRight":
                            case "onlyPhashRight":
                                return false;
                            default:
                                Assert.Fail("Unexpected id");
                                return false;
                        }
                    }
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
          ""objectId"": ""2cy807OQoe"",
          ""tag"": ""en"",
          ""name"": ""English""
        },
        {
          ""objectId"": ""hfWljz3HL9"",
          ""tag"": ""fr"",
          ""name"": ""Francais""
        },
        {
          ""objectId"": ""hfWl3HL9"",
          ""tag"": ""abc"",
          ""name"": ""Sign1""
        },
        {
          ""objectId"": ""VUiYTJhOyJ"",
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
            var ws = new WritingSystem(-1, () => "en");
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
