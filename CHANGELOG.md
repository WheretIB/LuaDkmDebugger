# Changelog

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
- Fixed conditional breakpoints in Lua 5.3 build for x64
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

[unreleased]https://github.com/olivierlacan/keep-a-changelog/compare/v0.8.0...HEAD

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
