using System;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.DefaultPort;
using Microsoft.VisualStudio.Debugger.Native;

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

        internal static string FindFunctionAddress(DkmRuntimeInstance runtimeInstance, string name)
        {
            string result = null;

            foreach (var module in runtimeInstance.GetModuleInstances())
            {
                var address = (module as DkmNativeModuleInstance)?.FindExportName(name, IgnoreDataExports: true);

                if (address != null)
                {
                    result = $"0x{address.CPUInstructionPart.InstructionPointer:X}";
                    break;
                }
            }

            return result;
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

            if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                return null;

            return variableAddressData[0];
        }

        internal static short? ReadShortVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[2];

            if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                return null;

            return BitConverter.ToInt16(variableAddressData, 0);
        }

        internal static int? ReadIntVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[4];

            if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                return null;

            return BitConverter.ToInt32(variableAddressData, 0);
        }

        internal static uint? ReadUintVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[4];

            if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                return null;

            return BitConverter.ToUInt32(variableAddressData, 0);
        }

        internal static long? ReadLongVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[8];

            if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                return null;

            return BitConverter.ToInt64(variableAddressData, 0);
        }

        internal static ulong? ReadUlongVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[8];

            if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                return null;

            return BitConverter.ToUInt64(variableAddressData, 0);
        }

        internal static float? ReadFloatVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[4];

            if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                return null;

            return BitConverter.ToSingle(variableAddressData, 0);
        }

        internal static double? ReadDoubleVariable(DkmProcess process, ulong address)
        {
            byte[] variableAddressData = new byte[8];

            if (process.ReadMemory(address, DkmReadMemoryFlags.None, variableAddressData) == 0)
                return null;

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
            byte[] nameData = process.ReadMemoryString(address, DkmReadMemoryFlags.AllowPartialRead, 1, limit);

            if (nameData != null && nameData.Length != 0)
                return System.Text.Encoding.UTF8.GetString(nameData, 0, nameData.Length - 1);

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
            catch (Exception)
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
            catch (Exception)
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
            catch (Exception)
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
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        internal static bool TryWriteVariable(DkmProcess process, ulong address, float value)
        {
            try
            {
                process.WriteMemory(address, BitConverter.GetBytes(value));
            }
            catch (Exception)
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
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }
}
