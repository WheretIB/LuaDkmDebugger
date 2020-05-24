using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Diagnostics;

namespace LuaDkmDebuggerComponent
{
    internal class LuaRemoteProcessData : DkmDataItem
    {
        public bool unrelatedRuntimeLoaded = false;

        public DkmLanguage language = null;
        public DkmCompilerId compilerId;

        public DkmCustomRuntimeInstance runtimeInstance = null;
        public DkmModule module = null;
        public DkmCustomModuleInstance moduleInstance = null;
    }

    public class RemoteComponent : IDkmProcessExecutionNotification, IDkmRuntimeInstanceLoadCompleteNotification, IDkmCustomMessageForwardReceiver, IDkmRuntimeBreakpointReceived
    {
        void IDkmProcessExecutionNotification.OnProcessPause(DkmProcess process, DkmProcessExecutionCounters processCounters)
        {
            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(process);

            if (processData.language == null)
            {
                processData.compilerId = new DkmCompilerId(Guids.luaCompilerGuid, Guids.luaLanguageGuid);

                processData.language = DkmLanguage.Create("Lua", processData.compilerId);
            }

            if (processData.runtimeInstance == null)
            {
                DkmRuntimeInstanceId runtimeId = new DkmRuntimeInstanceId(Guids.luaRuntimeGuid, 0);

                processData.runtimeInstance = DkmCustomRuntimeInstance.Create(process, runtimeId, null);
            }

            if (processData.module == null)
            {
                DkmModuleId moduleId = new DkmModuleId(Guid.NewGuid(), Guids.luaSymbolProviderGuid);

                processData.module = DkmModule.Create(moduleId, "lua.vm.code", processData.compilerId, process.Connection, null);
            }

            // NOTE: For some reason, creating module instance from OnProcessPause _after_ OnRuntimeInstanceLoadComplete for some unrelated runtime allows us to create module instances
            // Sadly, there is no way to find out if the application uses Lua and on-demand module instance creation from IDkmCustomMessageForwardReceiver.SendLower fails, so we always create a Lua module instance and hope that we don't mess up the debugger
            if (processData.moduleInstance == null && processData.unrelatedRuntimeLoaded)
            {
                DkmDynamicSymbolFileId symbolFileId = DkmDynamicSymbolFileId.Create(Guids.luaSymbolProviderGuid);

                processData.moduleInstance = DkmCustomModuleInstance.Create("lua_vm", "lua.vm.code", 0, processData.runtimeInstance, null, symbolFileId, DkmModuleFlags.None, DkmModuleMemoryLayout.Unknown, 0, 1, 0, "Lua vm code", false, null, null, null);

                processData.moduleInstance.SetModule(processData.module, true); // Can use reload?
            }
        }

        void IDkmProcessExecutionNotification.OnProcessResume(DkmProcess process, DkmProcessExecutionCounters processCounters)
        {
        }

        void IDkmRuntimeInstanceLoadCompleteNotification.OnRuntimeInstanceLoadComplete(DkmRuntimeInstance runtimeInstance, DkmWorkList workList, DkmEventDescriptor eventDescriptor)
        {
            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(runtimeInstance.Process);

            processData.unrelatedRuntimeLoaded = true;
        }

        DkmCustomMessage IDkmCustomMessageForwardReceiver.SendLower(DkmCustomMessage customMessage)
        {
            if (customMessage.MessageCode == MessageToRemote.createRuntime)
            {
                // NOTE: It would be great to create Lua DkmCustomModuleInstance on demand when we find that application uses Lua, but if you attempt to call DkmCustomModuleInstance.Create here, you will receive E_WRONG_COMPONENT for no apparent reason
            }

            return null;
        }

        void IDkmRuntimeBreakpointReceived.OnRuntimeBreakpointReceived(DkmRuntimeBreakpoint runtimeBreakpoint, DkmThread thread, bool hasException, DkmEventDescriptorS eventDescriptor)
        {
            var process = thread.Process;

            if (runtimeBreakpoint.SourceId == Guids.luaSupportBreakpointGuid)
            {
                thread.GetCurrentFrameInfo(out ulong retAddr, out ulong frameBase, out ulong vframe);

                var data = new SupportBreakpointHitMessage
                {
                    breakpointId = runtimeBreakpoint.UniqueId,
                    threadId = thread.UniqueId,
                    retAddr = retAddr,
                    frameBase = frameBase,
                    vframe = vframe
                };

                var message = DkmCustomMessage.Create(process.Connection, process, MessageToLocal.guid, MessageToLocal.luaSupportBreakpointHit, data, null);

                message.SendHigher();
            }
            else if (runtimeBreakpoint.SourceId == Guids.luaUserBreakpointGuid)
            {
                // TODO
            }
        }
    }
}
