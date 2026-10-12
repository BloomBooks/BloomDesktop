using System.IO;

namespace Bloom.ImageProcessing
{
    /// <summary>
    /// Works around a TagLib# bug that makes every image metadata read or write fail on a computer
    /// whose regional format is Thai (and probably Lao) (BL-16926).
    ///
    /// When TagLib.File.Create is not given a mime type, TagLib# works one out from the file name
    /// with a culture-sensitive <c>LastIndexOf(".")</c>. Under Thai collation in ICU the period is
    /// ignorable, so the search matches at the very end of the name, the extension comes out empty,
    /// the mime type is <c>taglib/</c>, and TagLib# throws UnsupportedFormatException. libpalaso's
    /// ClearShare metadata code and Bloom's own code both call TagLib.File.Create(path), so rather
    /// than fixing each caller we register a FileTypeResolver, TagLib#'s own extension point, which
    /// sees every Create in the process. It redoes the extension lookup ordinally and hands back to
    /// TagLib# with the right mime type. Once a TagLib# release does an ordinal search itself
    /// (https://github.com/mono/taglib-sharp/issues/380, fixed by PR 381), this can go.
    /// </summary>
    public static class TagLibCultureFix
    {
        private static bool _registered;

        /// <summary>
        /// Registers the resolver with TagLib#. Call it once at startup, before anything touches
        /// image metadata; calling it again does nothing.
        /// </summary>
        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;
            TagLib.File.AddFileTypeResolver(ResolveWithOrdinalExtension);
        }

        /// <summary>
        /// If TagLib# came up with a mime type it does not know but the file name has an extension
        /// that it does know, create the file with the mime type for that extension. Otherwise
        /// return null, which tells TagLib# to carry on as it would have.
        /// </summary>
        private static TagLib.File ResolveWithOrdinalExtension(
            TagLib.File.IFileAbstraction abstraction,
            string mimetype,
            TagLib.ReadStyle style
        )
        {
            if (TagLib.FileTypes.AvailableTypes.ContainsKey(mimetype))
                return null;
            // Path.GetExtension searches for the period ordinally, whatever the culture.
            var extension = Path.GetExtension(abstraction.Name);
            if (string.IsNullOrEmpty(extension))
                return null;
            var correctMimetype = "taglib/" + extension.Substring(1).ToLowerInvariant();
            if (
                correctMimetype == mimetype
                || !TagLib.FileTypes.AvailableTypes.ContainsKey(correctMimetype)
            )
                return null;
            return TagLib.File.Create(abstraction, correctMimetype, style);
        }
    }
}
