using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Provide values needed by the Book Info Indicator
    /// </summary>
    public class IndicatorInfoApi
    {
        private readonly CollectionModel _collectionModel;
        private readonly BookSelection _bookSelection;

        public IndicatorInfoApi(CollectionModel collectionModel, BookSelection bookSelection)
        {
            this._collectionModel = collectionModel;
            this._bookSelection = bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("book/indicatorInfo", HandleIndicatorInfo, false);
        }

        private void HandleIndicatorInfo(ApiRequest request)
        {
            switch (request.HttpMethod)
            {
                case HttpMethods.Get:
                    BookInfo bookInfo = null;
                    BookCollection collection;
                    if (GetBookInfo(request, out bookInfo, out collection))
                    {
                        var firstPossiblyOffendingCssFile = "";
                        if (bookInfo.AppearanceSettings.IsInitialized)
                        {
                            firstPossiblyOffendingCssFile = bookInfo
                                .AppearanceSettings
                                .FirstPossiblyOffendingCssFile;
                        }
                        else
                        {
                            // There's a race condition I haven't found a way to prevent between when the UI asks
                            // for the indicator info and when we initialize the settings as part of bringing the
                            // selected book up to date. So we'll just wait a bit and try again.
                            Application.Idle += UpdateIndicatorInfo;
                        }

                        var data = new
                        {
                            id = bookInfo.Id,
                            factoryInstalled = collection.IsFactoryInstalled,
                            cssThemeName = bookInfo.AppearanceSettings.CssThemeName,
                            firstPossiblyOffendingCssFile = firstPossiblyOffendingCssFile,
                            //substitutedCssFile = bookInfo.AppearanceSettings.SubstitutedCssFile,
                            path = bookInfo.FolderPath,
                        };
                        request.ReplyWithJson(data);
                    }
                    else
                    {
                        request.ReplyWithJson(
                            new
                            {
                                // user won't see this message, the UI just sees that there is an error and hides the indicator
                                error = "Could not find a book with that id."
                            }
                        );
                        return;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void UpdateIndicatorInfo(object sender, EventArgs e)
        {
            Debug.WriteLine("Needed to retry UpdateIndicatorInfo");
            Application.Idle -= UpdateIndicatorInfo;
            NotifyIndicatorInfoChanged();
        }

        private bool GetBookInfo(
            ApiRequest request,
            out BookInfo bookInfoOut,
            out BookCollection collectionOut
        )
        {
            var id = request.RequiredParam("id").Trim();
            BookInfo bookInfo = null;
            // get the book and collection info by looking in each of the collections in the _collectionModel for the book with this id
            var collection = _collectionModel
                .GetBookCollections()
                .FirstOrDefault(c =>
                {
                    var bi = c.GetBookInfoById(id);
                    if (bi != null)
                        bookInfo = bi;
                    return bi != null;
                });
            if (_bookSelection.CurrentSelection?.BookInfo?.Id == id)
                bookInfo = _bookSelection.CurrentSelection.BookInfo; // May be more initialized than the one in the collection.
            bookInfoOut = bookInfo;
            collectionOut = collection;

            return bookInfo != null;
        }

        public static void NotifyIndicatorInfoChanged()
        {
            BloomWebSocketServer.Instance?.SendEvent("book", "indicatorInfo");
        }
    }
}
