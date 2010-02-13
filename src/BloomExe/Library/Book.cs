using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.Library
{
	public class Book
	{
		private readonly string _path;

		public Book(string path)
		{
			_path = path;
		}

		public string Title
		{
			get { return Path.GetFileNameWithoutExtension(_path); }
		}

		public  Image GetThumbNail()
		{
			var existing = Path.Combine(_path, "thumbnail.png");
			if(!File.Exists(existing))
				return null;

			try
			{
				return Image.FromFile(existing);
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
