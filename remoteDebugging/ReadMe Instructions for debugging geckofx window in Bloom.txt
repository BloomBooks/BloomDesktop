"Remote" debugging refers to using the FireFox browser to debug what is going on in the embedded geckofx window in Bloom.
Requirements:
- Gecko/xulrunner 29.0.1 or later (hence: doesn't work in 2.0 Branch of Bloom)
- currently only supported in debug builds.
(To enable for a release build, remove the #ifdef from around StartDebugServer(). Please don't do this for a released version, there could be security issues. You will also need to copy the folder remoteDebugging to output/release.)
- reqires a compatible version of FireFox to do the debugging. There may be some flexibility here, but trying to debug when Bloom uses geckofx 29 using FF 31 failed. In that case, the failure mode was an unending wait for the browser to connect.
I found an installer for FF29 here: https://ftp.mozilla.org/pub/mozilla.org/firefox/releases/29.0.1/win32/en-US/
Note that you need to tell FF NOT to update automatically, otherwise, FF29 will immediately upgrade itself to the latest version, and debugging will fail the next time you start it up. See Tools/Options/Advanced and choose "Never check for updates"
It is possible to install both FF 29 and the current one (in different folders) but they seem to share settings, and cannot run at the same time. This means that you will need to turn off automatic updates using the current version before you run 29, then turn it on again afterwards.

OK, assuming you have a debug build of Bloom running, here's what you do:
- If you have a later version of FF that you normally use with automatic updates enabled, turn that off as described above
- Launch FF 29 (or whatever corresponds to the current version of xulrunner)

The first time:
- In FF, navigate to about:config
- Proceed past the warning
- Find the setting devtools.debugger.remote-enabled and set it to true

Each time you want to debug bloom:
- Choose Tools/Web Developer/Connect. (You should see a black screen saying "Connect to remote device", Host localhost, Port 6000. Adjust those settings if need be.)
- Click Connect.
You should see a dialog pop up in the Bloom process with the caption "Incoming connection" and a warning that something is trying to connect. This often seems to be hidden behind other windows. Look for a second window connected with the Bloom icon in your task bar, and bring the dialog to the front if needed. You don't have a lot of time before the connection attempt times out. If that happens, just try again.
- Click OK in the Incoming connection dialog.
It's at this point that I see an indefinite delay if I use the wrong version of FF. You should very quickly see the content of the FF window change to something like this:
   Available remote tabs:
   Available remote processes:
	 Main Process
- Click on Main Process

You should now see the remote debugging console for working with the main page of Bloom.

When you are done, you may wish to start up your main version of FF and re-enable automatic updates.
