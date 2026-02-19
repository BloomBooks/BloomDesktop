using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using L10NSharp;

namespace Bloom.Edit
{
    public partial class JpegWarningDialog
        : SIL.Windows.Forms.Miscellaneous.FormForUsingPortableClipboard
    {
        public JpegWarningDialog()
        {
            InitializeComponent();
            _warningText.Text = LocalizationManager
                .GetString(
                    "EditTab.JpegWarningDialog.WarningText",
                    @"The file you’ve chosen is a “JPEG” file. JPEG files are perfect for photographs and color artwork. However, JPEG files are a big problem for black and white line art. Problems include:
• Fuzziness and grey dots.
• Large file sizes, making the book hard to share.
• If there are many large JPEGs, Bloom may not have enough memory to make PDF files.

Note: Because JPEG is “lossy”, converting a JPEG to PNG, TIFF, or BMP actually makes things even worse. If this is black and white line art, you want to get an original scan in one of those formats.

Please select from one of the following, then click “OK”:"
                )
                .Replace("\r\n", "\n")
                .Replace("\n", Environment.NewLine);
        }

        private void _okButton_Click(object sender, EventArgs e)
        {
            DialogResult = _cancelRadioButton.Checked ? DialogResult.Cancel : DialogResult.OK;
            Close();
        }

        /// <summary>
        /// Tells whether the supplied image looks suspiciously like a file that
        /// would be better off as a PNG. Not particularly smart, but fast.
        /// </summary>
        public static Boolean ShouldWarnAboutJpeg(Image image)
        {
            var bmp = image as Bitmap;
            if (bmp == null)
                return false;
            //Note: currently I can't justify why initially I was testing for greyscale.... maybe the logic will return...
            return HasLotsOfWhiteSpace(bmp)
                && ( /*IsGreyScale(bmp) || */
                    !HasLotsOfColor(bmp)
                );
        }

        private static bool IsGreyScale(Bitmap bmp)
        {
            var sampleLinePercentages = new[] { 20, 50, 70 };
            return sampleLinePercentages.Any(sampleLine =>
                GetIsGrey(bmp, (int)(bmp.Height * (sampleLine / 100.0)))
            );
        }

        private static bool HasLotsOfColor(Bitmap bmp)
        {
            //what's a good threshold?
            //Higher than one would think, as we want to detect pictures that don't have color just because they were
            //mistakenly made into a jpeg.
            //A  drawing with just 4 colors had 62 once ran through jpeg.
            // Just turning the bloom placeholder flower into jpeg gives us 10 "colors" (different shades of grey)
            const int threshold = 100;
            var sampleLinePercentages = new[] { 20, 50, 70 };
            return sampleLinePercentages.Any(sampleLine =>
                GetNumberOfColors(bmp, (int)(bmp.Height * (sampleLine / 100.0))) > threshold
            );
        }

        private static bool HasLotsOfWhiteSpace(Bitmap bmp)
        {
            const double threshold = .5; // we'll warn if any of the samples we take of the image show > 50% white
            var sampleLinePercentages = new[] { 20, 50, 70 };
            return sampleLinePercentages.Any(sampleLine =>
                GetPercentWhiteOfLine(bmp, (int)(bmp.Height * (sampleLine / 100.0))) > threshold
            );
        }

        private static int GetNumberOfColors(Bitmap bmp, int lineNumber)
        {
            if (lineNumber >= bmp.Height)
                return 0; //guard against math errors by the caller

            var colors = new HashSet<int>();
            for (var x = 0; x < bmp.Width; x++)
            {
                var pixel = bmp.GetPixel(x, lineNumber);
                int pixelColor = pixel.ToArgb();
                if (!colors.Contains(pixelColor))
                {
                    colors.Add(pixelColor);
                }
            }
            return colors.Count;
        }

        private static bool GetIsGrey(Bitmap bmp, int lineNumber)
        {
            const int thresholdForGrey = 20;
            if (lineNumber >= bmp.Height)
                return false; //guard against math errors by the caller
            for (int x = 0; x < bmp.Width; x++)
            {
                var pixelColor = bmp.GetPixel(x, lineNumber);
                var rgbDelta =
                    Math.Abs(pixelColor.R - pixelColor.G)
                    + Math.Abs(pixelColor.G - pixelColor.B)
                    + Math.Abs(pixelColor.B - pixelColor.R);
                if (rgbDelta > thresholdForGrey)
                    return false;
            }
            return true;
        }

        private static double GetPercentWhiteOfLine(Bitmap bmp, int lineNumber)
        {
            if (lineNumber >= bmp.Height)
                return 0; //guard against math errors by the caller

            const int maxCombinedRgbValueToBeConsideredWhite = 3 * 253; // true white is 255. We'll count off-white as well
            int whiteCount = 0;
            for (int x = 0; x < bmp.Width; x++)
            {
                var pixelColor = bmp.GetPixel(x, lineNumber);
                //we'll add up the R, G, and B
                if (
                    (pixelColor.R + pixelColor.G + pixelColor.B)
                    > maxCombinedRgbValueToBeConsideredWhite
                )
                    ++whiteCount;
            }
            return ((double)whiteCount) / (double)bmp.Width;
        }
    }
}
