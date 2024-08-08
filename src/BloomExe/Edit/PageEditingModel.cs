using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.web.controllers;
using SIL.IO;
using SIL.Progress;
using SIL.Windows.Forms.ImageToolbox;
using static Bloom.Edit.PageEditingModel;
using Application = System.Windows.Forms.Application;

namespace Bloom.Edit
{
    // enhance: this is really just a single static method, so the file should be renamed
    public static class PageEditingModel
    {
        // NB: don't rename any of this without also changing the javascript recipient
        public class ImageInfoForJavascript
        {
            public string imageId;
            public string src;
            public string copyright;
            public string creator;
            public string license;
        }

        public static ImageInfoForJavascript ChangePicture(
            string bookFolderPath,
            string imageId,
            UrlPathString priorImageSrc,
            PalasoImage imageInfo
        )
        {
            var isSameFile = IsSameFilePath(bookFolderPath, priorImageSrc, imageInfo);
            var imageFileName = ImageUtils.ProcessAndSaveImageIntoFolder(
                imageInfo,
                bookFolderPath,
                isSameFile
            );
            try
            {
                ImageUtils.SaveImageMetadata(imageInfo, Path.Combine(bookFolderPath, imageFileName));
            }
            catch (Exception e)
            {
                ImageUtils.ReportImageMetadataProblem(Path.Combine(bookFolderPath, imageFileName), e);
            }
            return new ImageInfoForJavascript()
            {
                imageId = imageId,
                src = UrlPathString.CreateFromUnencodedString(imageFileName).UrlEncoded,
                copyright = imageInfo.Metadata.CopyrightNotice ?? "",
                creator = imageInfo.Metadata.Creator ?? "",
                license = imageInfo.Metadata.License?.ToString() ?? ""
            };
        }

        /// <summary>
        /// Check whether the new image file is the same as the one we already have chosen.
        /// (or at least the same pathname in the filesystem)
        /// </summary>
        /// <remarks>
        /// See https://silbloom.myjetbrains.com/youtrack/issue/BL-2776.
        /// If the user goes out of his way to choose the exact same picture file from the
        /// original location again, a copy will still be created with a slightly revised
        /// name.  Cropping a picture also results in a new copy of the file with the
        /// revised name.  We still need a tool to remove unused picture files from a
        /// book's folder.  (ie, BL-2351)
        /// </remarks>
        private static bool IsSameFilePath(
            string bookFolderPath,
            UrlPathString src,
            PalasoImage imageInfo
        )
        {
            if (src != null)
            {
                var path = Path.Combine(bookFolderPath, src.PathOnly.NotEncoded);
                if (path == imageInfo.OriginalFilePath)
                    return true;
            }
            return false;
        }
    }
}
