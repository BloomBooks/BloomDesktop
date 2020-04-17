using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using Shipwreck.Phash.Bitmaps;
using Shipwreck.Phash.Imaging;

namespace Bloom.Book
{
	// These methods are adapted from the code in Shipwreck, which fails to handle 1-bit colormap PNG images properly
	// on both Windows and Linux, always giving them a phash of all zeroes on Windows (but a different constant value
	// on Linux).  Many if not all AOR images fall into this category.
	public static class PhashHelper
	{
		internal static Bitmap ToRgb24(this Bitmap bitmap)
		{
			if (bitmap.PixelFormat == PixelFormat.Format24bppRgb)
				return bitmap;
			Bitmap drawingBitmap = null;
			try
			{
				drawingBitmap = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
				drawingBitmap.SetResolution(bitmap.HorizontalResolution, bitmap.VerticalResolution);
				// Check for data from 1-bit colormap (Black & White) on Windows.
				// If data looks like R=G=B=0 everywhere and A has 2 values, then set each pixel from
				// the A value of the original bitmap.  [SLOW, but simple]
				if (IsAlphaChannelBlackAndWhite(bitmap))
				{
					SetPixelsFrom32bppArgbBWBitmap(bitmap, drawingBitmap);
				}
				// Check for data from 1-bit colormap (Black & White) on Linux.
				else if (bitmap.PixelFormat == PixelFormat.Format1bppIndexed)
				{
					SetPixelsFrom1bppIndexedBWBitmap(bitmap, drawingBitmap);
				}
				else
				{
					using (var graphics = Graphics.FromImage(drawingBitmap))
					{
						graphics.CompositingMode = CompositingMode.SourceCopy;
						graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

						graphics.DrawImage(bitmap, 0, 0);
					}
				}
				return drawingBitmap;
			}
			catch (Exception ex)
			{
				drawingBitmap?.Dispose();
				throw;
			}
		}

		/// <remarks>
		/// This method is essentially copied from Shipwreck.Phash.Bitmaps.  We need a
		/// copy to make sure it calls our version of ToRgb24().
		/// </remarks>
		public static ByteImage ToLuminanceImage(this Bitmap bitmap)
		{
			Bitmap bitmap24Rgb = null;
			try
			{
				bitmap24Rgb = ToRgb24(bitmap);
				var data = bitmap24Rgb.ToBytes();

				var r = new ByteImage(bitmap.Width, bitmap.Height);

				int bytesPerPixel = (Image.GetPixelFormatSize(bitmap24Rgb.PixelFormat) + ((sizeof(byte) * 8) - 1)) / (sizeof(byte) * 8);
				int strideDelta = bitmap24Rgb.GetStride() % (bitmap24Rgb.Width * bytesPerPixel);
				var yc = new Vector3(66, 129, 25);
				var i = 0;
				for (var dy = 0; dy < r.Height; dy++)
				{
					for (var dx = 0; dx < r.Width; dx++)
					{
						Vector3 sv;
						sv.Z = data[i++]; // B
						sv.Y = data[i++]; // G
						sv.X = data[i++]; //R
						r[dx, dy] = (byte)(((int)(Vector3.Dot(yc, sv) + 128) >> 8) + 16);
					}

					i += strideDelta;
				}
				return r;
			}
			finally
			{
				if (bitmap != bitmap24Rgb)
				{
					bitmap24Rgb?.Dispose();
				}
			}
		}

		private static bool IsAlphaChannelBlackAndWhite(Bitmap bitmap)
		{
			if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
				return false;
			// On Windows, 1-bit colormap PNG files generate 32bpp bitmaps where the B/W distinction
			// is carried in the A byte (0|255) and the other bytes are always 0.  Calling ToLuminanceImage()
			// on this directly would result in the image data bytes all being set to 16, and the computed
			// hash ending up all zero bytes.  So we record the color byte data enough to detect this situation.
			HashSet<byte> redValues = new HashSet<byte>();
			HashSet<byte> greenValues = new HashSet<byte>();
			HashSet<byte> blueValues = new HashSet<byte>();
			HashSet<byte> alphaValues = new HashSet<byte>();
			int bytesPerPixel = (Image.GetPixelFormatSize(bitmap.PixelFormat) + ((sizeof(byte) * 8) - 1)) / (sizeof(byte) * 8);
			int strideDelta = bitmap.GetStride() % (bitmap.Width * bytesPerPixel);
			var i = 0;
			var data = bitmap.ToBytes();
			for (var dy = 0; dy < bitmap.Height; dy++)
			{
				for (var dx = 0; dx < bitmap.Width; dx++)
				{
					blueValues.Add(data[i++]);
					greenValues.Add(data[i++]);
					redValues.Add(data[i++]);
					if (bytesPerPixel == 4)
						alphaValues.Add(data[i++]);
				}
				i += strideDelta;
			}
			return (redValues.Count == 1 && redValues.Contains(0) &&
					greenValues.Count == 1 && greenValues.Contains(0) &&
					blueValues.Count == 1 && blueValues.Contains(0) &&
					alphaValues.Count > 1);
		}

		private static void SetPixelsFrom32bppArgbBWBitmap(Bitmap bitmap, Bitmap bitmap24bpp)
		{
			var data = bitmap.ToBytes();
			int bytesPerPixel = (Image.GetPixelFormatSize(bitmap.PixelFormat) + ((sizeof(byte) * 8) - 1)) / (sizeof(byte) * 8);
			int strideDelta = bitmap.GetStride() % (bitmap.Width * bytesPerPixel);
			var i = 0;
			// We have a black and white image that is using only the A byte to signal values.
			// So we use the A byte to set the R,G,B bytes as well.
			for (var dy = 0; dy < bitmap.Height; dy++)
			{
				for (var dx = 0; dx < bitmap.Width; dx++)
				{
					i += 3;	// skip over B,G,R
					var alpha = data[i++];
					// OPTIMIZE: SetPixel is slow compared to setting bytes in an array.  Setting the byte array
					// used by ToLuminanceImage() directly would be faster.
					bitmap24bpp.SetPixel(dx, dy, Color.FromArgb(alpha, alpha, alpha, alpha));
				}
				i += strideDelta;
			}
		}

		private static void SetPixelsFrom1bppIndexedBWBitmap(Bitmap bitmap, Bitmap bitmap24bpp)
		{
			// On Linux, 1-bit colormap PNG files generate 1bpp bitmaps.  On translation to 24bpp bitmaps,
			// the same problem occurs as happens on Windows (A set, RGB left all zero).  For some reason,
			// the generated phash isn't all zeroes like on Windows, but it's probably constant for all
			// 1-bit colormap images.  So we fill in the 24bpp data explicitly.
			for (var dy = 0; dy < bitmap.Height; dy++)
			{
				for (var dx = 0; dx < bitmap.Width; dx++)
				{
					// OPTIMIZE: interpreting the bits in the input data bytes might be faster that using GetPixel.
					// Setting the bytes in the output byte array would certainly be faster than using SetPixel.
					var pixel = bitmap.GetPixel(dx,dy);
					bitmap24bpp.SetPixel(dx, dy, Color.FromArgb(pixel.A, pixel.A, pixel.A, pixel.A));
				}
			}
		}
	}
}
