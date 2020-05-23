# C++ Debugger Extensions for Lua

---
This Visual Studio extension enables limited support for inspection of Lua state in C++ applications during debug.

Supported Lua versions:
* Lua 5.3
* Lua 5.2 with LUA_NANTRICK (default configuration)
* Lua 5.1

[Extension on Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=wheretib.lua-dkm-debug)

### Features:
 * Lua 'inline' stack frame insertion into the Call Stack
 * Mark Lua library stack frames as non-user code
 * Jump to Lua source/line from Lua stack frames
 * Function argument and local variable display in the 'Locals' watch section
 * Local variable lookup and expression evaluation in Watch, Immediate and similar elements

![Example debug session](https://github.com/WheretIB/LuaDkmDebugger/blob/master/resource/front_image.png?raw=true)

### Additional configuration

In the default configuration, debugger searches for script files in current working directory and application executable directory.

Application may provide Lua with script file paths that do not match the file system. To help the debugger find your script files in this scenario, additional script search paths can be provided using an optional configuration file.

`lua_dkm_debug.json` file can be placed in application working directory or near the executable file.

Add `ScriptPaths` key with an array of additional search paths.

```
{
  "ScriptPaths": [
    "../",
    "../scripts/base/"
  ]
}
```

### Known Issues:
 * This extension will always add Lua module to the application (can be seen in 'Modules' section of the debugger) even when debugging applications with no Lua code (check notes in RemoteComponent.cs)
 * Lua 5.2 is assumed to be compiled with LUA_NANTRICK in x86 (deafult configuration)
 * When Lua stack frames are separated by C/C++ frames, stepping might display two top Lua call stack blocks in reverse order, double click any frame for Visual Studio to refresh with correct order
