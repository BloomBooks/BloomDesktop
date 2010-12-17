using System.Linq;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Crop
{
	public partial class ImageCropper : UserControl
	{
		private Image _image;
		private Grip _gripperBeingDragged;
		private Rectangle _sourceImageArea;
		private const int GripThickness = 20;
		private const int GripLength = 80;
		const int BorderSize = 30;
		private Grip _bottomGrip;
		private Grip _topGrip;
		private Grip _leftGrip;
		private Grip _rightGrip;

		/// <summary>
		/// Used to mark the spot where the user first started dragging the mouse, when he clicks somewhere other than one of the grips.
		/// We use this to create the new crop rectangle as he continues the drag.
		/// </summary>
		private Point _startOfDrag= default(Point);

		private const int MarginAroundPicture = GripThickness;
		private const int MinDistanceBetweenGrips = 20;

		public ImageCropper()
		{
			 InitializeComponent();

			Application.Idle += new EventHandler(Application_Idle);
		}

		void Application_Idle(object sender, EventArgs e)
		{
			if (_image == null)
				return;

			if (_startOfDrag != default(Point))
			{
				DoCropDrag();
			}
			else if(_gripperBeingDragged != null)
			{
				DoGripDrag();
			}
			Invalidate();
		}

		private void DoGripDrag()
		{
			Point mouse = PointToClient(MousePosition);
			if (_gripperBeingDragged.MovesVertically)
			{
				_gripperBeingDragged.Value= mouse.Y-MarginAroundPicture;

				//adjust the vertical position of other axis' grips
				foreach (var grip in HorizontalControlGrips)
				{
					grip.UpdateRectangle();
				}
			}
			else
			{
				_gripperBeingDragged.Value = mouse.X-MarginAroundPicture;
				foreach (var grip in VerticalControlGrips)
				{
					grip.UpdateRectangle();
				}
			}
		}

		private void DoCropDrag()
		{
			Grip hStart, vStart, hEnd, vEnd;

			Point mouse = PointToClient(MousePosition);
			if (_startOfDrag.X < mouse.X)
			{
				hStart = _leftGrip;
				hEnd = _rightGrip;
			}
			else
			{
				hEnd = _leftGrip;
				hStart = _rightGrip;
			}

			if(_startOfDrag.Y < mouse.Y)
			{
				vStart = _topGrip;
				vEnd = _bottomGrip;
			}
			else
			{
				vEnd = _topGrip;
				vStart = _bottomGrip;
			}

			hStart.Value = _startOfDrag.X - MarginAroundPicture;
			vStart.Value = _startOfDrag.Y-MarginAroundPicture;
			hEnd.Value = mouse.X - MarginAroundPicture;
			vEnd.Value = mouse.Y - MarginAroundPicture;
		}

		protected int MiddleOfVerticalGrips()
		{
			return _leftGrip.Right + ((_rightGrip.Left - _leftGrip.Right)/2);
		}
		protected int MiddleOfHorizontalGrips()
		{
			 return _topGrip.Bottom + ((_bottomGrip.Top  - _topGrip.Bottom) / 2);
		}
		public Image Image
		{
			get {
				return _image;
			}
			set {
				_image = value;

				CalculateSourceImageArea();
				CreateGrips();

				foreach (var grip in Grips)
				{
					grip.UpdateRectangle();
				}

				Invalidate();
			}
		}

		private void CreateGrips()
		{
			_bottomGrip = new Grip(_sourceImageArea.Height, GripLength, GripThickness, Grip.Sides.Bottom,
								   MiddleOfVerticalGrips,
								   () => _topGrip.Value,
								   () => _sourceImageArea.Height);

			_topGrip = new Grip(0, GripLength, GripThickness, Grip.Sides.Top,
								MiddleOfVerticalGrips,
								()=>0,
								() => _bottomGrip.Value);
			_leftGrip = new Grip(0, GripThickness, GripLength, Grip.Sides.Left,
								 MiddleOfHorizontalGrips,
								 () => 0,
								 () => _rightGrip.Value);

			_rightGrip = new Grip(_sourceImageArea.Width, GripThickness, GripLength, Grip.Sides.Right,
								  MiddleOfHorizontalGrips,
								  () => _leftGrip.Value,
								  () => _sourceImageArea.Width);
		}


		private void ImageCropper_Resize(object sender, EventArgs e)
		{
			if (_image == null)
				return;

			var old = _sourceImageArea;


			CalculateSourceImageArea();

			if (old.Width == 0 || old.Height == 0)
				return;

			float  horizontalGrowthFactor = ((float) _sourceImageArea.Width)/((float) old.Width);
			float verticalGrowthFactor = ((float)_sourceImageArea.Height) / ((float)old.Height);

			foreach (var grip in VerticalControlGrips)
			{
				 grip.Value =  (int) ((float)grip.Value * verticalGrowthFactor);
			}
			foreach (var grip in HorizontalControlGrips)
			{
				 grip.Value = (int)((float)grip.Value * horizontalGrowthFactor);
			}

			foreach (var grip in Grips)
			{
				grip.UpdateRectangle();
			}

			Invalidate();
		}

		private void CalculateSourceImageArea()
		{
			var availArea = new Rectangle(BorderSize, BorderSize, Width - (2*BorderSize), Height - (2*BorderSize));
			float hProportion = availArea.Width/((float)_image.Width);
			float vProportion = availArea.Height/((float)_image.Height);
			float imageToCanvaseScaleFactor = Math.Min(hProportion, vProportion);
			_sourceImageArea = new Rectangle(GripThickness,GripThickness, (int)(_image.Width * imageToCanvaseScaleFactor), (int)(_image.Height * imageToCanvaseScaleFactor ));

		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (_image == null || _sourceImageArea.Width==0)
				return;

			e.Graphics.FillRectangle(Brushes.Gray, ClientRectangle);
		   e.Graphics.DrawImage(
				_image,
				_sourceImageArea,
				new Rectangle( // Source
					0, 0,
					_image.Width, _image.Height),
				GraphicsUnit.Pixel);

			Brush brush = new Pen(Color.FromArgb(150, Color.LightBlue)).Brush;
			e.Graphics.FillRectangle(brush, _leftGrip.InnerEdge, _bottomGrip.InnerEdge, _rightGrip.InnerEdge - _leftGrip.InnerEdge /*this avoids overlapp which makes it twice as light*/
								, Height - _bottomGrip.InnerEdge);
			e.Graphics.FillRectangle(brush, _leftGrip.InnerEdge, 0, _rightGrip.InnerEdge - _leftGrip.InnerEdge, _topGrip.InnerEdge);
			e.Graphics.FillRectangle(brush, 0, 0, _leftGrip.InnerEdge, Height);
			e.Graphics.FillRectangle(brush, _rightGrip.InnerEdge, 0, Width - _rightGrip.InnerEdge, Height);

			e.Graphics.DrawRectangle(Pens.LightBlue, _leftGrip.Right, _topGrip.Bottom, _rightGrip.Left - _leftGrip.Right, _bottomGrip.Top - _topGrip.Bottom);



			//            e.Graphics.FillRectangle(brush, _leftGrip.Rectangle.Right, _bottomGrip.Top, _rightGrip.Rectangle.Left - _leftGrip.Rectangle.Right /*this avoids overlapp which makes it twice as light*/
//                                , Height - _bottomGrip.Top);
//            e.Graphics.FillRectangle(brush, _leftGrip.Rectangle.Right, 0, _rightGrip.Rectangle.Left - _leftGrip.Rectangle.Right, _topGrip.Bottom);
//            e.Graphics.FillRectangle(brush, 0, 0, _leftGrip.Rectangle.Right, Height);
//            e.Graphics.FillRectangle(brush, _rightGrip.Rectangle.Left, 0, Width- _rightGrip.Rectangle.Left , Height);
//
//            e.Graphics.DrawRectangle(Pens.LightBlue, _leftGrip.Right, _topGrip.Bottom, _rightGrip.Left  - _leftGrip.Right, _bottomGrip.Top - _topGrip.Bottom);
//
			_bottomGrip.Paint(e.Graphics);
			_topGrip.Paint(e.Graphics);
			_leftGrip.Paint(e.Graphics);
			_rightGrip.Paint(e.Graphics);

		}

		private Grip[] Grips
		{
			get { return new Grip[] { _bottomGrip, _topGrip, _leftGrip, _rightGrip }; }
		}
		private Grip[] VerticalControlGrips
		{
			get { return new Grip[] { _bottomGrip, _topGrip}; }
		}
		private Grip[] HorizontalControlGrips
		{
			get { return new Grip[] { _leftGrip, _rightGrip }; }
		}

	   private void ImageCropper_MouseDown(object sender, MouseEventArgs e)
		{

			foreach (var grip in Grips)
			{
				if (grip.Contains(e.Location))
				{
					_gripperBeingDragged = grip;
					return;
				}
			}
		   _startOfDrag = e.Location;
		}

		private void ImageCropper_MouseUp(object sender, MouseEventArgs e)
		{
			_gripperBeingDragged = null;

			_startOfDrag = default(Point);
		}
	}
}
