using System;

namespace Bloom.TeamCollection
{
    /// <summary>
    /// Thrown while opening a Team Collection to abort the open entirely -- as opposed to the
    /// usual "fall back to Disconnected mode" behavior -- because the current signed-in account
    /// is not allowed to open this collection at all (batch item 9, account-switch behavior: the
    /// current cloud logon is not a server member of this Team Collection). Message is the full,
    /// already-composed, user-facing text (see CloudTeamCollection.CheckConnection and
    /// CloudTeamCollection.ComposeNotAMemberRefusalDetail); callers should show it directly rather
    /// than treating it as an unexpected-crash report. Caught in
    /// Program.HandleErrorOpeningProjectWindow, which shows the message and returns to the
    /// collection chooser exactly as it already does for any other failure to open a project.
    /// </summary>
    public class TeamCollectionAccessRefusedException : Exception
    {
        /// <inheritdoc />
        public TeamCollectionAccessRefusedException(string message)
            : base(message) { }
    }
}
