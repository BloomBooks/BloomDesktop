using System;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Provide values needed by the Book Info Indicator
	/// </summary>
	public class OtherBookInfoApi
	{
		private readonly CollectionModel _collectionModel;

		public OtherBookInfoApi(CollectionModel collectionModel)
		{
			this._collectionModel = collectionModel;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("book/otherInfo", HandleBookInfo,false);
		}

		private void HandleBookInfo(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					var bookInfo = GetBookInfo(request);
					if(bookInfo == null)
					{
						request.ReplyWithJson(new {
							// user won't see this message, the UI just sees that there is an error, which for now is fine, since we are
							// only needing this with books that are in the editable collection, not the source books.
							error = "The editable collection does not have a book with that id." });
						return;
					}
					var data = new
					{
						id = bookInfo.Id,
						// Note, sometimes CssThemeWeWillActuallyUse just hasn't been computed yet. For now, we're going to live with that.
						// The UI will just not list it.
						cssThemeWeWillActuallyUse = bookInfo.AppearanceSettings.CssThemeWeWillActuallyUse ?? "",
						firstPossiblyLegacyCss = bookInfo.AppearanceSettings.FirstPossiblyLegacyCss,
						path = bookInfo.FolderPath,

					};
					request.ReplyWithJson(data);
					break;

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private BookInfo GetBookInfo(ApiRequest request)
		{
			var id = request.RequiredParam("id").Trim();
			BookInfo bookInfo=null;
			// get the book info by looking in each of the collections in the _collectionModel for the book with this id
			var collection = _collectionModel.GetBookCollections().FirstOrDefault(c => {
					var bi = c.GetBookInfoById(id);
					if (bi != null)
						bookInfo = bi;
					return bi != null;
			});
			return bookInfo;
		}
	}
}
