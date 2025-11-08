- [ ] HandleBookFileRequest doesn't belong in PageListApi.cs

# Later
- [ ] Fix CSS in pages (see this with ./show.sh )
* in the bloom pageThumbnailList, we add http://localhost:8089/bloom/bookEdit/pageThumbnailList/pageThumbnailList.css. How do we reference that from this component? We need basePage.css, previewMode.css.
*  appearance.css comes from the book itself.

# Low Priority
- [ ] When the URL starts with "\bloom", don't let the user edit that. They can select it and copy it, they can delete the url, they can paste a new url, but they can't edit it.


## Optimization for large books

