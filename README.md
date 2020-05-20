# Debugger extensions for C++/Lua

---
This extensions enables limited support for inspection of lua (Vanilla 5.3.5) state in C++ applications during debugging in Visual Studio.

## Features:
 * Lua 'inline' stack frame insertion into the Call Stack
 * Mark Lua stack frames as non-user code

 ## Known Issues:
 * This extension will always add Lua module to the application (can be seen in 'Modules' section of the debugger) even when debugging applications with no Lua code (check notes in RemoteComponent.cs)
