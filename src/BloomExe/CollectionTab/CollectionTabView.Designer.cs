using Bloom.Collection;
using Bloom.TeamCollection;
using Bloom.web;

namespace Bloom.CollectionTab
{
	partial class CollectionTabView
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				BookCollection.CollectionCreated -= OnBookCollectionCreated;

				try
				{
					var collections = _model?.GetBookCollections(true) ?? System.Linq.Enumerable.Empty<BookCollection>();
					foreach (var collection in collections)
						collection?.StopWatchingDirectory();
				}
				catch (System.Exception e)
				{
					// The exception is almost expected in ConsoleMode, but not otherwise.
					System.Diagnostics.Debug.WriteLine("Caught exception in CollectionTabView.Dispose(): {0}", e);
					if (!Program.RunningInConsoleMode)
						throw;
				}
				if (components != null)
					components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Component Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this._reactControl = new ReactControl();
            this._L10NSharpExtender = new L10NSharp.Windows.Forms.L10NSharpExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
            this.SuspendLayout();
            //
            // _L10NSharpExtender
			//
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			//
			// reactControl2
			//
			this._reactControl.BackColor = System.Drawing.Color.White;
			this._reactControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this._reactControl.JavascriptBundleName = "collectionsTabPaneBundle";
			this._L10NSharpExtender.SetLocalizableToolTip(this._reactControl, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reactControl, null);
			this._L10NSharpExtender.SetLocalizingId(this._reactControl, "ReactControl");
			this._reactControl.Location = new System.Drawing.Point(0, 0);
			this._reactControl.Name = "_reactControl";
			this._reactControl.Size = new System.Drawing.Size(773, 518);
			this._reactControl.TabIndex = 16;
			//
			// ReactCollectionTabView
			//
			this.BackColor = System.Drawing.SystemColors.Control;
			this.Controls.Add(this._reactControl);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "CollectionTab.LibraryView");
			this.Name = "CollectionTabView";
			this.Size = new System.Drawing.Size(773, 518);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion
		private L10NSharp.Windows.Forms.L10NSharpExtender _L10NSharpExtender;
		private web.ReactControl _reactControl;
	}
}
