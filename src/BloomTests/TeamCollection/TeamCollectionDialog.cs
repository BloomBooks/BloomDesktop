using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BloomTests.TeamCollection
{
	/// <summary>
	/// The dialog that comes up when the TeamCollection status indicator is clicked.
	/// All the interesting content and behavior is in the tsx file of the same name.
	/// The connection is through the child ReactControl, which entirely fills the dialog.
	/// </summary>
	/// <remarks>Unfortunately we haven't yet found a good way to make a Form with its
	/// title rendered in HTML draggable.</remarks>
	public partial class TeamCollectionDialog : Form
	{
		public TeamCollectionDialog()
		{
			InitializeComponent();
		}
	}
}
