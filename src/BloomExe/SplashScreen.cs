using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Bloom
{
	public partial class SplashScreen : Form
	{
		public SplashScreen()
		{
			InitializeComponent();
		}

		public void FadeAndClose()
		{
			_fadeOutTimer.Enabled = true;
		}

		private void _fadeOutTimer_Tick(object sender, EventArgs e)
		{
			Opacity -= 0.10;
			if (Opacity <= 0.01)
			{
				Close();
			}
		}
	}

	public class Splasher
	{
		static SplashScreen _splashForm ;
		static Thread _splashThread;

		static public void Show()
		{
			if (_splashThread != null)
				return;

			_splashThread = new Thread(new ThreadStart(Splasher.ShowThread));
			_splashThread.IsBackground = true;
			_splashThread.ApartmentState = ApartmentState.STA;
			_splashThread.Start();
		}



		static void ShowThread()
		{
			_splashForm = new SplashScreen();
			Application.Run(_splashForm);
//            _splashForm.ShowDialog();
		}

		static public void Close()
		{
			if (_splashThread == null) return;
			if (_splashForm == null) return;

			try
			{
				_splashForm.Invoke(new MethodInvoker(_splashForm.FadeAndClose));
			}
			catch (Exception)
			{
			}
			_splashThread = null;
			_splashForm = null;
		}

//
//        static public string Status
//        {
//            set
//            {
//                if (_splashForm == null)
//                {
//                    return;
//                }
//
//                _splashForm.StatusInfo = value;
//            }
//            get
//            {
//                if (_splashForm == null)
//                {
//                    throw new InvalidOperationException("Splash Form not on screen");
//                }
//                return _splashForm.StatusInfo;
//            }
//        }

	}
}