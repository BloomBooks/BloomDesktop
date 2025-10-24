Status of the attempt to move BloomBrowserUI to Vite and its testing to Vitest

We now have two build modes.
yarn dev runs the dev mode of vite, which serves up our source with minimal bundling.
This needs some special-case handling where the HTML loads stuff.
This branch has an updated version of ReactControl.cs, which builds a different html file
to load the appropriate thing in dev mode. So far, it's only been tested, and I think only
works, on the CollectionTab control. It should take only minimal work, we think, to get
it working for other clients of ReactControl.
It will take a bit more to get it working for the other entrypoint/bundle clients, especially edit view.
I have not looked at this side of things.

The other mode is vite's build mode, which we are trying to get to build bundles similar to the old ones
for use in production. That's what I've been working on.

Currently I'm building stand-alone bundles for the three bundles that make up edit mode
plus the spreadsheet bundle. This came about because I thought (perhaps wrongly) that creating
multiple entry points was responsible for some of our problems. It's plausible that we might make
a single bundle out of all the ReactControl (and ReactDialog) entrypoints, and just give ReactControl
a way to activate the root control it wants. For example, WireUpWinForms might add an entry to a map
and a prop would tell which control to make at the root. I'm not sure whether this would slow down
bringing up controls (more code to load) or speed it up (same file, already cached, for each root).
We could easily try going back to making the four single bundles be just entry points again.

I made vite produce EsNext code. This means the output files are modules and can themselves import
things. Vite makes extensive use of this when doing multiple entry points. So any one bundle has a
lot of files the html is supposed to import. But we don't have a root html file that vite can modify
to do this. I figured out a way to generate a master file for each bundle which imports all the
necessary pieces. CommonBundle is obsolete, though I haven't removed all vestiges of it yet. HTML only
needs to import one file for each entrypoint (wich a few exceptions for legacy dependencies, mainly
jquery and friends).

Because bundles are modules, script tags for them must use type="module", not type="text/javascript".
I think I have fixed everywhere that needs this. One consequence is that it's more likely that the document
is already loaded by the time our bundle code starts. This also means any legacy js code imported with
text/javascript will do any immediate execution before any immediate execution in our main code.

Our package.json previously claimed (with side-effects: false) that none of our code had side effects.
I've made a considerable list of exceptions where this is not true already, but there may be
more. Anything with immediately-executed code (e.g., adding event listeners) is suspect, and anything
that adds a function to jQuery will definitely be a problem.

jQuery and $ are no longer automatically properly defined. Code using them that is imported needs to
import them. Code that is loaded directly from HTML needs to be imported after jquery itself.

Status:
Collection tab seems to be working pretty well. No known problems, but not extensively tested.
Edit tab is working reasonably well, not very extensively tested. The toolbox More button is off
the bottom of the screen; this might just need a rebase, since I think it was recently fixed in master.
Collection settings is working well.
There are lots of warnings.
Nothing clears the output directory before any of the builds. It's easy to have something go on
working because a previous build left something around.
Don't yet have vite building pug files (use yarn gulp pug until we do). It's likely there are other
file types for which we need to add handling.
I've made some attempts at copying assets that don't need compilation, but it's likely that
we are copying some things we don't need (I used ** on lib, for example), not copying some
that we do, and copying some to the wrong places (e.g. root of output\browser rather than a
subdirectory, or vv).

I've pulled in my incomplete work on the vitest switchover, mainly because some of the same code
changes were needed to get things working. We may decide to back that out and just keep the changes
that help with the vite build, or it may be possible to carry on and get them all working.

Major todos:
- get vite dev mode working for all react controls
- get it working for edit tab (maye for spreadsheet bundle? I don't think it's worth it.)
- can we make the post-build.js some sort of plugin so it's part of the build?
- it would feel cleaner, and maybe save space and time, if xBundle.js replaced xBundle-main.js rather than importing it.
- clean up everything that thinks commonbundle.js should exist, and get rid of it.
- pug
- anything else we do with gulp that vite doesn't do yet
- clean the output directory
- vitest
