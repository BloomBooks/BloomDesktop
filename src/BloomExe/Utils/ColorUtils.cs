using System;
using System.Drawing;
using Bloom.ImageProcessing;

namespace Bloom.Utils
{
    public class ColorUtils
    {
        /// <summary>
        /// Check whether the cover color is dark enough to need changing for some logos.
        /// </summary>
        /// <remarks>
        /// See https://www.w3.org/TR/WCAG20-TECHS/G17.html#G17-procedure
        /// 7 is the recommended minimal contrast for accessibility.  We can't guarantee
        /// this, but we can guarantee that the contrast is better than it would be if we
        /// ignored the cover color.
        /// </remarks>
        public static bool IsDark(string coverColor)
        {
            if (ImageUtils.TryCssColorFromString(coverColor, out Color color))
            {
                var R255 = color.R;
                var R = AdjustColorForLuminance(color.R);
                var G = AdjustColorForLuminance(color.G);
                var B = AdjustColorForLuminance(color.B);
                var luminance = 0.2126 * R + 0.7152 * G + 0.0722 * B;

                var blackLuminance = 0.0;   // we know this is 0, almost by definition.
                var contrastToBlack = (luminance + 0.05) / (blackLuminance + 0.05);

                var white = Color.White;
                var Rw = AdjustColorForLuminance(white.R);
                var Gw = AdjustColorForLuminance(white.G);
                var Bw = AdjustColorForLuminance(white.B);
                var whiteLuminance = 0.2126 * Rw + 0.7152 * Gw + 0.0722 * Bw;
                var contrastToWhite = (whiteLuminance + 0.05) / (luminance + 0.05);

                return contrastToBlack < contrastToWhite;
            }
            return false;
        }

        private static double AdjustColorForLuminance(byte byteValue)
        {
            var value = byteValue / 255.0;
            if (value < 0.03928)
                value = value / 12.92;
            else
                value = Math.Pow((value + 0.055) / 1.055, 2.4);
            return value;
        }
    }
}
