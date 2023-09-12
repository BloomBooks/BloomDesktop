# FileMeddler

## Motivation

We get occasional unreproducible bug reports from users in which some other program is locking files we just created or modified. How rude! Two things that we know can do this are Dropbox and anti-virus programs. So we wrote FileMeddler as a testing tool that lets us torture-test a program's ability to handle this kind of interference gracefully.

## What it Does

FileMeddler watches all the files in a directory (including subdirectories). When one is added, modified, or renamed, it attempts to grab exclusive lock on the file for a couple seconds.

![](https://i.imgur.com/bY4gLSL.png)

## Usage
In a console, go to a directory where your application writes files, and start to meddle:

    cd somedirectory
    meddle

If  want to meddle in different directories (e.g. both where you store user files and temp directory), start a different console and a different copy of meddle.

## Parameters
There are no parameters you can set at this time.

## Possible Future Work
It would be nice to be able to control how aggressively  we meddle with files, including:

* time to wait before trying to lock the file
* how long to keep it locked
* how long before giving up on attempts to get the lock
* a "Dropbox simulation" preset

## License
This project is licensed under the terms of the MIT license.
