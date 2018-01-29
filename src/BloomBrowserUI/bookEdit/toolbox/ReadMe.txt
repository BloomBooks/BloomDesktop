The intended code organization of the toolbox is as follows:
- Files in the root folder (this one, toolbox) should contain only generic code for managing the toolbox as a whole
- Anything that is part of the implementation of a particular tool should be in one of the child folders, of which there is one for each tool in the accordion.

A partial exception to this is a chunk of code which is either shared by the Decodable Reader and Leveled Reader tool,
or at least has not yet been teased apart. Much of this code is in files or folders with names starting with Reader or containing Synphony.
For now, all such code is in the decodableReader folder (since a slightly larger share of it really belongs there).

It is a goal of our design that code outside the folder of an individual tool should not know about the tool.
Ideally it should be possible to add a new tool to the accordion without modifying any file outside the new folder
that is added, except for modifying toolbox.jade to get the files included.