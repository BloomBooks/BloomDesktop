Status of the attempt to move BloomBrowserUI to Vite and its testing to Vitest

We now have two build modes.
yarn dev runs the dev mode of vite, which serves up our source with minimal bundling.
This needs some special-case handling where the HTML loads stuff.
This branch has an updated version of ReactControl.cs, which builds a different html file
to load the appropriate thing in dev mode. As far as I've tested, this now works on all
ReactControls, including ReadDialogs and the whole PublishTab. The browser automatically
hot-reloads any changes served by vite. If it somehow happens that code explicitly asks
the bloom server for something (explicit bloom: links, other than api calls), that will not
produce hot reloading. Unless another process is watching, new versions won't even get copied
to output\debug; vite dev serves directly from the source.
It will take a bit more to get vite dev working for the other entrypoint/bundle clients, especially edit view.
I took a quick look at edit mode but could not get it working with vite dev. For editablePageBundle,
I think the problem is that we load tabpane, jquery, and ckeditor separately from the main code bundle;
we don't have adequate doc of why we do that rather than importing them, but I get mysterious errors if
I change to importing, even taking care to make sure jquery is available as a global.

The other mode is vite's build mode, which builds bundles similar to the old ones
for use in production. I think all of that is working now. (We're not building a test bundle, since we
have switched to vitest.)

I made vite produce EsNext code. This means the output files are modules and can themselves import
things. Vite makes extensive use of this when doing multiple entry points. So any one bundle has a
lot of files the html is supposed to import. But we don't have a root html file that vite can modify
to do this. I figured out a way to generate a master file for each bundle which imports all the
necessary pieces. CommonBundle is obsolete and has been removed. HTML only
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
Edit tab is working reasonably well, not very extensively tested.
Publish tab is basically working.
Collection settings is working well.
There are lots of warnings.
There is a new build:clean which clears the output directory.
There is a new build-prod which clears the output directory, builds both BloomBrowserUi and content,
and builds the xliff stuff.
There is a new build:watch which should cause some automatic reprocessing and maybe hot loads when
working on a part of the UI that is still using bundles, or when not using vite dev. I haven't tried
to see how well it works.
Only clean and build-prod clear the output directory. As before, it's possible to have something go on
working in other modes because a previous build left something around.
Vite is building pug files (both its own and in content), but only when yarn build is run.
They won't cause hot-reloading in yarn dev, and probably not even in watch. This is unfortunate
and may deserve a card, but we're trying to get this merged so I can move on.
I think we're now copying all the same static assets (that don't need compilation) as before when we
run build. As noted before, I don't think yarn dev will copy them to output/browser.
Vitest is working, running and passing all of our tests except five that are disabled because
we're testing in jsdom, and it can't give real measurements or a canvas that can really be drawn on.
It should be possible to get these tests working by running them in a real browser, but I was not
able to in the time we wanted to spend on this task currently. This involved quite a bit of mocking
for things that jsdom didn't do quite right but which were only marginally related to the purpose of
the test.
Claude attemped to migrate Storybook to vite (which required an upgrade to storybook 10)
but it is not working at all (vite is not processing the tsx files) and Claude can't figure out why.
Running and debugging tests directly from VS Code (install the vitest extension) is working fairly
well, though it takes a few seconds to start up. (You can right-click and run a single test, with
breakpoints set in VS Code, and without a fight every time to get debug config right, I hope.)

Major todos:
- get vite dev mode working for edit tab. Current plan is to make a separate card for this.
- get all remaining entry points working in dev mode
- Storybook
- pug files should hot-reload when changed
- fix the five skipped tests (support tests that need a real browser)
