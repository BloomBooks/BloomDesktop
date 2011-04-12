using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Palaso.IO;

namespace Bloom
{
	public partial class InfoView : UserControl
	{
		public delegate InfoView Factory();//autofac uses this

		public InfoView()
		{

			InitializeComponent();
		}

		private void InfoView_Load(object sender, EventArgs e)
		{
			var dir= FileLocator.GetDirectoryDistributedWithApplication("infoPages");
			foreach (var file in Directory.GetFiles(dir,"*.htm"))
			{
				_topicsList.Items.Add(new ListViewItem(Path.GetFileNameWithoutExtension(file)) {Tag = file});
			}
			_topicsList.Items[0].Selected = true;
		}

		private void _topicsList_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(_topicsList.SelectedItems.Count>0)
			{
				_browser.Navigate(_topicsList.SelectedItems[0].Tag as string, false);
			}
		}
	}
}
