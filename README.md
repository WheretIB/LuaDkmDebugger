# C++ Debugger Extensions for Lua

---
This Visual Studio extension enables debugging of Lua scripts running inside C++ applications with Lua library.

Supported Lua versions:
* Lua 5.3
* Lua 5.2 with LUA_NANTRICK (default configuration)
* Lua 5.1

[Extension on Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=wheretib.lua-dkm-debug)

## Features:
 * Lua call stack frames in the Call Stack window
 * Lua library call stack frames are marked as non-user code
 * Jump to Lua source/line from Lua call stack frames
 * Function arguments, local variables and upvalues are displayed in the 'Locals' window
 * Lua expression evaluation in Watch, Immediate and similar elements
 * Numeric and user data values can be modified
 * Breakpoints
    * Known issue: breakpoints must be set after the debugger is launched
    * Up to 256 breakpoints are supported
 * Step Over, Step Into and Step Out
 * Conditional breakpoints

![Example debug session](https://github.com/WheretIB/LuaDkmDebugger/blob/master/resource/front_image_2.png?raw=true)

![Example debug session](https://github.com/WheretIB/LuaDkmDebugger/blob/master/resource/front_image.png?raw=true)

## Additional configuration

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

## Troubleshooting

If you experience issues with the extension, you can enable debug logs in 'Extensions -> Lua Debugger' menu if you wish to provide additional info in your report.

### Breakpoints and Stepping information

As in other Lua debuggers, breakpoints are implemented using Lua library hooks. The hook is set as soon as Lua state is created.

If you use your own Lua hooks in your application, you can call the previous hook function from your hook.

This debugger or other debuggers might override each other hooks, so if breakpoints are not hit, this might be the reason.

If you experience issues with the debugger on launch, you can disable attachment to your process in 'Extensions -> Lua Debugger' menu. Debug logs can be enabled there as well if you wish to report the issue. (note that names of your Lua scripts might be included in the log). If debugger attachment is disabled, all features except for breakpoints and stepping will still work.

## Known Issues:
 * This extension will always add Lua module to the application (can be seen in 'Modules' section of the debugger) even when debugging applications with no Lua code
 * Lua 5.2 is assumed to be compiled with LUA_NANTRICK in x86 (default configuration)
 * Breakpoints must be set after the debugger is launched
 * Step Into from Lua into C++ doesn't work at the moment
