# Space-Engineers-Spaceport-Manager
A Space Engineers Programmable Block Script for automated docking/undocking of ships with advanced features.

From Space Engineers Official Steam Page (https://store.steampowered.com/app/244850/Space_Engineers/): "Space Engineers is a sandbox game about engineering, construction, exploration and survival in space and on planets. Players build space ships, space stations, planetary outposts of various sizes and uses, pilot ships and travel through space to explore planets and gather resources to survive." Space Engineers features a programmable block which offers opportunities for automation or ease of life task, which the script(s) here utilize to accomplish their task. These scripts are programmed in C#.

# Overview and Features
This particular script allows ships to automatically dock themselves at a "Spaceport" without any assistance from the player. Ships and stations can connect (which allows for transfer of inventory, fuel, and power) if they both of a connector and have both connectors attempt a connection when they are within proximity of one another. The script allows the user to setup different "docking connector groups", this can be used if a user wants different hangers for different spacecraft (for example, military spacecraft can be reserved to one hanger, mining drones to another, and personal exploration ships to another). Sometimes these hangers are buried deep inside of a spacestation or ship with not so straight forward paths to reach them, so this script features "pathing" which allows spacecraft to wireless receive a queue of points to travel to first before attempting to dock (damaging crashes / hilarity would ensue without this).

There is many features for adding connectors (with or without a specified group) and ensuring no duplicate ids occur, request to dock at a particular connector or connector group, if a request to dock at a specific connector group that is already full is received, wait until a connector from the group is free (placing the request into a 'Docking Que') and then send an update back to the request granting it permission to dock, also ensure only one ship is using a particular path at a time to avoid collisions, automated antenna management (only one message can be sent per ingame 'tick', if multiple are needed to be sent, then the rest of the messages are simply added to a 'Messague Que'). The most advanced feature (in my opinion), is that the spaceport is able to mobile, this means all stored coordinates for group pathing must be translated back and forth from relative to world coordinates with respect to both position and orientation, which takes a fair bit of math of accomplish.

# Storing data through parsing
Due to the way Space Engineers is programmed, all script data must be saved into a single 'Storage' string everytime the world is saved (usually every 15-30 minutes), so all Connector information (whether or not is occupied or about to become occupied), grouping and pathing information, and waiting queues must saved in such a manner that can be parsed after. All wireless communications are setup similarly, where only a string can be passed from one grid (station or ship) to another, therefore parsing of all intergrid communication is necessary.

# How to use
One script manages and is placed on the spaceport (SpaceportProgram.cs), and the other script is placed on any participating spacecraft (ShipProgram.cs).


# Additional Info
This script is sadly outdated as of now due to the intergrid communcation structure being overhauled (and same for writing to LCD's screens apparently), but with some modification it can work again. 
