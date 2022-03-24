using System;

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
	}

}
