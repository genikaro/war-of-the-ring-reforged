War of the Ring Reforged - test build {BUILD}
=============================================

An unofficial, non-commercial fan engine that plays The Lord of the Rings: War of the Ring
(Liquid Entertainment, 2003) from your own installed copy of the game. It contains no game
content: it reads your installation in place and never changes or copies it. See NOTICE.md.

This is a test build of a prototype: skirmish only (the campaign is not built yet).


What you need
-------------
- Windows 10 or 11, 64-bit.
- Your own installation of The Lord of the Rings: War of the Ring (the folder that holds
  "Archives" and "Database"). English and Russian installs have been tested.
- Nothing else: the .NET runtime is included.


Starting
--------
1. Unzip this folder anywhere (not inside the original game's folder).
2. Run WarOfTheRingReforged.exe.
3. The launcher opens. If your War of the Ring folder is not filled in, click Browse and pick
   the folder that holds "Archives" and "Database". The launcher checks that it reads
   correctly (a second or two).
4. Click Start Reforged. The main menu opens; choose Skirmish.
   Next time the launcher remembers the folder.

WarOfTheRingReforged.console.exe starts the same game with a console window that shows the
log, which helps when something goes wrong.


Debug mode
----------
Start the game as WarOfTheRingReforged.exe -Debug (from a command prompt, or add -Debug to a
shortcut's target) for the testing tools: the Sandbox in the main menu, the build number and
FPS at the top right, and the debug tools and cheats on F4. F10 hides the interface in any mode.


Where things are kept
---------------------
Settings, the launcher's folder and the log (logs\godot.log) are in
  %APPDATA%\Godot\app_userdata\War of the Ring Reforged — Prototype
Deleting that folder resets everything. Nothing is written into the game's folder.


Reporting a problem
-------------------
Please give the build number ({BUILD}), what you did,
what happened, and attach logs\godot.log from the folder above.


License
-------
The engine's code and documentation are GPL-3.0-or-later (LICENSE). The original game, its
content and its marks belong to their owners (NOTICE.md).
