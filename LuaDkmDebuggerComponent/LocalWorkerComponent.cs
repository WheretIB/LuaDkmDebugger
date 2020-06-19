using Dia2Lib;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace LuaDkmDebuggerComponent
{
    public class LocalWorkerComponent : IDkmCustomMessageForwardReceiver
    {
        internal static void ReleaseComObject(object obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
                Marshal.ReleaseComObject(obj);
        }

        DkmCustomMessage IDkmCustomMessageForwardReceiver.SendLower(DkmCustomMessage customMessage)
        {
            var process = customMessage.Process;

            if (customMessage.MessageCode == MessageToLocalWorker.fetchLuaSymbols)
            {
                var moduleUniqueId = new Guid(customMessage.Parameter1 as byte[]);

                var nativeModuleInstance = process.GetNativeRuntimeInstance().GetNativeModuleInstances().FirstOrDefault(el => el.UniqueId == moduleUniqueId);

                if (nativeModuleInstance == null)
                    return null;

                // Check if Lua library is loaded
                if (AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "lua_newstate", out _).GetValueOrDefault(0) != 0)
                {
                    var locations = new LuaLocationsMessage();

                    locations.luaExecuteAtStart = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaV_execute", out _).GetValueOrDefault(0);
                    locations.luaExecuteAtEnd = AttachmentHelpers.TryGetFunctionAddressAtDebugEnd(nativeModuleInstance, "luaV_execute", out _).GetValueOrDefault(0);

                    locations.luaNewStateAtStart = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "lua_newstate", out _).GetValueOrDefault(0);
                    locations.luaNewStateAtEnd = AttachmentHelpers.TryGetFunctionAddressAtDebugEnd(nativeModuleInstance, "lua_newstate", out _).GetValueOrDefault(0);

                    locations.luaClose = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "lua_close", out _).GetValueOrDefault(0);
                    locations.closeState = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "close_state", out _).GetValueOrDefault(0);

                    locations.luaLoadFileEx = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaL_loadfilex", out _).GetValueOrDefault(0);
                    locations.luaLoadFile = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaL_loadfile", out _).GetValueOrDefault(0);
                    locations.solCompatLoadFileEx = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "kp_compat53L_loadfilex", out _).GetValueOrDefault(0);

                    locations.luaLoadBufferEx = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaL_loadbufferx", out _).GetValueOrDefault(0);
                    locations.luaLoadBuffer = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaL_loadbuffer", out _).GetValueOrDefault(0);

                    locations.luaLoad = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "lua_load", out _).GetValueOrDefault(0);

                    locations.luaError = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaB_error", out _).GetValueOrDefault(0);
                    locations.luaRunError = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaG_runerror", out _).GetValueOrDefault(0);
                    locations.luaThrow = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaD_throw", out _).GetValueOrDefault(0);

                    return DkmCustomMessage.Create(process.Connection, process, MessageToLocal.guid, MessageToLocal.luaSymbols, locations.Encode(), null);
                }
            }

            return null;
        }
    }
}
