using System;
using System.Text;

namespace Bloom.Publish.Video
{
	/// <summary>
	/// The response format for API calls asking for the dimensions of one or more formats
	/// </summary>
	/// <remarks>Example: Used by the publish/av/getUpdatedFormatDimensions API call.</remarks>
	internal class FormatDimensionsResponseEntry
	{
		// NOTE: The public fields should be kept in sync with Javascript's IFormatDimensionsResponseEntry!
		public string format;
		public string aspectRatio;
		public int desiredWidth;
		public int desiredHeight;
		public int actualWidth;
		public int actualHeight;

		public FormatDimensionsResponseEntry(string formatName, Resolution desiredResolution, Resolution actualResolution)
		{
			this.format = formatName;
			this.aspectRatio = desiredResolution.GetAspectRatio();
			this.desiredWidth = desiredResolution.Width;
			this.desiredHeight = desiredResolution.Height;
			this.actualWidth = actualResolution.Width;
			this.actualHeight = actualResolution.Height;
		}
	}
}
