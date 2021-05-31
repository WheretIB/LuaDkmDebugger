using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using System;
using System.Linq;

namespace LuaDkmDebuggerComponent
{
    public class LocalWorkerComponent : IDkmCustomMessageForwardReceiver
    {
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
                ulong luaNewState = AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "lua_newstate", out _).GetValueOrDefault(0);
                ulong luaLibNewState = AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "luaL_newstate", out _).GetValueOrDefault(0);

                if (luaNewState != 0 || luaLibNewState != 0)
                {
                    var locations = new LuaLocationsMessage();

                    locations.luaExecuteAtStart = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaV_execute", out _).GetValueOrDefault(0);
                    locations.luaExecuteAtEnd = AttachmentHelpers.TryGetFunctionAddressAtDebugEnd(nativeModuleInstance, "luaV_execute", out _).GetValueOrDefault(0);

                    if (luaNewState != 0)
                    {
                        locations.luaNewStateAtStart = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "lua_newstate", out _).GetValueOrDefault(0);
                        locations.luaNewStateAtEnd = AttachmentHelpers.TryGetFunctionAddressAtDebugEnd(nativeModuleInstance, "lua_newstate", out _).GetValueOrDefault(0);
                    }
                    else
                    {
                        locations.luaNewStateAtStart = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaL_newstate", out _).GetValueOrDefault(0);
                        locations.luaNewStateAtEnd = AttachmentHelpers.TryGetFunctionAddressAtDebugEnd(nativeModuleInstance, "luaL_newstate", out _).GetValueOrDefault(0);
                    }

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

                    // Check if it's luajit
                    locations.ljSetMode = AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "luaJIT_setmode", out _).GetValueOrDefault(0);

                    if (locations.ljSetMode != 0)
                    {
                        locations.luaLibNewStateAtStart = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "luaL_newstate", out _).GetValueOrDefault(0);
                        locations.luaLibNewStateAtEnd = AttachmentHelpers.TryGetFunctionAddressAtDebugEnd(nativeModuleInstance, "luaL_newstate", out _).GetValueOrDefault(0);

                        locations.luaSetHook = AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "lua_sethook", out _).GetValueOrDefault(0);
                        locations.luaGetInfo = AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "lua_getinfo", out _).GetValueOrDefault(0);
                        locations.luaGetStack = AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "lua_getstack", out _).GetValueOrDefault(0);

                        locations.ljErrThrow = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(nativeModuleInstance, "lj_err_throw", out _).GetValueOrDefault(0);
                    }

                    return DkmCustomMessage.Create(process.Connection, process, MessageToLocal.guid, MessageToLocal.luaSymbols, locations.Encode(), null);
                }
            }

            return null;
        }
    }
}
