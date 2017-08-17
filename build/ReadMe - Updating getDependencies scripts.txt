Updating the getDependencies-*.sh scripts requires ruby to be installed on
your machine and https://github.com/chrisvire/BuildUpdate to be cloned and up
to date.

Here's the command lines to use (assuming that BloomDesktop\build is your
current directory):

<your path to BuildUpdate>\buildupdate.rb -f getDependencies-windows.sh
<your path to BuildUpdate>\buildupdate.rb -f getDependencies-Linux.sh

Explanation:

The "-f ____" option specifies the script to update.  Since the file already
exists, no other command line options are needed.  To list the available
command line options, type

<your path to BuildUpdate>\buildupdate.rb --help

Note that unless you are asking for help, the program will prompt you for a
TeamCity username and password.

Of course, doing this on Linux requires using forward slashes (/) instead of
backslashes (\) as shown in the examples above.
