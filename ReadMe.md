Bloom Desktop is a c# Windows application that dramatically "lowers the bar" for language communities who want books in their own languages. Bloom delivers a low-training, high-output system where mother tongue speakers and their advocates work together to foster both community authorship and access to external material in the vernacular.


# Development Process

## RoadMap / Day-to-day progress

See the [Bloom Trello Board](https://trello.com/board/bloom-development/4f087ec138f81c83752051a0).

## Bug Reports

Reports can be entered in [http://jira.palaso.org/issues/browse/BL](http://jira.palaso.org/issues/browse/BL). They can be entered there via email by sending to [issues@bloom.palaso.org](mailto:issues@bloom.palaso.org); things sent there will be visible on the web to anyone who makes an account on the jira system.

## Continuous Build System

Each time code is checked in, an automatic build begins on our [TeamCity build server](http://build.palaso.org/project.html?projectId=project16&amp;tab=projectOverview), running all the unit tests. Similarly, when there is a new version of some Bloom dependency (e.g. Palaso, PdfDroplet, our fork of GeckoFX), that server automatically rebuilds Bloom. This automatic build doesn't publish a new installer, however. That kind of build is launched manually, by pressing a button on the TeamCity page. This "publish" process builds Bloom, makes and installer, rsyncs it to the distribution server, and writes out a little bit of html which the [Bloom download page](../download/) then displays to the user.

## Source Code

Bloom is written in C# with Winforms, with an embedded Gecko (Firefox) browser and a bunch of jquery-using javascript & Typescript. You will need Visual Studio 2010 SP1, or greater, to build it. The free Visual Studio Express version should be fine, but we don't test it.
`
You'll need at least a 2010 edition of Visual Studio, including the free Express version.

Now, what revision should you be on? If you're not familiar with DVCS (Distributed version control), this could be a big stumbling block. I hesitate to give advice in this document in case I forget to update it. But a reasonable start is to update to the tip revision, which is the most recent one that anyone has checked in, regardless of which branch it is on. To update to the tip, do:
`
hg update tip`

It will avoid some complications if you do that now, before adding the dependencies which follow.

## Binary Dependencies

Some of the dependencies are very large, and others are updated frequently. For both of those reasons, you can't just pull the code and expect it to compile. First, you will have to do some extra work to get Bloom's library dependencies complete and up to date. To make that easy, each time our TeamCity server builds Bloom via [Bloom-Default-Win32-Auto](http://build.palaso.org/viewType.html?tab=buildTypeStatusDiv&amp;buildTypeId=bt222), it creates a [Bloom32Dependencies.zip](http://build.palaso.org/guestAuth/repository/download/bt222/.lastSuccessful/BloomWin32Dependencies.zip) file, which has one file for distfiles (exiftool.exe), and a bunch of files for the lib directory, including the full xulrunner. Extract this file *on top of your bloom directory*, allowing your zip utility to replace any conflicting files with the ones in the zip file.

Stop.

Did you really extract on top of your Bloom Directory? This is an unusual thing to do with zip files, and so you may not have paid attention to that instruction. But you really need to. Aim your zip extractor at the top-most Bloom folder, and allow it to replace whatever it wants. Then all the libraries will be placed exactly in the same position as they are in the build server.

### Mercurial
If you'll be working with Send/Receive, please copy the "Mercurial" and "Mercurial Extensions" folders from an installation of Bloom to the root of your Bloom source directory.

Next, find and double-click "Bloom VS2010.sln" and choose "Debug:Start Debugging". Problems? Don't get frustrated, just drop us an email: hattonjohn on gmail.

#### About Bloom Dependencies

Our **[Palaso libraries](http://projects.palaso.org/projects/palaso)** hold the classes that are common to multiple products. If you need to build palaso from source, see [projects.palaso.org/projects/palaso/wiki](http://projects.palaso.org/projects/palaso/wiki).

Our **[PdfDroplet ](http://pdfdroplet.palaso.org)**engine drives the booklet-making in the Publish tab. If you need to build PdfDroplet from source, see [projects.palaso.org/projects/pdfdroplet/wiki](http://projects.palaso.org/projects/palaso/wiki).

Our **[Chorus](http://projects.palaso.org/projects/chorus)** library provides the Send/Receive functionality.

**GeckoFX**: Much of Bloom happens in its embedded Firefox browser. This has two parts: the XulRunner engine, and the GeckoFX .net wrapper.

GeckoFX is included in the BloomWin32Dependencies.zip file. If you need to build GeckoFX from source, see [https://bitbucket.org/geckofx](https://bitbucket.org/geckofx). Note that Bloom is actually built off of the [Hatton fork](https://bitbucket.org/hatton/geckofx-11.0). In either case, you'll need to figure out which version of gecko (firefox) Bloom is currently using.

**XulRunner**: If you need some other version that what is already in the BloomWin32Dependencies.zip, they come from here: [http://ftp.mozilla.org/pub/mozilla.org/xulrunner/releases](http://ftp.mozilla.org/pub/mozilla.org/xulrunner/releases). You want a "runtime", not an "sdk". Note, in addition to the generic "lib/xulrunner", the code will also work if it finds "lib/xulrunner8" (or 9, or 10, or whatever the current version is). I prefer to append that number so that I'm clear what the version of xulrunner is that I have sitting there.

More information on XulRunner and GeckoFX: Firefox is a browser which uses XulRunner which uses Gecko rendering engine. GeckoFX is the name of the .net dll which lets you use XulRunner in your WinForms applications, with .net or mono. This is a bit confusing, because GeckoFX is the wrapper but you won't find something called "gecko" coming out of Mozilla and shipping with Bloom. Instead, "XulRunner" comes from Mozilla and ships with Bloom, which accesses it using the GeckoFX dll. Got it?

Now, Mozilla puts out a new version of XulRunner every 6 weeks at the time of this writing, and Hindle's GeckoFX keeps up with that, which is great, but also adds a level of complexity when you're trying to get Bloom going. Bloom needs to have 3 things in sync:
1) XulRunner
2) GeckoFX intended for that version of XulRunner
3) Bloom source code which is expecting that same version of GeckoFX.


# Testers

Please see "Tips for Testing Palaso Software":https://docs.google.com/document/d/1dkp0edjJ8iqkrYeXdbQJcz3UicyilLR7GxMRIUAGb1E/edit

# **License**

Bloom is open source, using the [MIT License](http://sil.mit-license.org). It is Copyright SIL International.