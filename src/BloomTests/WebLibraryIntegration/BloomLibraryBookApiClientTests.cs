using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Bloom.WebLibraryIntegration;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using NUnit.Framework;
using RestSharp;
using SIL.Code;

namespace BloomTests.WebLibraryIntegration
{
    [TestFixture]
    public class BloomLibraryBookApiClientTests
    {
        private BloomLibraryBookApiClientTestDouble _client;

        [SetUp]
        public void Setup()
        {
            _client = new BloomLibraryBookApiClientTestDouble();
        }

        // It would be nice to have some tests for the new FirebaseLogin code, too, but so far
        // we have not been able to get that code to run except after starting Bloom fully.

        // GetBookRecord success case is tested by BookUploadAndDownloadTests

        [Test]
        public void GetBookRecord_BookIsNotThere_Fails()
        {
            Assert.IsNull(_client.GetSingleBookRecord("notthere"));
        }

        //{"authors":["Heinrich Poschinger"],"categories":[],"description":"This is an EXACT reproduction of a book published before 1923. This IS NOT an OCR\"d book with strange characters, introduced typographical errors, and jumbled words. This book may have occasional imperfections such as missing or blurred pages, poor pictures, errant marks, etc. that were either part of the original artifact, or were introduced by the scanning process. We believe this work is culturally important, and despite the imperfections, have elected to bring it back into print as part of our continuing commitment to the preservation of printed works worldwide. We appreciate your understanding of the imperfections in the preservation process, and hope you enjoy this valuable book.","imageLinks":{"smallThumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=5&source=gbs_api","thumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=1&source=gbs_api"},"industryIdentifiers":[{"identifier":"1148362096","type":"ISBN_10"},{"identifier":"9781148362090","type":"ISBN_13"}],"language":"en","pageCount":270,"previewLink":"http://books.google.co.th/books?id=MxhvSQAACAAJ&dq=hamburger&hl=&cd=520&source=gbs_api","printType":"BOOK","publishedDate":"2010-04","publisher":"Nabu Press","title":"Fürst Bismarck und Seine Hamburger Freunde"}
    }

    public class BloomLibraryBookApiClientTestDouble : BloomLibraryBookApiClient
    {
        // Fake log in so that the unit tests can upload, since we haven't been able to get the new firebase login to work
        // except when Bloom is actually running.
        public void TestOnly_SetUserAccountInfo(string account)
        {
            Account = account;
            _authenticationToken = "fakeTokenForUnitTests"; //azure function will ignore auth token for unit tests
        }

        public void TestOnly_DeleteBook(string bookObjectId)
        {
            var request = MakeDeleteRequest(bookObjectId);
            AzureRestClient.Execute(request);
        }
    }
}
