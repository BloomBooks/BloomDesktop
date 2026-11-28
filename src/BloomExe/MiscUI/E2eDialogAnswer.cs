namespace Bloom.MiscUI
{
    /// <summary>
    /// The path that the next native file dialog should answer with in an e2e test, instead of
    /// showing the dialog, which no automation can answer reliably (see AUTOMATION-DEBT.md,
    /// "Native OS dialogs hang automation"). A test sets it through e2e/nextFileToChoose, an
    /// endpoint that exists only under --e2e; nothing else writes it. It is taken (and cleared) by
    /// the one dialog it answers, so a test arms it once per dialog it expects. BloomOpenFileDialog,
    /// BloomFolderChooser and the Save As in MiscUtils.GetOutputFilePathOutsideCollectionFolder all
    /// take it, so a test does not have to know which of them a feature opens.
    /// </summary>
    public static class E2eDialogAnswer
    {
        private static string _nextPathToChoose;

        /// <summary>
        /// Arm the next file, folder or Save As dialog to answer with this path, or clear the
        /// armed path with null.
        /// </summary>
        public static void SetNextPathToChoose(string path)
        {
            _nextPathToChoose = path;
        }

        /// <summary>
        /// Take the armed path, clearing it, if there is one and this is an e2e run; otherwise
        /// return false. Honoured only under --e2e, so a normal run always shows its dialog even
        /// if something managed to set the path.
        /// </summary>
        public static bool TryTakeNextPathToChoose(out string path)
        {
            path = _nextPathToChoose;
            if (!Program.RunningE2eTests || string.IsNullOrEmpty(path))
            {
                path = null;
                return false;
            }
            _nextPathToChoose = null;
            return true;
        }
    }
}
