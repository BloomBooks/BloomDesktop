using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Publish.Video
{
    public enum Codec
    {
        H264, // h.264
        H263, // h.263
        MP3,
    }

    internal static class CodecUtils
    {
        /// <summary>
        /// Returns the default extension Bloom will use for this codec
        /// </summary>
        /// <remarks>If the calling code wants to use an alternate extension/file type, that's fine; the caller should just manually determine it instead of using the results of this function.</remarks>
        /// <param name="codec"></param>
        /// <returns>The extension, including the leading period. Example: "mp4"</returns>
        /// <exception cref="NotImplementedException"></exception>
        public static string ToExtension(this Codec codec)
        {
            switch (codec)
            {
                case Codec.H263:
                    return ".3gp";
                case Codec.H264:
                    return ".mp4";
                case Codec.MP3:
                    return ".mp3";
                default:
                    throw new NotImplementedException(
                        $"CodecToExtension for codec {codec.ToString()} is not supported yet"
                    );
            }
        }
    }
}
