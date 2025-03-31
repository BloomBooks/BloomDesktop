# Files shared from bloom-player package

Four files are currently being shared between bloom-player and BloomDesktop.
Their real home is in the bloom-player repository.

1. dragActivityRuntime.ts
2. event.ts
3. narration.ts
4. scrolling.ts

These typescript files are copied here by the `yarn bp-to-src` script in the
packages.json file.  This script is automatically run by both the `yarn
build` and `yarn build-prod` scripts.

One of these files (scrolling.ts) is modified slightly by commenting out the
`import $ from "jquery";` line at the beginning of the file.  This line is
needed in bloom-player, but is not needed and even breaks the build in
BloomDesktop.  This is something to keep in mind if you fix any bugs in this
file in the BloomDesktop environment and need to copy the file over to where
the bloom-player repository lives on your computer.

These files can be worked on in the BloomDesktop environment only if you use
`yarn watch` to build and **not** `yarn build`.  The latter command will
silently overwrite all of your hard work!  Once you have finished getting
whatever bug fixed or feature working to your satisfaction, then you need to
copy the modified files over to the bloom-player folder and build and test
things there.  Once you're sure that the changes work in both BloomDesktop
and bloom-player, submit a pull request from bloom-player and work on
getting it approved and merged.  One it is merged, and a new package
published to the npm site, update the version of bloom-player in the
BloomDesktop/src/BloomBrowserUI/packages.json file and rebuild BloomDesktop
to verify everything is working.

Note that the formatting setup in bloom-player is different than the setup
in BloomDesktop.  When you copy the files over to bloom-player, they need to
be reformatted by saving them from VS Code before you commit them to a
branch and submit a pull request.  Otherwise, a number of spurious, unstable
changes can creep into the code base.

If the bug/feature being worked on is common to the bloom-player environment
and the BloomDesktop environment, it's probably best to work on things from
the bloom-player side.  Before submitting a pull request, the files could be
copied over to BloomDesktop for a smoke test build. (Remember to comment out
the first line of scrolling.ts if that is one of the modified files!)

## Why shared files

bloom-player is not set up to act as a library module.  None of the methods
used from these files is exported by bloom-player.  Even if it were modified
to export those methods, importing bloom-player would add about 840K to
whatever module imported it.  Sharing the individual files was deemed the
lightest weight approach to sharing this code between BloomDesktop and
bloom-player.  (Of course, if somebody better versed in typescript and
javascript modules wants to try a different approach, that would be
welcome.)

Having the individual files available for editing also can expedite
development and debugging on the BloomDesktop side, although care must be
taken to copy the modified files back to bloom-player for deployment.
