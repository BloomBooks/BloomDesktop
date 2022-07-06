using System;
using System.Diagnostics;
using Bloom.Utils;

namespace Bloom.Publish.Video
{
	public struct Resolution
	{
		public int Width;
		public int Height;

		public Resolution(int width, int height)
		{
			Width = width;
			Height = height;
		}

		/// <summary>
		/// Creates a copy of this object
		/// </summary>
		/// <remarks>Not necessary to use Clone over assignment operator since this is a struct,
		/// but offered for anyone who wants a future-proof version</remarks>
		/// <returns></returns>
		public Resolution Clone()
		{
			return new Resolution(Width, Height);
		}

		public Resolution GetInverse() => new Resolution(Height, Width);

		public override string ToString()
		{
			return $"({Width}, {Height})";
		}

		/// <summary>
		/// Returns the aspect ratio in x:y format. (Whole number formats like "16:9" will be preferred over the equivalent "1.7:1")
		/// </summary>
		public string GetAspectRatio()
		{
			var proportion = ((double)Width) / Height;

			double delta = 0.05;
			if (proportion.ApproximatelyEquals(16.0 / 9, delta))
			{
				return "16:9";
			}
			else if (proportion.ApproximatelyEquals(3.0 / 2, delta))
			{
				return "3:2";
			}
			// ENHANCE: You could add 4:3 here if you want.
			else if (proportion.ApproximatelyEquals(5.0 / 4, delta))
			{
				return "5:4";
			}
			else if (proportion.ApproximatelyEquals(1, delta))
			{
				return "1:1";
			}
			else if (proportion.ApproximatelyEquals(4.0 / 5, delta))
			{
				return "4:5";
			}
			// ENHANCE: You could add 3:4 here if you want.
			else if (proportion.ApproximatelyEquals(2.0 / 3, delta))
			{
				return "2:3";
			}
			else if (proportion.ApproximatelyEquals(9.0 / 16, delta))
			{
				return "9:16";
			}
			else
			{
				// Unrecognized aspect ratio. Not sure what is the best text to put here.
				// Just make a best effort. Return something like "1:1.31"

				string aspectRatio;
				if (Width > Height)
				{
					var aspectRatioRounded = proportion.ToString("0.##");
					aspectRatio = $"{aspectRatioRounded}:1";
				}
				else
				{
					var aspectRatioRounded = (1.0 / proportion).ToString("0.##");
					aspectRatio = $"1:{aspectRatioRounded}";
				}
				Debug.Fail($"Unimplemented aspect ratio: ({Width}, {Height}) = {aspectRatio}");
				return aspectRatio;
			}
		}		
	}
}
