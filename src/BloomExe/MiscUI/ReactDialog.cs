using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using SIL.Reporting;

namespace Bloom.MiscUI
{
    /// <summary>
    /// A dialog whose entire content is a react control. The constructor specifies
    /// the js bundle.
    /// All the interesting content and behavior is in the tsx file of the component.
    /// The connection is through the child ReactControl, which entirely fills the dialog.
    /// </summary>
    /// <remarks>To make a Form with its title rendered in HTML draggable, the caller
    /// can (after calling the ReactDialog constructor) just modify the instance's
    /// FormBorderStyle and ControlBox properties.
    /// This class is not thread safe, we need to call any public methods from the UI thread.
    /// </remarks>
    public partial class ReactDialog : Form, IBrowserDialog
    {
        public string CloseSource { get; set; } = null;

        private static readonly List<ReactDialog> _activeDialogs = new List<ReactDialog>();

        public ReactDialog(
            string javascriptBundleName,
            object props = null,
            string taskBarTitle = "Bloom"
        )
        {
            InitializeComponent();
            FormClosing += ReactDialog_FormClosing;
            reactControl.JavascriptBundleName = javascriptBundleName;

            reactControl.Props = props;
            _activeDialogs.Add(this);
            Text = taskBarTitle;
            ShowInTaskbar = false;

            Icon = global::Bloom.Properties.Resources.BloomIcon;
        }

        public static void CloseCurrentModal(string labelOfUiElementUsedToCloseTheDialog = null)
        {
            Debug.Assert(
                Program.RunningOnUiThread || Program.RunningUnitTests,
                "ReactDialog must be called on UI thread."
            );
            if (_activeDialogs.Count == 0)
                return;

            // Closes the current dialog.
            try
            {
                var currentDialog = _activeDialogs[_activeDialogs.Count - 1];
                // On the off chance that something triggers CloseAllReactDialogs while we're closing this one,
                // we don't need to close this one again.
                _activeDialogs.Remove(currentDialog);
                // Optionally, the caller may provide a string value in the payload.  This string can be used to determine which button/etc that initiated the close action.
                currentDialog.CloseSource = labelOfUiElementUsedToCloseTheDialog;
                currentDialog.Close();
            }
            catch (Exception ex)
            {
                Logger.WriteError(ex);
            }
        }

        public static void CloseAllReactDialogs()
        {
            Debug.Assert(
                Program.RunningOnUiThread || Program.RunningUnitTests,
                "ReactDialog must be called on UI thread."
            );
            while (_activeDialogs.Count > 0)
            {
                CloseCurrentModal();
            }
        }

        /// <summary>
        /// Use this to create any ReactDialog from within an API handler which has requiresSync == true (the default).
        /// Otherwise, our server is still locked, and all kinds of things the dialog wants to do through the server won't work.
        /// Instead, we arrange for it to be launched when the system is idle (and the server is no longer locked).
        /// </summary>
        /// <param name="reactComponent">passed to ReactDialog constructor</param>
        /// <param name="props">passed to ReactDialog constructor</param>
        /// <param name="width">used to set the WinForms dialog Width property</param>
        /// <param name="height">used to set the WinForms dialog Height property</param>
        /// <param name="initialize">an optional action done after width and height are set but before ShowDialog is called</param>
        /// <param name="handleResult">an optional action done after the dialog is closed; takes a DialogResult</param>
        /// <param name="taskBarTitle">Label to show in the task bar for this form</param>
        public static void ShowOnIdle(
            string reactComponentName,
            object props,
            int width,
            int height,
            Action initialize = null,
            Action<DialogResult> handleResult = null,
            string taskBarTitle = "Bloom"
        )
        {
            DoOnceOnIdle(() =>
            {
                using (var dlg = new ReactDialog(reactComponentName, props, taskBarTitle))
                {
                    dlg.Width = width;
                    dlg.Height = height;

                    initialize?.Invoke();

                    var result = dlg.ShowDialog();

                    handleResult?.Invoke(result);
                }
            });
        }

        private static void DoOnceOnIdle(Action actionToDoOnIdle)
        {
            void HandleAction(object sender, EventArgs eventArgs)
            {
                Application.Idle -= HandleAction;
                actionToDoOnIdle();
            }

            Application.Idle += HandleAction;
        }

        private void ReactDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            _activeDialogs.Remove(this);
        }

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            // Needed for closing the dialog before focus has been given to the browser (i.e. when first launched).
            // Once the dialog has focus, the .ts side handles Escape by calling CloseCurrentModal via the API.
            if (e.KeyCode == Keys.Escape)
                CloseCurrentModal();
        }
    }
}
