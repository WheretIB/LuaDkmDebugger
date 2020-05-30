# Changelog

## [0.6.0] - 2020-05-30

### Added

- Support for breakpoints
- Support for Step Over, Step In and Step Out
- Debug logs
- Function name cache for fast stack filter for Lua 5.2 and 5.3
- Lua function upvalue support in expression evaluation and Locals window

### Changed

- Fixed access to local functions list of a Lua function
- Fixed enumeration size and double completion callback call in Locals window
- Additional stack filter optimizations
- Hide Lua call stack frames between internal Lua calls for Lua 5.3

## [0.4.1] - 2020-05-23

### Added

- Support for Lua 5.2 in default configuration (LUA_NANTRICK is enabled for x86 and disabled for other platforms)
- Support for Lua 5.1
- Numeric and light user data values can be modified

### Changed

- Fixed missing locals when their lifetime ends after current instruction (off-by-one error)
- Lua global function is called 'main' instead of '__global'
- Identifier names can contain '_' symbol
- Stack frame location is now provided for Lua 5.3 finalizers and Lua 5.2/5.3 tail-called functions
- Fixed out-of-order/missing call stack sections
- Stability improvements when accessing target process memory
- Reduce amount of expression evaluation requests to C++ debugger
- Fixed amount of skipped frames on stacks with multiple language transitions
- Support call stacks with transitions between different Lua states/threads

## [0.3.0] - 2020-05-22

### Added

- Complex expression evaluation
- Support for relative Lua script source paths

### Changed

- Adjusted frame instruction pointer value to point on the currently executing instruction (fixes missing locals)

## [0.2.6] - 2020-05-21

### Changed

- Fixed search for files without '@' at the start of the name

## [0.2.5] - 2020-05-21

### Changed

- Fixed interaction with C++ debugger that broke C++ breakpoint placement

## [0.2.4] - 2020-05-21

### Added

- Support for configuration file with additional script search directories

## [0.2.3] - 2020-05-21

### First Release

[0.4.1]https://github.com/WheretIB/LuaDkmDebugger/commit/133da5f5a03d99c0a63307007d8af7515d673e41
[0.3.0]https://github.com/WheretIB/LuaDkmDebugger/commit/cfe2f159c700790c23d24ef986dad49b62cb92bf
[0.2.6]https://github.com/WheretIB/LuaDkmDebugger/commit/90c303512f3fd85e518ac3bbb14f9585bdc57fb2
[0.2.5]https://github.com/WheretIB/LuaDkmDebugger/commit/93b8abe68f9a572b59637df7dca97738f8cbe259
[0.2.4]https://github.com/WheretIB/LuaDkmDebugger/commit/48c028f1757c05925f3da0dbff511c10e6c0f3ed
[0.2.3]https://github.com/WheretIB/LuaDkmDebugger/commit/48433c036535efb9f4c58f548db56d368df9e52f
