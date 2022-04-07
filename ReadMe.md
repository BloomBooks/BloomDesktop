# Let's Grow Some Libraries

Bloom Desktop is an [award winning](http://allchildrenreading.org/sil-international-wins-enabling-writers-prize-for-software-solution-to-childrens-book-shortage/) software solution to the children's book shortage among most of the world's languages. It is an application for Windows and Linux that dramatically "lowers the bar" for creating, translating, and sharing books. WIth Bloom, communities can do the work for themselves instead of depending on outsiders.

Internally, Bloom is a hybrid. It started as a c#/winforms app with an embedded browser for editing documents and an embedded Adobe Acrobat for displaying PDF outputs. It wants to grow up to be a pure react-driven offline-capable web app, perhaps with a c# backend. In its current adolescence, Bloom is hybrid of c#/web app in which the bits of the UI are gradually moving to html.

|            | Windows | Linux |
| :--------: | :-----: | :---: |
| Build      | ![http://build.palaso.org/viewType.html?buildTypeId=bt222&guest=1](https://img.shields.io/teamcity/http/build.palaso.org/s/bt222.svg?style=flat)| [![Build Status](https://jenkins.lsdev.sil.org/buildStatus/icon?job=Bloom-Linux-any-master-debug)](https://jenkins.lsdev.sil.org/view/Bloom/job/Bloom-Linux-any-master-debug/) |
| .net Unit tests | (part of above build)| [![Build Status](https://jenkins.lsdev.sil.org/buildStatus/icon?job=Bloom-Linux-any-master-debug-Tests)](https://jenkins.lsdev.sil.org/view/Bloom/job/Bloom-Linux-any-master-debug-Tests/)|

# Development Process

## Kanban / Bug Reports

We use [YouTrack](https://silbloom.myjetbrains.com) Kanban boards. Errors (via email or api) also flow into YouTrack, and we do some support from there by @mentioning users.


## Continuous Build System

Each time code is checked in, an automatic build begins on our [TeamCity build server](https://build.palaso.org/project/Bloom), running all the unit tests. Similarly, when there is a new version of some Bloom dependency (e.g. Palaso, PdfDroplet, our fork of GeckoFX), that server automatically rebuilds Bloom. This automatic build doesn't publish a new installer, however. That kind of build is launched manually, by pressing a button on the TeamCity page. This "publish" process builds Bloom, makes and installer, rsyncs it to the distribution server, and writes out a little bit of html which the [Bloom download page](http://bloomlibrary.org/#/installers) then displays to the user.

## Building Browser Source Code ##

Install yarn (https://yarnpkg.com/lang/en/docs/install/#windows-stable)

You'll need [nodejs](https://nodejs.org/en/) installed.  As time goes on, the required version of nodejs changes. Bloom 4.1 (and several earlier versions of Bloom) builds with nodejs 6.10.1. Bloom 4.2 (and later versions of Bloom) builds with nodejs 8.10.0. To make this feasible, we use [nvm-windows](https://github.com/coreybutler/nvm-windows) on Windows and [nvm](https://github.com/creationix/nvm) on Linux to install and manage which version of nodejs is active for the build process. To install nvm on Windows, go to  [nvm-windows releases](https://github.com/coreybutler/nvm-windows/releases) and download the latest nvm-setup.zip file. Unzip the downloaded file and run the nvm-setup.exe program to install nvm. Once nvm has been installed for windows, run these commands in a command window to install the needed versions of nodejs. This needs to be done only once.

    nvm install 1.2.3 # or whatever the version should be. Check "engines" field of package.json.
    nvm list

To install nvm (and the needed versions of nodejs) on Linux, run these commands in a bash shell window. Again, this needs to be done only once. (The version of nvm may change over time. Check the [nvm home page](https://github.com/creationix/nvm#install-script) for exact details.)

    wget -qO- https://raw.githubusercontent.com/creationix/nvm/v0.33.8/install.sh | bash
    export NVM_DIR="$HOME/.nvm"
    [ -s "$NVM_DIR/nvm.sh" ] && \. "$NVM_DIR/nvm.sh" # This loads nvm as a shell function
    command -v nvm                                   # This should respond with "nvm"
    nvm install 1.2.3 # or whatever the version should be. Check "engines" field of package.json.
    nvm list

This will build and test the Typescript, javascript, less, and pug:

    nvm use 16.13.1    # or whatever the current package.json "engine" specifies
    cd ../BloomBrowserUI
    yarn
    yarn build
    yarn test


To watch code, use `yarn watchCode`

## Building C# Source Code ##

To build the .net/C# part of Bloom, on Windows you'll need at least a 2015 Community edition of Visual Studio (for Linux, see Linux section below).

It will avoid some complications if you update to master branch now, before adding the dependencies that follow.

Before you'll be able to build, you'll have to download some binary dependencies (see below).

On Linux you'll also have to make sure that you have installed some dependencies (see below).

To build Bloom you can open and build the solution in Visual Studio or MonoDevelop, or build from the command line using msbuild/xbuild.

## Get Binary Dependencies

In the `build` directory, open a git bash shell and run

`./getDependencies-windows.sh`

or

`./getDependencies-linux.sh`

That will take several minutes the first time, and afterward will be quick as it only downloads what has changed. When you change branches, run this again.


#### About Bloom Dependencies

Javascript dependencies should be introduced using

   yarn add <modulename>

Our **[Palaso libraries](https://github.com/sillsdev/libpalaso)** hold the classes that are common to multiple products.

Our **[PdfDroplet ](http://pdfdroplet.palaso.org)**engine drives the booklet-making in the Publish tab. If you need to build PdfDroplet from source, see [projects.palaso.org/projects/pdfdroplet/wiki](http://projects.palaso.org/projects/palaso/wiki).

Our **[Chorus](https://github.com/sillsdev/chorus)** library provides the Send/Receive functionality.

**GeckoFX**: Much of Bloom happens in its embedded Firefox browser. This has two parts: the XulRunner engine, and the [GeckoFX .net wrapper](https://bitbucket.org/geckofx). As of Bloom version 3.8, this comes in via a nuget package.

Bloom uses various web services that require identification. We can't really keep those a secret, but we can at least not make them google'able by not checking them into github. To get the file that contains user and test-level authorization codes, just get the connections.dll file out of a shipping version of a Bloom, and place it in your Bloom/DistFiles directory.


## Disable analytics

We don't want developer and tester runs (and crashes) polluting our statistics. On Windows, add the environment variable "feedback" with value "off". On Linux, edit $HOME/.profile and add:

        export FEEDBACK=off

# Special instructions for building on Linux


At various points you will be asked for your password.

1. Install `wget`

        sudo apt-get install wget

2. Add the SIL keys for the main and testing SIL package repositories

        wget -O - http://linux.lsdev.sil.org/downloads/sil-testing.gpg | sudo apt-key add -
        wget -O - http://packages.sil.org/sil.gpg | sudo apt-key add -

3. Make sure you have your system set up to look at the main and testing SIL repositories

    Install Synaptic if you haven't (sudo apt-get install synaptic).

    You need Synaptic to look in some extra places for components. In Synaptic, go to
    `Settings->Repositories`, `Other Software` tab. You want to see the following lines (replace
    `precise` with your distribution version):

        http://packages.sil.org/ubuntu precise main
        http://packages.sil.org/ubuntu precise-experimental main
        http://linux.lsdev.sil.org/ubuntu precise main
        http://linux.lsdev.sil.org/ubuntu precise-experimental main

    If some are missing, click add and paste the missing line, then insert 'deb' at the start,
    then confirm.

    (May help to check for and remove any lines that refer to the obsolete `ppa.palaso.org`, if
    you've been doing earlier work on SIL stuff.)

4. Update your system:

        sudo apt-get update
        sudo apt-get upgrade

5. Clone the Bloom repository:

        mkdir $HOME/palaso
        cd $HOME/palaso
        git clone https://github.com/BloomBooks/BloomDesktop.git

    This should leave you in the default branch, which is currently correct for Linux. Don't be
    misled into activating the Linux branch, which is no longer used.

6. Install MonoDevelop 5 (or later)

    A current MonoDevelop can be found on launchpad: https://launchpad.net/~ermshiperete/+archive/ubuntu/monodevelop
    or https://launchpad.net/~ermshiperete/+archive/ubuntu/monodevelop-beta.

    Follow the installation instructions on the launchpad website (currently a link called "Read about installing").

    Make a shortcut to launch MonoDevelop (or just use this command line). The shortcut should execute something like this:

        bash -c 'PATH=/opt/monodevelop/bin:$PATH; \
            export MONO_ENVIRON="$HOME/palaso/bloom-desktop/environ"; \
            export MONO_GAC_PREFIX=/opt/monodevelop:/opt/mono4-sil:/usr:/usr/local; \
            monodevelop-launcher.sh'

    Correct the path in MONO_ENVIRON to point to the Bloom source code directory.

7. Install the dependencies needed for Bloom

        cd $HOME/palaso/bloom-desktop/build
        ./install-deps # (Note the initial dot)

    This will also install a custom mono version in `/opt/mono4-sil`. However, to successfully
    use it with MonoDevelop, you'll need to do some additional steps.

    Copy this script to /opt/mono4-sil/bin:

        wget https://raw.githubusercontent.com/sillsdev/mono-calgary/develop/mono-sil
        sudo mv mono4-sil /opt/mono4-sil/bin
        sudo chmod +x /opt/mono4-sil/bin/mono-sil

    Delete /opt/mono4-sil/bin/mono and create two symlinks instead:

        sudo rm /opt/mono4-sil/bin/mono
        sudo ln -s /opt/mono4-sil/bin/mono-sgen /opt/mono4-sil/bin/mono-real
        sudo ln -s /opt/mono4-sil/bin/mono-sil /opt/mono4-sil/bin/mono

8. Get binary dependencies:

        cd $HOME/palaso/bloom-desktop/build
        ./getDependencies-Linux.sh  # (Note the initial dot)
        cd ..
        . environ #(note the '.')
        sudo mozroots --import --sync

9. Open solution in MonoDevelop

    Run MonoDevelop using the shortcut. Open the solution BloomLinux.sln. Go to
    `Edit -> Preferences`, `Packages/Sources`. The list should include
    `https://www.nuget.org/api/v2/`, and `http://build.palaso.org/guestAuth/app/nuget/v1/FeedService.svc/`
    (not sure the second is necessary).

    Add the /opt/mono4-sil/ as additional runtime in MonoDevelop (`Edit -> Preferences`, `Projects/.NET Runtimes`). Currently, this is 3.0.4.1 (Oct. 2014).

    When you want to run Bloom you'll have to select the /opt/mono4-sil/ as current runtime (Project/Active Runtime).

    At this point you should be able to build the whole BloomLinux solution (right-click in
    Solution pane, choose Build).

10. You'll have to remember to redo the symlink step (end of #7) every time you install a new mono4-sil package. You'll notice quickly if you forget because you get an error saying that it can't find XULRUNNER - that's an indication that it didn't source the environ file, either because the wrong runtime is selected or /opt/mono4-sil/bin/mono points to mono-sgen instead of the wrapper script mono4-sil.

Hopefully we can streamline this process eventually.

# Registry settings

One responsibility of Bloom desktop is to handle url's starting with "bloom://"", such as those used in the bloom library web site when the user clicks "open in Bloom." Making this work requires some registry settings. These are automatically created when you run Bloom. If you have multiple versions installed, just make sure that the one you ran most recently is the one you want to do the download.

# License

Bloom is open source, using the [MIT License](http://sil.mit-license.org). It is Copyright SIL International.
"Bloom" is a registered trademark of SIL International.
