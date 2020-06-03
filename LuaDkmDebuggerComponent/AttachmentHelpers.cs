using Dia2Lib;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.Native;
using System;
using System.Runtime.InteropServices;

namespace LuaDkmDebuggerComponent
{
    internal class Kernel32
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, UIntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);
    }

    internal class AttachmentHelpers
    {
        internal static void ReleaseComObject(object obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
                Marshal.ReleaseComObject(obj);
        }

        internal static IDiaSymbol TryGetDiaSymbols(DkmModuleInstance moduleInstance, out string error)
        {
            if (moduleInstance == null)
            {
                error = $"TryGetDiaSymbols() Module instance is null";
                return null;
            }

            if (moduleInstance.Module == null)
            {
                error = $"TryGetDiaSymbols() Module is null";
                return null;
            }

            IDiaSession diaSession;
            try
            {
                diaSession = (IDiaSession)moduleInstance.Module.GetSymbolInterface(typeof(IDiaSession).GUID);
            }
            catch (InvalidCastException)
            {
                error = $"TryGetDiaSymbols() diaSession InvalidCastException";
                return null;
            }

            diaSession.findChildren(null, SymTagEnum.SymTagExe, null, 0, out IDiaEnumSymbols exeSymEnum);

            if (exeSymEnum.count != 1)
            {
                error = $"TryGetDiaSymbols() exeSymEnum.count {exeSymEnum.count} != 1";

                ReleaseComObject(diaSession);
                return null;
            }

            var symbol = exeSymEnum.Item(0);

            ReleaseComObject(exeSymEnum);
            ReleaseComObject(diaSession);

            error = null;
            return symbol;
        }

        internal static IDiaSymbol TryGetDiaSymbol(IDiaSymbol symbol, SymTagEnum symTag, string name, out string error)
        {
            symbol.findChildren(symTag, name, 1, out IDiaEnumSymbols enumSymbols);

            if (enumSymbols.count != 1)
            {
                error = $"TryGetDiaSymbols() enumSymbols.count {enumSymbols.count} != 1";

                ReleaseComObject(enumSymbols);

                return null;
            }

            error = null;
            return enumSymbols.Item(0u);
        }

        internal static IDiaSymbol TryGetDiaFunctionSymbol(DkmNativeModuleInstance moduleInstance, string name, out string error)
        {
            var moduleSymbols = TryGetDiaSymbols(moduleInstance, out error);

            if (moduleSymbols == null)
                return null;

            var functionSymbol = TryGetDiaSymbol(moduleSymbols, SymTagEnum.SymTagFunction, name, out error);

            if (functionSymbol == null)
            {
                ReleaseComObject(moduleSymbols);

                return null;
            }

            ReleaseComObject(moduleSymbols);

            return functionSymbol;
        }

        internal static ulong? TryGetFunctionAddress(DkmNativeModuleInstance moduleInstance, string name, out string error)
        {
            var functionSymbol = TryGetDiaFunctionSymbol(moduleInstance, name, out error);

            if (functionSymbol == null)
                return null;

            uint rva = functionSymbol.relativeVirtualAddress;

            ReleaseComObject(functionSymbol);

            return moduleInstance.BaseAddress + rva;
        }

        internal static ulong? TryGetFunctionAddressAtDebugStart(DkmNativeModuleInstance moduleInstance, string name, out string error)
        {
            var functionSymbol = TryGetDiaFunctionSymbol(moduleInstance, name, out error);

            if (functionSymbol == null)
                return null;

            var functionStartSymbol = TryGetDiaSymbol(functionSymbol, SymTagEnum.SymTagFuncDebugStart, null, out error);

            if (functionStartSymbol == null)
            {
                ReleaseComObject(functionSymbol);

                return null;
            }

            uint rva = functionStartSymbol.relativeVirtualAddress;

            ReleaseComObject(functionStartSymbol);
            ReleaseComObject(functionSymbol);

            return moduleInstance.BaseAddress + rva;
        }

        internal static ulong? TryGetFunctionAddressAtDebugEnd(DkmNativeModuleInstance moduleInstance, string name, out string error)
        {
            var functionSymbol = TryGetDiaFunctionSymbol(moduleInstance, name, out error);

            if (functionSymbol == null)
                return null;

            var functionEndSymbol = TryGetDiaSymbol(functionSymbol, SymTagEnum.SymTagFuncDebugEnd, null, out error);

            if (functionEndSymbol == null)
            {
                ReleaseComObject(functionSymbol);

                return null;
            }

            uint rva = functionEndSymbol.relativeVirtualAddress;

            ReleaseComObject(functionEndSymbol);
            ReleaseComObject(functionSymbol);

            return moduleInstance.BaseAddress + rva;
        }

        internal static Guid? CreateHelperFunctionBreakpoint(DkmNativeModuleInstance nativeModuleInstance, string functionName)
        {
            var functionAddress = TryGetFunctionAddressAtDebugStart(nativeModuleInstance, functionName, out string error);

            if (functionAddress != null)
            {
                LocalComponent.log.Debug($"Creating breakpoint in '{functionName}'");

                var nativeAddress = nativeModuleInstance.Process.CreateNativeInstructionAddress(functionAddress.Value);

                var breakpoint = DkmRuntimeInstructionBreakpoint.Create(Guids.luaSupportBreakpointGuid, null, nativeAddress, false, null);

                breakpoint.Enable();

                return breakpoint.UniqueId;
            }
            else
            {
                var nativeFunctionAddress = FindFunctionAddress(nativeModuleInstance, functionName);

                if (nativeFunctionAddress != 0)
                {
                    LocalComponent.log.Debug($"Creating 'native' breakpoint in '{functionName}'");

                    var nativeAddress = nativeModuleInstance.Process.CreateNativeInstructionAddress(nativeFunctionAddress);

                    var breakpoint = DkmRuntimeInstructionBreakpoint.Create(Guids.luaSupportBreakpointGuid, null, nativeAddress, false, null);

                    breakpoint.Enable();

                    return breakpoint.UniqueId;
                }
                else
                {
                    LocalComponent.log.Warning($"Failed to create breakpoint in '{functionName}' with {error}");
                }
            }

            return null;
        }

        internal static ulong FindFunctionAddress(DkmNativeModuleInstance nativeModuleInstance, string functionName)
        {
            var address = nativeModuleInstance.FindExportName(functionName, IgnoreDataExports: true);

            if (address != null)
            {
                LocalComponent.log.Debug($"Found helper library '{functionName}' function at 0x{address.CPUInstructionPart.InstructionPointer:x}");

                return address.CPUInstructionPart.InstructionPointer;
            }

            LocalComponent.log.Warning($"Failed to find helper library '{functionName}' function");

            return 0;
        }

        internal static ulong FindVariableAddress(DkmNativeModuleInstance nativeModuleInstance, string variableName)
        {
            var address = nativeModuleInstance.FindExportName(variableName, IgnoreDataExports: false);

            if (address != null)
            {
                LocalComponent.log.Debug($"Found helper library '{variableName}' variable at 0x{address.CPUInstructionPart.InstructionPointer:x}");

                return address.CPUInstructionPart.InstructionPointer;
            }

            LocalComponent.log.Warning($"Failed to find helper library '{variableName}' variable");

            return 0;
        }

        internal static Guid? CreateTargetFunctionBreakpointAtDebugStart(DkmProcess process, DkmNativeModuleInstance moduleWithLoadedLua, string name, string desc, out ulong breakAddress)
        {
            var address = TryGetFunctionAddressAtDebugStart(moduleWithLoadedLua, name, out string error);

            if (address != null)
            {
                LocalComponent.log.Debug($"Hooking Lua '{desc}' ({name}) function (address 0x{address.Value:x})");

                var nativeAddress = process.CreateNativeInstructionAddress(address.Value);

                var breakpoint = DkmRuntimeInstructionBreakpoint.Create(Guids.luaSupportBreakpointGuid, null, nativeAddress, false, null);

                breakpoint.Enable();

                breakAddress = address.Value;
                return breakpoint.UniqueId;
            }
            else
            {
                LocalComponent.log.Warning($"Failed to create breakpoint in '{name}' with {error}");
            }

            breakAddress = 0;
            return null;
        }

        internal static Guid? CreateTargetFunctionBreakpointAtDebugEnd(DkmProcess process, DkmNativeModuleInstance moduleWithLoadedLua, string name, string desc, out ulong breakAddress)
        {
            var address = TryGetFunctionAddressAtDebugEnd(moduleWithLoadedLua, name, out string error);

            if (address != null)
            {
                LocalComponent.log.Debug($"Hooking Lua '{desc}' ({name}) function (address 0x{address.Value:x})");

                var nativeAddress = process.CreateNativeInstructionAddress(address.Value);

                var breakpoint = DkmRuntimeInstructionBreakpoint.Create(Guids.luaSupportBreakpointGuid, null, nativeAddress, false, null);

                breakpoint.Enable();

                breakAddress = address.Value;
                return breakpoint.UniqueId;
            }
            else
            {
                LocalComponent.log.Warning($"Failed to create breakpoint in '{name}' with {error}");
            }

            breakAddress = 0;
            return null;
        }
    }
}
