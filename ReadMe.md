# Let's Grow Some Libraries

Bloom is an [award winning](http://allchildrenreading.org/sil-international-wins-enabling-writers-prize-for-software-solution-to-childrens-book-shortage/) software solution to the children's book shortage among most of the world's languages. It is an application for Windows and [Linux](https://bloomlibrary.org/page/create/linux) that dramatically "lowers the bar" for creating, translating, and sharing books. With Bloom, communities can do the work for themselves instead of depending on outsiders.

Internally, Bloom is a hybrid. It started as a C#/WinForms app with an embedded browser for editing documents and an embedded Adobe Acrobat for displaying PDF outputs. It is growing up to be a pure React-driven offline-capable web app, with a C# backend. In its current state, Bloom is hybrid of C#/web app in which the bits of the UI are gradually moving to html.

# Development Process

## Kanban / Bug Reports

We use [YouTrack](https://issues.bloomlibrary.org) Kanban boards. Errors (via email or api) also flow into YouTrack, and we do some support from there by @mentioning users.

## Continuous Build System

Each time code is checked in, an automatic build begins on our [TeamCity build server](https://build.palaso.org/project/Bloom), running all the unit tests. Similarly, when there is a new version of some Bloom dependency (e.g. `bloom-player`), that server automatically rebuilds and publishes Bloom Alpha. Public channels are released by pressing a button on the TeamCity page. This "publish" process builds Bloom, runs tests, makes an installer, uploads it to S3, and writes out a little bit of json which the [Bloom download page](http://bloomlibrary.org/downloads) uses to display version-specific information to the user.

## Building Browser Source Code

You'll need nodejs and yarn, but we manage both by [installing volta](https://docs.volta.sh/guide/getting-started) and informing volta which versions to use with a setting in package.json:

    "volta": {
        "node": "16.14.0",
        "yarn": "1.22.19"
    }

This will build and test the Typescript, javascript, less, and pug:

    cd src/content
    yarn
    cd ../BloomBrowserUI
    yarn
    yarn build
    yarn test

To watch code, use `yarn watchCode`

## Building C# Source Code

To build the .NET/C# part of Bloom you'll need Visual Studio Community Edition.

### Setting up formatting

In a terminal, run `dotnet tool restore`. This will install any tools we have put in .config/dotnet-tools.json along with the correct versions.
In Visual Studio, under Extensions, install "CSharpier".  The extension's version will not be the same as the CSharpier installed by 'dotnet tool restore', but that's not a problem.
In Tools/Options, under CSharpier:General, set `Reformat with CSharpier on Save` to `true`. Note that you should set it for this solution, not globally.
You should also install the CSharpier extension in vscode.
CSharpier should be using the version specified in `.config/dotnet-tools.json`.
When testing a new version of csharpier, to format everything, run `dotnet csharpier src/BloomExe`
To hide reformatting-only commits from git blame, add the sha of the commit to `.git-blame-ignore-revs`

For Typescript formatting, we use the Prettier extension in VCode.


## Get Binary Dependencies

Before you'll be able to build, you'll have to download some binary dependencies (see below).


### TeamCity artifacts

In the `build` directory, open a git bash shell and run

`./getDependencies-windows.sh`

That could take several minutes the first time, and afterward will be quick as it only downloads what has changed. When you change branches, run this again.

Our **[PdfDroplet](https://software.sil.org/pdfdroplet/)** engine drives the booklet-making in the Publish tab. If you need to build PdfDroplet from source, see [the project on Github](https://github.com/sillsdev/pdfdroplet).

### NuGet packages

Various NuGet packages should get pulled in by the build automatically.

Our **[Palaso libraries](https://github.com/sillsdev/libpalaso)** hold the classes that are common to multiple products.

### Adding npm dependencies

npm dependencies should be introduced using

    yarn add <modulename>

## Linux Development

5.4 is the last version for Linux until we get rid of WinForms.
See the `Version5.4` branch and ReadMe if you need to update it.

See [this page](https://bloomlibrary.org/page/create/linux) for how to run it.

## Disable analytics

We don't want developer and tester runs (and crashes) polluting our statistics. On Windows, add the environment variable "feedback" with value "off".

# Registry settings

One responsibility of Bloom desktop is to handle url's starting with "bloom://"", such as those used on bloomlibrary.org when the user clicks "Translate into _your_ language!" Making this work requires some registry settings. These are automatically created when you run Bloom. If you have multiple versions installed, just make sure that the one you ran most recently is the one you want to do the download.

# License

Bloom is open source, using the [MIT License](http://sil.mit-license.org). It is Copyright SIL International.
"Bloom" is a registered trademark of SIL International.
