using Dia2Lib;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.Native;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace LuaDkmDebuggerComponent
{
    internal class Kernel32
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, UIntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr LocalFree(IntPtr hMem);
    }

    internal class Advapi32
    {
        public enum SE_OBJECT_TYPE
        {
            SE_UNKNOWN_OBJECT_TYPE = 0,
            SE_FILE_OBJECT,
            SE_SERVICE,
            SE_PRINTER,
            SE_REGISTRY_KEY,
            SE_LMSHARE,
            SE_KERNEL_OBJECT,
            SE_WINDOW_OBJECT,
            SE_DS_OBJECT,
            SE_DS_OBJECT_ALL,
            SE_PROVIDER_DEFINED_OBJECT,
            SE_WMIGUID_OBJECT,
            SE_REGISTRY_WOW64_32KEY
        }

        internal enum MULTIPLE_TRUSTEE_OPERATION
        {
            NO_MULTIPLE_TRUSTEE,
            TRUSTEE_IS_IMPERSONATE
        }

        internal enum TRUSTEE_FORM
        {
            TRUSTEE_IS_SID = 0,
            TRUSTEE_IS_NAME,
            TRUSTEE_BAD_FORM,
            TRUSTEE_IS_OBJECTS_AND_SID,
            TRUSTEE_IS_OBJECTS_AND_NAME
        }

        internal enum TRUSTEE_TYPE
        {
            TRUSTEE_IS_UNKNOWN = 0,
            TRUSTEE_IS_USER,
            TRUSTEE_IS_GROUP,
            TRUSTEE_IS_DOMAIN,
            TRUSTEE_IS_ALIAS,
            TRUSTEE_IS_WELL_KNOWN_GROUP,
            TRUSTEE_IS_DELETED,
            TRUSTEE_IS_INVALID,
            TRUSTEE_IS_COMPUTER
        }

        internal enum ACCESS_MODE : uint
        {
            NOT_USED_ACCESS = 0,
            GRANT_ACCESS,
            SET_ACCESS,
            REVOKE_ACCESS,
            SET_AUDIT_SUCCESS,
            SET_AUDIT_FAILURE
        }

        internal enum ACCESS_MASK : uint
        {
            GENERIC_ALL = 0x10000000, //268435456,
            GENERIC_READ = 0x80000000, //2147483648L,
            GENERIC_WRITE = 0x40000000, //1073741824,
            GENERIC_EXECUTE = 0x20000000, //536870912,
            STANDARD_RIGHTS_READ = 0x00020000, //131072
            STANDARD_RIGHTS_WRITE = 0x00020000,
            SHARE_ACCESS_READ = 0x1200A9, // 1179817
            SHARE_ACCESS_WRITE = 0x1301BF, // 1245631
            SHARE_ACCESS_FULL = 0x1f01ff // 2032127
        }

        internal enum ACCESS_INHERITANCE : uint
        {
            NO_INHERITANCE = 0,
            OBJECT_INHERIT_ACE = 0x1,
            CONTAINER_INHERIT_ACE = 0x2,
            NO_PROPAGATE_INHERIT_ACE = 0x4,
            INHERIT_ONLY_ACE = 0x8,
            INHERITED_ACE = 0x10,
            SUB_OBJECTS_ONLY_INHERIT = ACCESS_INHERITANCE.OBJECT_INHERIT_ACE | ACCESS_INHERITANCE.INHERIT_ONLY_ACE,
            SUB_CONTAINERS_ONLY_INHERIT = ACCESS_INHERITANCE.CONTAINER_INHERIT_ACE | ACCESS_INHERITANCE.INHERIT_ONLY_ACE,
            SUB_CONTAINERS_AND_OBJECTS_INHERIT = ACCESS_INHERITANCE.CONTAINER_INHERIT_ACE | ACCESS_INHERITANCE.OBJECT_INHERIT_ACE,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TRUSTEE
        {
            public IntPtr MultipleTrustee;
            public MULTIPLE_TRUSTEE_OPERATION MultipleTrusteeOperation;
            public TRUSTEE_FORM TrusteeForm;
            public TRUSTEE_TYPE TrusteeType;
            public IntPtr Name;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct EXPLICIT_ACCESS
        {
            public ACCESS_MASK AccessPermissions;
            public ACCESS_MODE AccessMode;
            public ACCESS_INHERITANCE Inheritance;
            public TRUSTEE trustee;
        }

        [DllImport("advapi32.dll", EntryPoint = "GetNamedSecurityInfoW", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int GetNamedSecurityInfo(string objectName, SE_OBJECT_TYPE objectType, System.Security.AccessControl.SecurityInfos securityInfo, out IntPtr sidOwner, out IntPtr sidGroup, out IntPtr dacl, out IntPtr sacl, out IntPtr securityDescriptor);

        [DllImport("advapi32.dll", EntryPoint = "SetNamedSecurityInfoW", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int SetNamedSecurityInfo(string objectName, SE_OBJECT_TYPE objectType, System.Security.AccessControl.SecurityInfos securityInfo, IntPtr sidOwner, IntPtr sidGroup, IntPtr dacl, IntPtr sacl);

        [DllImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int ConvertStringSidToSid(string stringSid, out IntPtr sid);

        [DllImport("advapi32.dll", EntryPoint = "SetEntriesInAclW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int SetEntriesInAcl(int cCountOfExplicitEntries, ref EXPLICIT_ACCESS pListOfExplicitEntries, IntPtr oldAcl, out IntPtr newAcl);

        public static int AdjustAccessControlListForUwp(string dllPath)
        {
            // Get current Access Control List
            int errorCode = GetNamedSecurityInfo(dllPath, SE_OBJECT_TYPE.SE_FILE_OBJECT, SecurityInfos.DiscretionaryAcl, out _, out _, out IntPtr currentAcl, out _, out IntPtr securityDescriptor);

            if (errorCode != 0)
            {
                Debug.WriteLine("Call to 'GetNamedSecurityInfoA' failed");

                return 11;
            }

            // sid for all application packages
            if (ConvertStringSidToSid("S-1-15-2-1", out IntPtr sid) == 0)
            {
                Debug.WriteLine("Call to 'ConvertStringSidToSidA' failed");

                return 12;
            }

            EXPLICIT_ACCESS access = new EXPLICIT_ACCESS
            {
                AccessPermissions = ACCESS_MASK.GENERIC_READ | ACCESS_MASK.GENERIC_EXECUTE,
                AccessMode = ACCESS_MODE.SET_ACCESS,
                Inheritance = ACCESS_INHERITANCE.SUB_CONTAINERS_AND_OBJECTS_INHERIT,

                trustee = new TRUSTEE
                {
                    TrusteeForm = TRUSTEE_FORM.TRUSTEE_IS_SID,
                    TrusteeType = TRUSTEE_TYPE.TRUSTEE_IS_WELL_KNOWN_GROUP,
                    Name = sid
                }
            };

            // Set new access entry in the Access Control List
            errorCode = SetEntriesInAcl(1, ref access, currentAcl, out IntPtr updatedAcl);

            if (errorCode != 0)
            {
                Debug.WriteLine("Call to 'SetEntriesInAclA' failed");

                return 13;
            }

            // Set new Access Control List
            errorCode = SetNamedSecurityInfo(dllPath, SE_OBJECT_TYPE.SE_FILE_OBJECT, SecurityInfos.DiscretionaryAcl, IntPtr.Zero, IntPtr.Zero, updatedAcl, IntPtr.Zero);

            if (errorCode != 0)
            {
                Debug.WriteLine("Call to 'SetNamedSecurityInfoA' failed");

                return 14;
            }

            if (securityDescriptor != IntPtr.Zero)
                Kernel32.LocalFree(securityDescriptor);

            if (updatedAcl != IntPtr.Zero)
                Kernel32.LocalFree(updatedAcl);

            return 0;
        }
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

        internal static IDiaSymbol TryGetDiaFunctionSymbol(DkmModuleInstance moduleInstance, string name, out string error)
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

        internal static ulong? TryGetFunctionAddress(DkmModuleInstance moduleInstance, string name, out string error)
        {
            try
            {
                var functionSymbol = TryGetDiaFunctionSymbol(moduleInstance, name, out error);

                if (functionSymbol == null)
                    return null;

                uint rva = functionSymbol.relativeVirtualAddress;

                ReleaseComObject(functionSymbol);

                return moduleInstance.BaseAddress + rva;
            }
            catch (Exception ex)
            {
                error = "TryGetFunctionAddress() Unexpected error: " + ex.ToString();
            }

            return null;
        }

        internal static ulong? TryGetFunctionAddressAtDebugStart(DkmModuleInstance moduleInstance, string name, out string error)
        {
            try
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
            catch (Exception ex)
            {
                error = "TryGetFunctionAddressAtDebugStart() Unexpected error: " + ex.ToString();
            }

            return null;
        }

        internal static ulong? TryGetFunctionAddressAtDebugEnd(DkmModuleInstance moduleInstance, string name, out string error)
        {
            try
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
            catch (Exception ex)
            {
                error = "TryGetFunctionAddressAtDebugEnd() Unexpected error: " + ex.ToString();
            }

            return null;
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

        internal static DkmRuntimeInstructionBreakpoint CreateTargetFunctionBreakpointObjectAtAddress(DkmProcess process, DkmModuleInstance moduleWithLoadedLua, string name, string desc, ulong address, bool enabled)
        {
            if (address != 0)
            {
                LocalComponent.log.Debug($"Hooking Lua '{desc}' ({name}) function (address 0x{address:x})");

                var nativeAddress = process.CreateNativeInstructionAddress(address);

                var breakpoint = DkmRuntimeInstructionBreakpoint.Create(Guids.luaSupportBreakpointGuid, null, nativeAddress, false, null);

                if (enabled)
                    breakpoint.Enable();

                return breakpoint;
            }
            else
            {
                LocalComponent.log.Warning($"Failed to create breakpoint in '{name}' with missing address");
            }

            return null;
        }

        internal static Guid? CreateTargetFunctionBreakpointAtAddress(DkmProcess process, DkmModuleInstance moduleWithLoadedLua, string name, string desc, ulong address)
        {
            DkmRuntimeInstructionBreakpoint breakpoint = CreateTargetFunctionBreakpointObjectAtAddress(process, moduleWithLoadedLua, name, desc, address, true);

            if (breakpoint != null)
                return breakpoint.UniqueId;

            return null;
        }

        internal static DkmRuntimeInstructionBreakpoint CreateTargetFunctionBreakpointObjectAtDebugStart(DkmProcess process, DkmModuleInstance moduleWithLoadedLua, string name, string desc, out ulong breakAddress, bool enabled)
        {
            var address = TryGetFunctionAddressAtDebugStart(moduleWithLoadedLua, name, out string error);

            if (address != null)
            {
                LocalComponent.log.Debug($"Hooking Lua '{desc}' ({name}) function (address 0x{address.Value:x})");

                var nativeAddress = process.CreateNativeInstructionAddress(address.Value);

                var breakpoint = DkmRuntimeInstructionBreakpoint.Create(Guids.luaSupportBreakpointGuid, null, nativeAddress, false, null);

                if (enabled)
                    breakpoint.Enable();

                breakAddress = address.Value;
                return breakpoint;
            }
            else
            {
                LocalComponent.log.Warning($"Failed to create breakpoint in '{name}' with {error}");
            }

            breakAddress = 0;
            return null;
        }

        internal static Guid? CreateTargetFunctionBreakpointAtDebugStart(DkmProcess process, DkmModuleInstance moduleWithLoadedLua, string name, string desc, out ulong breakAddress)
        {
            DkmRuntimeInstructionBreakpoint breakpoint = CreateTargetFunctionBreakpointObjectAtDebugStart(process, moduleWithLoadedLua, name, desc, out breakAddress, true);

            if (breakpoint != null)
                return breakpoint.UniqueId;

            return null;
        }

        internal static Guid? CreateTargetFunctionBreakpointAtDebugEnd(DkmProcess process, DkmModuleInstance moduleWithLoadedLua, string name, string desc, out ulong breakAddress)
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
