using System;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.web.controllers;
using Newtonsoft.Json.Linq;
using SIL.IO;

// BloomPlayer is a web app that can be used to read Bloom Books. We embed it in the Publish:BloomPub:Preview screen.
// Some requests the Player makes are unique to it, and so we handle them here.
static class ServerHandlerForBloomPlayer
{
    public static bool TryToHandle(
        Bloom.Api.IRequestInfo request,
        string currentCollectionFolderPath,
        string currentBookFolderPath
    )
    {
        // When we're previewing bloom-player, jumps between books get sent
        // as for example "/book/12345". We need to redirect that to something like "c:/bloom/books/crow/crow.htm"
        // That part is easy. But the browser will ask for resources like css and images, and we want them to come as
        // "/book/12345/whatever.css". I've found that the way to get that behavior is using url rewriting.
        if (request.LocalPathWithoutQuery.StartsWith("/book/"))
        {
            if (request.LocalPathWithoutQuery.IndexOf(".distribution") > 0)
            {
                // we normally don't have this file, and even if we do it's not useful in the preview
                // so avoid any errors by just returning an empty response
                request.WriteCompleteOutput("");
                return true;
            }
            var parts = request.LocalPathWithoutQuery.Substring(1).Split(new[] { '/' }, 3); // split into "book/", bookId, and remaining path
            if (parts.Length >= 2)
            {
                var bookId = parts[1];
                var remainingPath = parts.Length > 2 ? "/" + parts[2] : "";

                // enhance: could cache it
                var finder = new BookFinder(
                    new[]
                    {
                        // for now, we're only searching in this collection, but in theory there could be a need to support looking over other collections
                        currentCollectionFolderPath,
                    }
                );

                (var bookPath, var htmlFileName) = finder.TryFindBook(bookId);
                if (!string.IsNullOrEmpty(bookPath))
                {
                    // NOTICE, we're not trying to do anything at all with the target book to get it to look like a device-ready,
                    // published BloomPUB. So far as I can tell, it's not necessary for the task at hand, which is just to make
                    // sure that links jump to the right book. At the moment, in Bloom Editor, it doesn't matter that the book
                    // might not look just like it will once published. However, if we find
                    // we do need that, we have a PR that did a bunch of that work, which we could resurrect:
                    // https://github.com/BloomBooks/BloomDesktop/pull/6744

                    // If we're returning to the current book, we need to redirect to the staging folder,
                    // not the original folder, so that users don't get confused by a change in the
                    // xmatter.  See comments in BL-13881.
                    if (bookPath == currentBookFolderPath)
                    {
                        var bloomPubPath = Path.Combine(
                            Path.GetTempPath(),
                            PublishApi.kStagingFolder,
                            Path.GetFileName(bookPath)
                        );
                        if (Directory.Exists(bloomPubPath))
                            bookPath = bloomPubPath;
                    }
                    if (string.IsNullOrEmpty(remainingPath))
                    {
                        remainingPath = "/" + htmlFileName;
                    }
                    if (remainingPath.ToLower() == "/index.htm")
                    {
                        // bloom-player expects that every book has an "index.htm". But since we haven't taken the book through
                        // the publishing process, it may still have the original name, like "crow.htm". So we just redirect to the actual name.
                        remainingPath = "/" + htmlFileName;
                    }
                    var redirectUrl =
                        $"{BloomServer.ServerUrlWithBloomPrefixEndingInSlash}{bookPath.Replace('\\', '/')}{remainingPath}";
                    // keeping the redirect temporary in case that books moves during the life of the webview2 browser, which could become the whole bloom editor session
                    request.WriteRedirect(redirectUrl, false);
                    //Debug.WriteLine($"Redirecting {localPath} to {redirectUrl}");
                    return true;
                }
            }

            return false; // report some error? supposed to be a book id url, but we couldn't find a redirect?
        }
        return false;
    }
}

// a lightweight way to find a book and its html file for serving to Bloom Player

class BookFinder
{
    private string[] _containingFolders;

    public BookFinder(string[] containingFolders)
    {
        _containingFolders = containingFolders;
    }

    public (string folderPath, string htmlPath) TryFindBook(string bookId)
    {
        // enhance: cache the results of this search
        foreach (var folder in _containingFolders)
        {
            string folderPath = TryFindBookInFolder(folder, bookId);
            if (folderPath != null)
            {
                string htmlFileName = Directory
                    .GetFiles(folderPath, "*.htm")
                    .Select(Path.GetFileName)
                    .FirstOrDefault();

                return (folderPath, htmlFileName);
            }
        }
        return (null, null);
    }

    private string TryFindBookInFolder(string folder, string bookId)
    {
        if (!Directory.Exists(folder))
            return null;

        foreach (var childFolder in Directory.GetDirectories(folder))
        {
            var metaPath = Path.Combine(childFolder, "meta.json");
            if (!RobustFile.Exists(metaPath))
                continue;

            try
            {
                var json = JObject.Parse(RobustFile.ReadAllText(metaPath));
                var instanceId = json["bookInstanceId"]?.ToString();

                if (instanceId == bookId)
                {
                    return childFolder;
                }
            }
            catch (Exception)
            {
                // Skip invalid json files
                continue;
            }
        }
        return null;
    }
}
