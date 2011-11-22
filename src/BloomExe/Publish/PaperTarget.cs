using System.Drawing;

namespace Bloom.Publish
{
	public abstract class PaperTargetx
	{
		public readonly int Width;
		public readonly int Height;
		public string Name;

		public PaperTargetx(string name, int width, int height)
		{
			Name = name;
			Width = width;
			Height = height;
		}

		public virtual Point GetOutputDimensions(int inputWidth, int intputHeight)
		{
			return new Point(Width, Height);
		}


		public override string ToString()
		{
			return Name;
		}
	}

	class DoublePaperTargetx : PaperTargetx
	{
		public DoublePaperTargetx()
			: base(StaticName, 0,0)
		{

		}
		public override Point GetOutputDimensions(int inputWidth, int inputHeight)
		{
			if (inputHeight > inputWidth)
			{
				return new Point(inputWidth*2, inputHeight);//portrait
			}
			else
			{
				return new Point(inputWidth, inputHeight*2); //landscape
			}
		}
		public const string StaticName = @"PerservePage";//this is tied to use settings, so don't change it.
	}

	class SameSizePaperTargetx : PaperTargetx
	{
		public SameSizePaperTargetx()
			: base(StaticName, 0, 0)
		{

		}

		public const string StaticName = @"ShrinkPage";//this is tied to use settings, so don't change it.

		public override Point GetOutputDimensions(int inputWidth, int inputHeight)
		{
			return new Point(inputHeight, inputWidth);
		}
	}
}
