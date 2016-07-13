Rebuilding this requires ruby and https://github.com/chrisvire/BuildUpdate
Here's the command line I used:

<your path to buildupdate.rb>\buildupdate.rb -t bt222 -f getDependencies-windows.sh -r ..

Explanation:

"-t bt222" points at the configuration that tracks this branch (in this case, the master branch)
"-f ____" gives what I want the file to be called
"-r .." takes care of moving the context up from build to the root directory