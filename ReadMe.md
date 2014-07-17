Bloom Desktop is a c# Windows application that dramatically "lowers the bar" for language communities who want books in their own languages. Bloom delivers a low-training, high-output system where mother tongue speakers and their advocates work together to foster both community authorship and access to external material in the vernacular.

# Development Process

## RoadMap / Day-to-day progress

See the Trello boards:
[Bloom 2](https://trello.com/b/UA7QLibU/bloom-desktop-2-0), [Bloom 3](https://trello.com/b/ErDHtpNe/bloom-3-0)

## Bug Reports

Reports can be entered in [jira](https://jira.sil.org/browse/BL). They can be entered there via email by sending to [issues@bloom.palaso.org](mailto:issues@bloom.palaso.org); things sent there will be visible on the web to anyone who makes an account on the jira system.

## Continuous Build System

Each time code is checked in, an automatic build begins on our [TeamCity build server](http://build.palaso.org/project.html?projectId=project16&amp;tab=projectOverview), running all the unit tests. Similarly, when there is a new version of some Bloom dependency (e.g. Palaso, PdfDroplet, our fork of GeckoFX), that server automatically rebuilds Bloom. This automatic build doesn't publish a new installer, however. That kind of build is launched manually, by pressing a button on the TeamCity page. This "publish" process builds Bloom, makes and installer, rsyncs it to the distribution server, and writes out a little bit of html which the [Bloom download page](../download/) then displays to the user.

## Source Code

Bloom is written in C# with Winforms, with an embedded Gecko (Firefox) browser and a bunch of jquery-using javascript & Typescript. You will need Visual Studio 2010 SP1, or greater, to build it. The free Visual Studio Express version should be fine, but we don't test it.

You'll need at least a 2010 edition of Visual Studio, including the free Express version.

Now, what revision should you be on? If you're not familiar with DVCS (Distributed version control), this could be a big stumbling block. I hesitate to give advice in this document in case I forget to update it. But a reasonable start is to update to the tip of the "default" branch, which is the most recent one that anyone has checked in, regardless of which branch it is on. To update to the tip, do:

`hg update default`

It will avoid some complications if you do that now, before adding the dependencies which follow.

## Get Binary Dependencies

In the build directory, run

`getDependencies-windows.sh`
or
`getDependencies-linux.sh`

That will take several minutes the first time, and afterwards will be quick as it only downloads what has changed. When you change branches, run this again.

#### About Bloom Dependencies

Our **[Palaso libraries](http://projects.palaso.org/projects/palaso)** hold the classes that are common to multiple products. If you need to build palaso from source, see [projects.palaso.org/projects/palaso/wiki](http://projects.palaso.org/projects/palaso/wiki).

Our **[PdfDroplet ](http://pdfdroplet.palaso.org)**engine drives the booklet-making in the Publish tab. If you need to build PdfDroplet from source, see [projects.palaso.org/projects/pdfdroplet/wiki](http://projects.palaso.org/projects/palaso/wiki).

Our **[Chorus](http://projects.palaso.org/projects/chorus)** library provides the Send/Receive functionality.

**GeckoFX**: Much of Bloom happens in its embedded Firefox browser. This has two parts: the XulRunner engine, and the [GeckoFX .net wrapper](https://bitbucket.org/geckofx).

**XulRunner**: If you need some other version, they come from here: [http://ftp.mozilla.org/pub/mozilla.org/xulrunner/releases](http://ftp.mozilla.org/pub/mozilla.org/xulrunner/releases). You want a "runtime", not an "sdk". Note, in addition to the generic "lib/xulrunner", the code will also work if it finds "lib/xulrunner8" (or 9, or 10, or whatever the current version is).

More information on XulRunner and GeckoFX: Firefox is a browser which uses XulRunner which uses Gecko rendering engine. GeckoFX is the name of the .net dll which lets you use XulRunner in your WinForms applications, with .net or mono. This is a bit confusing, because GeckoFX is the wrapper but you won't find something called "gecko" coming out of Mozilla and shipping with Bloom. Instead, "XulRunner" comes from Mozilla and ships with Bloom, which accesses it using the GeckoFX dll. Got it?

Now, Mozilla puts out a new version of XulRunner every 6 weeks at the time of this writing, and Hindle's GeckoFX keeps up with that, which is great, but also adds a level of complexity when you're trying to get Bloom going. Bloom needs to have 3 things in sync:
1) XulRunner
2) GeckoFX intended for that version of XulRunner
3) Bloom source code which is expecting that same version of GeckoFX.

Bloom uses various web services that require identification. We can't really keep those a secret, but we can at least not make them google'able by not checking them into github. To get the file that contains user and test-level authorization codes, just get the connections.dll file out of a shipping version of a Bloom, and place it in your Bloom/DistFiles directory.

# Special instructions for building on Linux

These notes were written by JohnT on 16 July 2014 based on previous two half-days working with Eberhard to get Bloom to build on a Precise Linux box. The computer was previously used to develop FLEx, so may have already had something that is needed. Sorry, I have not had the chance to try them on another system. If you do, please correct as needed.

Note that as of 16 July 2014, Bloom does not work very well on Linux. Something more may be needed by the time we get it fully working. Hopefully these notes will be updated.

At various points you will be asked for the SU password.

1. You need synaptic to look in some extra places for components. In Synaptic, go to Settings->Repositories, Other Software tab. You want to see the following lines:

http://packages.sil.org/ubuntu precise main
http://packages.sil.org/ubuntu precise-experimental main
http://linux.lsdev.sil.org/ubuntu precise main
http://linux.lsdev.sil.org/ubuntu precise-experimental main

If some are missing, click add and paste the missing line, then insert 'deb' at the start, then confirm.

(May help to check for and remove any lines that refer to the obsolete ppa.palaso.org, if you've been doing earlier work on SIL stuff.)

2. Update your system:

sudo apt-get update

sudo apt-get upgrade

3. Custom version of Mono:

Bloom depends on a patched version of Mono 3.4. Currently there is no package, you need to build mono yourself with the patches Bloom needs. I did this work in fwrepo/mono; I think this is only important in that some of the instructions assume the root of the mono rep is fwrepo/mono/mono.

3a. Get the mono source and the correct branch:

git clone git://github.com/sillsdev/mono.git

git checkout --track origin/feature/mono-3.4

This should put you on the necessary 3.4 branch of the mono repo that you want to build.

3b. Follow the instructions at http://linux.lsdev.sil.org/wiki/index.php/Building_mono_from_source to build mono. Note that you need to clone mono-calgary into the same parent directory as mono.

(You may get a message like "/bin/ls: cannot access /opt/mono-sil/bin/mono-fw: No such file or directory" at the end of the build. As long as it says "Finished successfully" a few lines up this is OK.

4. Install MonoDevelop 5 (or later). One way to do this is with synaptic.

Make a shortcut to launch MonoDevelop (or just use this command line). The shortcut should execute something like this:

bash -c 'PATH=/opt/monodevelop/bin:$PATH; export FIELDWORKS_ENVIRON="/home/thomson/fwrepo/fw/environ"; export MONO_ENVIRON="/home/thomson/palaso/bloom-desktop/environ"; monodevelop-launcher.sh'

(The FIELDWORKS_ENVIRON bit is probably not needed, but I had it in mine so I left it in. Correct the paths to have your username instead of 'thomson'.)

5. Clone the Bloom repository: hg clone https://bitbucket.org/hatton/bloom-desktop.

This should leave you in the default branch, which is currently correct for Linux. Don't be misled into activating the Linux branch, which is no longer used. Note that after about September, you should be using a git repo, if things go as planned.

6. Get dependencies:

cd bloom-desktop/build.

./install-deps (Note the initial dot.)

./getDependencies-Linux.sh

cd ..

. environ (note the '.')

mozroots --import --sync

Run MonoDevelop using the shortcut. Open the solution BloomLinux.sln. Go to Edit ->Preferences, Packages/Sources. The list should include  https://www.nuget.org/api/v2/, and http://build.palaso.org/guestAuth/app/nuget/v1/FeedService.svc/ (not sure the second is necessary).

Select the BloomExe project. Do Project/Restore Packages. (Uses nuget to get some of Bloom's dependencies.)

7. At this point you should be able to build the whole BloomLinux solution (right-click in Solution pane, choose Build).

Hopefully we can streamline this process eventually.

# Registry settings

One responsibility of Bloom desktop is to handle url's starting with "bloom://"", such as those used in the bloom library web site when the user clicks "open in Bloom." Making this work requires some registry settings. These are automatically created when you install Bloom. Developers who need this functionality can get it using the build/bloom link.reg file. You need to edit this file first. It contains a full path to Bloom.exe, and the first part of the path will depend on where you have put your working folder. After adjusting that, just double-click it to create the registry entries for handling bloom: urls.

# Testers

Please see [Tips for Testing Palaso Software](https://docs.google.com/document/d/1dkp0edjJ8iqkrYeXdbQJcz3UicyilLR7GxMRIUAGb1E/edit)

# License

Bloom is open source, using the [MIT License](http://sil.mit-license.org). It is Copyright SIL International.