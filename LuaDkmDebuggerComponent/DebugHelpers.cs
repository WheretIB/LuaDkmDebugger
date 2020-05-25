using System;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.DefaultPort;
using Microsoft.VisualStudio.Debugger.Native;
using Dia2Lib;
using System.Runtime.InteropServices;
using System.Security.Policy;

namespace LuaDkmDebuggerComponent
{
    static class DebugHelpers
    {
        internal static T GetOrCreateDataItem<T>(DkmDataContainer container) where T : DkmDataItem, new()
        {
            T item = container.GetDataItem<T>();

            if (item != null)
                return item;

            item = new T();

            container.SetDataItem<T>(DkmDataCreationDisposition.CreateNew, item);

            return item;
        }

        internal static ulong FindFunctionAddress(DkmRuntimeInstance runtimeInstance, string name)
        {
            foreach (var module in runtimeInstance.GetModuleInstances())
            {
                var address = (module as DkmNativeModuleInstance)?.FindExportName(name, IgnoreDataExports: true);

                if (address != null)
                    return address.CPUInstructionPart.InstructionPointer;
            }

            return 0;
        }

        internal static bool Is64Bit(DkmProcess process)
        {
            return (process.SystemInformation.Flags & DkmSystemInformationFlags.Is64Bit) != 0;
        }

        internal static int GetPointerSize(DkmProcess process)
        {
            return Is64Bit(process) ? 8 : 4;
        }

        internal static byte? ReadByteVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[1];

            try
            {
                if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                    return null;
            }
            catch (DkmException)
            {
                return null;
            }

            return variableAddressData[0];
        }

        internal static short? ReadShortVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[2];

            try
            {
                if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                    return null;
            }
            catch (DkmException)
            {
                return null;
            }

