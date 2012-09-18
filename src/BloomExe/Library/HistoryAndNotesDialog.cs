using System.Windows.Forms;
using Chorus;

namespace Bloom.Library
{
	public partial class HistoryAndNotesDialog : Form
	{
		public delegate HistoryAndNotesDialog Factory();//autofac uses this

		public HistoryAndNotesDialog(ChorusSystem chorusSystem)
		{
			InitializeComponent();

#if notes
			var notes = chorusSystem.WinForms.CreateNotesBrowser();
			notes.Dock = DockStyle.Fill;
			_notesPage.Controls.Add(notes);
#else
		tabControl1.Controls.Remove(_notesPage);
#endif
			var history = chorusSystem.WinForms.CreateHistoryPage();
			history.Dock = DockStyle.Fill;
			_historyPage.Controls.Add(history);
		}

		public bool ShowNotesFirst { get; set; }

		private void HistoryAndNotesDialog_Load(object sender, System.EventArgs e)
		{
			if (ShowNotesFirst)
				tabControl1.SelectTab(_notesPage);
		}
	}
}
