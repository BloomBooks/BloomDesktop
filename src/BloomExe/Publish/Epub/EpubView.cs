using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bloom.Publish
{
	/// <summary>
	/// This class implements the panel that appears in the Publish tab when the ePUB button is selected.
	/// See PublishView.SetupEpubControl for initialization. In addition to the controls created in
	/// InitializeComponent, this control normally has a browser displaying the book preview which occupies
	/// the remaining space. Since this is not a standard control it is easier to create it and insert
	/// it when needed, especially since the PublishView needs to manipulate it.
	/// </summary>
	public partial class EpubView : UserControl
	{
		public EpubView()
		{
			InitializeComponent();
		}

		private void _helpButton_Click(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, "Tasks/Publish_tasks/Create_Epub.htm");
		}
	}
}