            return BitConverter.ToInt16(variableAddressData, 0);
        }

        internal static int? ReadIntVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[4];

            try
            {
                if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                    return null;
            }
            catch (DkmException)
            {
                return null;
            }

            return BitConverter.ToInt32(variableAddressData, 0);
        }

        internal static uint? ReadUintVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[4];

            try
            {
                if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                    return null;
            }
            catch (DkmException)
            {
                return null;
            }

            return BitConverter.ToUInt32(variableAddressData, 0);
        }

        internal static long? ReadLongVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[8];

            try
            {
                if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                    return null;
            }
            catch (DkmException)
            {
                return null;
            }

            return BitConverter.ToInt64(variableAddressData, 0);
        }

        internal static ulong? ReadUlongVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[8];

            try
            {
                if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                    return null;
            }
            catch (DkmException)
            {
                return null;
            }

            return BitConverter.ToUInt64(variableAddressData, 0);
        }

        internal static float? ReadFloatVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[4];

            try
            {
                if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                    return null;
            }
            catch (DkmException)
            {
                return null;
            }

            return BitConverter.ToSingle(variableAddressData, 0);
        }

        internal static double? ReadDoubleVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[8];

            try
            {
                if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                    return null;
            }
            catch (DkmException)
            {
                return null;
            }

            return BitConverter.ToDouble(variableAddressData, 0);
        }

        internal static ulong? ReadPointerVariable(DkmProcess process, ulong address)
        {
            if (!Is64Bit(process))
                return ReadUintVariable(process, address);

            return ReadUlongVariable(process, address);
        }

        internal static string ReadStringVariable(DkmProcess process, ulong address, int limit)
        {
            try
            {
                byte[] nameData = process.ReadMemoryString(address, DkmReadMemoryFlags.AllowPartialRead, 1, limit);

                if (nameData != null && nameData.Length != 0)
                    return System.Text.Encoding.UTF8.GetString(nameData, 0, nameData.Length - 1);
            }
            catch (DkmException)
            {
                return null;
            }

            return null;
        }

        internal static ulong? ReadPointerVariable(DkmProcess process, string name)
        {
            var runtimeInstance = process.GetNativeRuntimeInstance();

            if (runtimeInstance != null)
            {
                foreach (var module in runtimeInstance.GetModuleInstances())
                {
                    var nativeModule = module as DkmNativeModuleInstance;

                    var variableAddress = nativeModule?.FindExportName(name, IgnoreDataExports: false);

                    if (variableAddress != null)
                        return ReadPointerVariable(process, variableAddress.CPUInstructionPart.InstructionPointer);
                }
            }

            return null;
        }

        internal static bool TryWriteVariable(DkmProcess process, ulong address, byte value)
        {
            try
            {
                process.WriteMemory(address, new byte[1] { value });
            }
            catch (DkmException)
            {
                return false;
            }

            return true;
        }

        internal static bool TryWriteVariable(DkmProcess process, ulong address, short value)
        {
            try
            {
                process.WriteMemory(address, BitConverter.GetBytes(value));
            }
            catch (DkmException)
            {
                return false;
            }

            return true;
        }

        internal static bool TryWriteVariable(DkmProcess process, ulong address, int value)
        {
            try
            {
                process.WriteMemory(address, BitConverter.GetBytes(value));
            }
            catch (DkmException)
            {
                return false;
            }

            return true;
        }

        internal static bool TryWriteVariable(DkmProcess process, ulong address, uint value)
        {
            try
            {
                process.WriteMemory(address, BitConverter.GetBytes(value));
            }
            catch (DkmException)
            {
                return false;
            }

            return true;
        }

        internal static bool TryWriteVariable(DkmProcess process, ulong address, long value)
        {
            try
            {
                process.WriteMemory(address, BitConverter.GetBytes(value));
            }
            catch (DkmException)
            {
                return false;
            }

            return true;
        }

        internal static bool TryWriteVariable(DkmProcess process, ulong address, ulong value)
        {
            try
            {
                process.WriteMemory(address, BitConverter.GetBytes(value));
            }
            catch (DkmException)
            {
                return false;
            }

            return true;
        }

        // TODO: separate explicit functions to avoid implicit casts
        internal static bool TryWriteVariable(DkmProcess process, ulong address, float value)
        {
            try
            {
                process.WriteMemory(address, BitConverter.GetBytes(value));
            }
            catch (DkmException)
            {
                return false;
            }

            return true;
        }

        internal static bool TryWriteVariable(DkmProcess process, ulong address, double value)
        {
            try
            {
                process.WriteMemory(address, BitConverter.GetBytes(value));
            }
            catch (DkmException)
            {
                return false;
            }

            return true;
        }

        internal static byte? ReadStructByte(DkmProcess process, ref ulong address)
        {
            var result = ReadByteVariable(process, address);

            address += 1ul;

            return result;
        }

        internal static short? ReadStructShort(DkmProcess process, ref ulong address)
        {
            address = (address + 1ul) & ~1ul; // Align

            var result = ReadShortVariable(process, address);

            address += 2ul;

            return result;
        }

        internal static int? ReadStructInt(DkmProcess process, ref ulong address)
        {
            address = (address + 3ul) & ~3ul; // Align

            var result = ReadIntVariable(process, address);

            address += 4ul;

            return result;
        }

        internal static uint? ReadStructUint(DkmProcess process, ref ulong address)
        {
            address = (address + 3ul) & ~3ul; // Align

            var result = ReadUintVariable(process, address);

            address += 4ul;

            return result;
        }

        internal static long? ReadStructLong(DkmProcess process, ref ulong address)
        {
            address = (address + 7ul) & ~7ul; // Align

            var result = ReadLongVariable(process, address);

            address += 8ul;

            return result;
        }

        internal static ulong? ReadStructUlong(DkmProcess process, ref ulong address)
        {
            address = (address + 7ul) & ~7ul; // Align

            var result = ReadUlongVariable(process, address);

            address += 8ul;

            return result;
        }

        internal static ulong? ReadStructPointer(DkmProcess process, ref ulong address)
        {
            if (!Is64Bit(process))
                return ReadStructUint(process, ref address);

            return ReadStructUlong(process, ref address);
        }

        internal static void SkipStructByte(DkmProcess process, ref ulong address)
        {
            address += 1ul;
        }

        internal static void SkipStructShort(DkmProcess process, ref ulong address)
        {
            address = (address + 1ul) & ~1u; // Align

            address += 2ul;
        }

        internal static void SkipStructInt(DkmProcess process, ref ulong address)
        {
            address = (address + 3ul) & ~3u; // Align

            address += 4ul;
        }

        internal static void SkipStructUint(DkmProcess process, ref ulong address)
        {
            address = (address + 3ul) & ~3ul; // Align

            address += 4ul;
        }

        internal static void SkipStructLong(DkmProcess process, ref ulong address)
        {
            address = (address + 7ul) & ~7ul; // Align

            address += 8ul;
        }

        internal static void SkipStructUlong(DkmProcess process, ref ulong address)
        {
            address = (address + 7ul) & ~7ul; // Align

            address += 8ul;
        }

        internal static void SkipStructPointer(DkmProcess process, ref ulong address)
        {
            if (!Is64Bit(process))
                SkipStructUint(process, ref address);
            else
                SkipStructUlong(process, ref address);
        }

        internal static void ReleaseComObject(object obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
                Marshal.ReleaseComObject(obj);
        }

        internal static IDiaSymbol TryGetDiaSymbols(DkmModuleInstance moduleInstance)
        {
            if (moduleInstance.Module == null)
                return null;

            IDiaSession diaSession;
            try
            {
                diaSession = (IDiaSession)moduleInstance.Module.GetSymbolInterface(typeof(IDiaSession).GUID);
            }
            catch (InvalidCastException)
            {
                return null;
            }

            diaSession.findChildren(null, SymTagEnum.SymTagExe, null, 0, out IDiaEnumSymbols exeSymEnum);

            if (exeSymEnum.count != 1)
            {
                ReleaseComObject(diaSession);

                return null;
            }

            var symbol = exeSymEnum.Item(0);

            ReleaseComObject(exeSymEnum);
            ReleaseComObject(diaSession);

            return symbol;
        }

        internal static IDiaSymbol TryGetDiaSymbol(IDiaSymbol symbol, SymTagEnum symTag, string name)
        {
            symbol.findChildren(symTag, name, 1, out IDiaEnumSymbols enumSymbols);

            if (enumSymbols.count != 1)
            {
                ReleaseComObject(enumSymbols);

                return null;
            }

            return enumSymbols.Item(0u);
        }

        internal static IDiaSymbol TryGetDiaFunctionSymbol(DkmNativeModuleInstance moduleInstance, string name)
        {
            var moduleSymbols = TryGetDiaSymbols(moduleInstance);

            if (moduleSymbols == null)
                return null;

            var functionSymbol = TryGetDiaSymbol(moduleSymbols, SymTagEnum.SymTagFunction, name);

            if (functionSymbol == null)
            {
                ReleaseComObject(moduleSymbols);

                return null;
            }

            ReleaseComObject(moduleSymbols);

            return functionSymbol;
        }

        internal static ulong? TryGetFunctionAddress(DkmNativeModuleInstance moduleInstance, string name)
        {
            var functionSymbol = TryGetDiaFunctionSymbol(moduleInstance, name);

            if (functionSymbol == null)
                return null;

            uint rva = functionSymbol.relativeVirtualAddress;

            ReleaseComObject(functionSymbol);

            return moduleInstance.BaseAddress + rva;
        }

        internal static ulong? TryGetFunctionAddressAtDebugStart(DkmNativeModuleInstance moduleInstance, string name)
        {
            var functionSymbol = TryGetDiaFunctionSymbol(moduleInstance, name);

            if (functionSymbol == null)
                return null;

            var functionStartSymbol = TryGetDiaSymbol(functionSymbol, SymTagEnum.SymTagFuncDebugStart, null);

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

        internal static ulong? TryGetFunctionAddressAtDebugEnd(DkmNativeModuleInstance moduleInstance, string name)
        {
            var functionSymbol = TryGetDiaFunctionSymbol(moduleInstance, name);

            if (functionSymbol == null)
                return null;

            var functionEndSymbol = TryGetDiaSymbol(functionSymbol, SymTagEnum.SymTagFuncDebugEnd, null);

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
    }
}
