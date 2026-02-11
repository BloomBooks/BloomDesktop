using System;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using SIL.Reporting;

namespace Bloom;

/// <summary>
/// This class is responsible for making sure Bloom really exits when we ask it to.
/// To ensure this, code must use ProgramExit.Exit() rather than Application.Exit() directly.
/// We have a git hook to enforce this.
/// </summary>
static class ProgramExit
{
    private static Thread _forceShutdownThread;

    public static void Exit()
    {
        EnsureBloomReallyQuits();
        Application.Exit();
    }

    // Use this if we haven't properly started up, just need to do minimal cleanup and exit.
    // This should not be used if there might be something that needs saving.
    public static void ExitFromStartup()
    {
        EssentialCleanup();
        Environment.Exit(1);
    }

    private static void EnsureBloomReallyQuits()
    {
        if (_forceShutdownThread != null)
            return; // already started
        // We've had a problem with Bloom failing to shutdown, leaving a zombie thread that prevents
        // other instances from starting up. If shutdown takes more than 20 seconds, we will just
        // forcefully exit.
        _forceShutdownThread = new Thread(() =>
        {
            Thread.Sleep(20000);
            // We hope to never get here; it means that Bloom failed to fully
            // quit 20s after we called Application.Exit(). We will now do some
            // minimal essential cleanup and then force an exit.
            // It might be nice to tell the user, but since we don't know
            // what went wrong or where we are in the shutdown process, it's not
            // obvious what to say. I don't even see how we can reliably show a message,
            // since we're about to force a shut down. And I don't think there's
            // anything that will be left in a bad state that the user might need
            // to deal with.
            EssentialCleanup();

            Environment.Exit(1);
            // If that doesn't prove drastic enough, an even more forceful option is
            // Process.GetCurrentProcess().Kill();
        });
        _forceShutdownThread.Priority = ThreadPriority.Highest;
        _forceShutdownThread.IsBackground = true; // so it won't block shutdown
        _forceShutdownThread.Start();
    }

    private static void EssentialCleanup()
    {
        try
        {
            Logger.WriteEvent("Forcing Bloom to close after normal shutdown timed out.");
        }
        catch (Exception)
        {
            // We might have already shut down the logger. If we can't log it, too bad.
        }

        // These things MUST be done so that Bloom can be started again without problems.
        // They should be very fast.
        try
        {
            BloomServer._theOneInstance?.CloseListener();
        }
        catch (Exception)
        {
            // Anything that goes wrong here shouldn't prevent either trying
            // the next cleanup or exiting.
        }

        try
        {
            Program.ReleaseBloomToken();
        }
        catch (Exception) { }
    }
}
