# Changelog

## [0.9.7] - 2021-06-14

### Added

- Experimental support for LuaJIT (2.0.5 x86/x64, 2.1.0 x86/x64). Limitations include:
    - Scripts loaded from a file are not captured for breakpoint placement/stepping immediately
    - Only writes to 'number' are supported with LUA_GC64
    - Conditional breakpoints are not supported
    - Only one global state is supported for breakpoints/stepping
    - Breakpoints based on function name are not supported
- Status bar for information about detected Lua library and helper attachment state

### Changed

- Fixed a case when failed helper attachment to x64 process could suspend thread indefinitely

## [0.9.6] - 2021-04-06

### Added

- Lua Script List panel can be opened from menu

### Changed

- Improved script name mapping to potential file name

## [0.9.5] - 2021-03-21

### Added

- String values can be modified
- Support for assignments in watch and immediate windows
- Support for table and string length operator

### Changed

- Watch values are refreshed after modification of a related value
- Fixed bool value modification in Lua 5.4
- Fixed nested linear array indexing
- Fixed breakpoints not being activated immediately after script load
- Fixed invalid call error display location
- Fixed error display location in Lua 5.1
- Fixed table size and content display in Lua 5.2 (nil entries)

## [0.9.4] - 2020-09-02

### Changed

- Added handler for unexpected exceptions in DIA SDK symbol load functions

## [0.9.3] - 2020-07-20

### Changed

- Fixed performance issue with Lua error handling

## [0.9.2] - 2020-07-07

### Added

- Optimization for table summary and element lookups using batched memory reads and lazy evaluation
- Optimization for function locals list using batched memory reads
- Function location display now works with Lua 5.1
- Function name caching for Lua 5.1 stack frames
- Conditional breakpoint support for Lua 5.4

### Changed

- Fixed Lua version lookup and hook setup when Lua library is built with optimization
- Fixed crash on function data lookup when debugging Lua 5.1
- Fix for Lua state creation hook when library is built with optimization
- Fix for internal Lua function display on 'Step Out' when debugging Lua 5.1
- Fixed conditional breakpoints

## [0.9.1] - 2020-06-23

### Added

- Support for full process dump debug

## [0.9.0] - 2020-06-22

### Added

- Support for Lua 5.4
- Lookup in __index metadata table in expression evaluation
- Location display for Lua function values and Jump to Source context menu option
- Alternative name is generated for unnamed scripts

### Changed

- Fixes for Lua libraries built with optimizations
- Fix for Lua sources without a valid name
- Compatibility Mode improvements
- Lua table address is now displayed in Watch
- Stability fixes
- Additional source file search locations
- '.lua' is added to temporary file names if the Lua source name doesn't contain it already
- Fixed long debugger startup when application is linked with /DEBUG:FULL and symbol file is large

## [0.8.8] - 2020-06-10

### Added

- Support for Visual Studio 2017

### Changed

- Fixed debug hook setup for Lua 5.1 and 5.2
- Fixed empty files being saved to Temp folder

## [0.8.7] - 2020-06-10

### Added

- Compatibility Mode for customized Lua interpreters
- Added 'Initialize...' action to the extension menu that can be used if extension hasn't loaded yet

### Changed

- Breakpoints can now be set before application has launched
- Lua hooks are only set when breakpoints are active or stepping through code was performed

## [0.8.2] - 2020-06-06

### Added

- Support for debug helper injection into x86 UWP apps (for breakpoints and stepping)

### Changed

- Fixed debug helper initialization after Lua state is created

## [0.8.1] - 2020-06-05

### Added

- Allow debugging when Lua is built as a DLL from [@fsfod](https://github.com/fsfod).

## [0.8.0] - 2020-06-03

### Added

- Quick Info tooltip display with variable value evaluation on mouse over in the code window
- Global environment variable lookup in expression evaluation
- External C function/closure pointer display with 'Go To Source' provided by Visual Studio
- Hexadecimal value support in expression evaluation
- Assertion failure, error call and runtime errors are displayed as unhandled exceptions (Break on Error option)
- User data meta-table value display
- ':' member access is now handled in expression evaluation
- When Lua library is used together with sol library, C++ object in user data is available (may work for custom user data if meta-table contains '__type.name' string with C++ type name)

### Changed

- Fixed hexadecimal value formatting & crash
- Expression evaluation optimization (caching call info, function data, upvalues and table values are not fetched until accessed)

## [0.7.7] - 2020-06-01

### Added

- Lua module code is marked as user code
- Support for debug helper injection into x64 UWP apps (for breakpoints and stepping)

### Changed

- Cleaned up module instance creation
- Silent debug helper injection failures will not hang the app on a suspended thread

## [0.7.5] - 2020-05-31

### Changed

- Fixed crash when crash dump is debugged

## [0.7.4] - 2020-05-31

### Added

- Support for conditional breakpoints
- Added display of source files that haven't been found disk
- Option to show hidden Lua call stack frames (for troubleshooting)

### Changed

- Fixed hook crash when application Lua library is compiled with a different LUA_IDSIZE
- Fixed conditional breakpoints in Lua 5.3 built for x64
- Some documents can be linked to known scripts using content comparison even when script source name doesn't match any file on disk

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

## Code changes

[unreleased]https://github.com/olivierlacan/keep-a-changelog/compare/v0.9.7...HEAD

[0.9.7]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.9.6...v0.9.7

[0.9.6]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.9.5...v0.9.6

[0.9.5]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.9.4...v0.9.5

[0.9.4]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.9.3...v0.9.4

[0.9.3]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.9.2...v0.9.3

[0.9.2]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.9.1...v0.9.2

[0.9.1]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.9.0...v0.9.1

[0.9.0]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.8.8...v0.9.0

[0.8.8]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.8.7...v0.8.8

[0.8.7]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.8.2...v0.8.7

[0.8.2]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.8.1...v0.8.2

[0.8.1]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.8.0...v0.8.1

[0.8.0]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.7.7...v0.8.0

[0.7.7]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.7.5...v0.7.7

[0.7.5]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.7.4...v0.7.5

[0.7.4]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.6.0...v0.7.4

[0.6.0]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.4.1...v0.6.0

[0.4.1]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.3.0...v0.4.1

[0.3.0]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.2.6...v0.3.0

[0.2.6]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.2.5...v0.2.6

[0.2.5]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.2.4...v0.2.5

[0.2.4]https://github.com/WheretIB/LuaDkmDebugger/compare/v0.2.3...v0.2.4

[0.2.3]https://github.com/WheretIB/LuaDkmDebugger/releases/tag/v0.2.3
