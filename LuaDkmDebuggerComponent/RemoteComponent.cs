using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Diagnostics;
using System.Linq;

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

        public HelperLocationsMessage locations;

        public DkmRuntimeBreakpoint testActiveBreakpoint = null;

        public bool pauseBreakpoints = false;
    }

    public class RemoteComponent : IDkmProcessExecutionNotification, IDkmRuntimeInstanceLoadCompleteNotification, IDkmCustomMessageForwardReceiver, IDkmRuntimeBreakpointReceived, IDkmRuntimeMonitorBreakpointHandler
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
            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(customMessage.Process);

            if (customMessage.MessageCode == MessageToRemote.createRuntime)
            {
                // NOTE: It would be great to create Lua DkmCustomModuleInstance on demand when we find that application uses Lua, but if you attempt to call DkmCustomModuleInstance.Create here, you will receive E_WRONG_COMPONENT for no apparent reason
            }
            else if (customMessage.MessageCode == MessageToRemote.luaHelperDataLocations)
            {
                var data = customMessage.Parameter1 as HelperLocationsMessage;

                Debug.Assert(data != null);

                processData.locations = data;
            }
            else if (customMessage.MessageCode == MessageToRemote.pauseBreakpoints)
            {
                processData.pauseBreakpoints = true;
            }
            else if (customMessage.MessageCode == MessageToRemote.resumeBreakpoints)
            {
                processData.pauseBreakpoints = false;
            }

            return null;
        }

        void IDkmRuntimeBreakpointReceived.OnRuntimeBreakpointReceived(DkmRuntimeBreakpoint runtimeBreakpoint, DkmThread thread, bool hasException, DkmEventDescriptorS eventDescriptor)
        {
            var process = thread.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(process);

            if (runtimeBreakpoint.SourceId == Guids.luaSupportBreakpointGuid)
            {
                if (processData.locations != null)
                {
                    if (runtimeBreakpoint.UniqueId == processData.locations.breakpointLuaHelperBreakpointHit)
                    {
                        // Breakpoint can get hit again after expression evaluation 'slips' the thread
                        if (processData.pauseBreakpoints)
                            return;

                        eventDescriptor.Suppress();

                        try
                        {
                            processData.testActiveBreakpoint.OnHit(thread, false);
                        }
                        catch (DkmException)
                        {
                            // In case another component evaluates a function
                        }

                        return;
                    }
                    else if (runtimeBreakpoint.UniqueId == processData.locations.breakpointLuaHelperStepComplete)
                    {
                        // Call OnStepComplete on DkmStepper

                        return;
                    }
                    else if (runtimeBreakpoint.UniqueId == processData.locations.breakpointLuaHelperStepInto)
                    {
                        // Call OnStepComplete on DkmStepper

                        return;
                    }
                    else if (runtimeBreakpoint.UniqueId == processData.locations.breakpointLuaHelperStepOut)
                    {
                        // Call OnStepComplete on DkmStepper

                        return;
                    }
                }

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
        }

        void IDkmRuntimeMonitorBreakpointHandler.EnableRuntimeBreakpoint(DkmRuntimeBreakpoint runtimeBreakpoint)
        {
            var process = runtimeBreakpoint.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(process);

            var runtimeInstructionBreakpoint = runtimeBreakpoint as DkmRuntimeInstructionBreakpoint;

            if (runtimeInstructionBreakpoint != null)
            {
                var customInstructionAddress = runtimeInstructionBreakpoint.InstructionAddress as DkmCustomInstructionAddress;

                if (customInstructionAddress != null)
                {
                    LuaBreakpointAdditionalData additionalData = new LuaBreakpointAdditionalData();

                    additionalData.ReadFrom(customInstructionAddress.AdditionalData.ToArray());

                    DebugHelpers.TryWriteVariable(process, processData.locations.helperBreakLineAddress, additionalData.instructionLine);

                    processData.testActiveBreakpoint = runtimeBreakpoint;
                }
            }
        }

        void IDkmRuntimeMonitorBreakpointHandler.TestRuntimeBreakpoint(DkmRuntimeBreakpoint runtimeBreakpoint)
        {
            var runtimeInstructionBreakpoint = runtimeBreakpoint as DkmRuntimeInstructionBreakpoint;

            if (runtimeInstructionBreakpoint != null)
            {
                var customInstructionAddress = runtimeInstructionBreakpoint.InstructionAddress as DkmCustomInstructionAddress;

                if (customInstructionAddress != null)
                {
                    LuaBreakpointAdditionalData additionalData = new LuaBreakpointAdditionalData();

                    additionalData.ReadFrom(customInstructionAddress.AdditionalData.ToArray());

                    if (additionalData.instructionLine == 0)
                        throw new Exception("Invalid instruction breakpoint location");
                }
            }
        }

        void IDkmRuntimeMonitorBreakpointHandler.DisableRuntimeBreakpoint(DkmRuntimeBreakpoint runtimeBreakpoint)
        {
            var process = runtimeBreakpoint.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(process);

            var runtimeInstructionBreakpoint = runtimeBreakpoint as DkmRuntimeInstructionBreakpoint;

            if (runtimeInstructionBreakpoint != null)
            {
                var customInstructionAddress = runtimeInstructionBreakpoint.InstructionAddress as DkmCustomInstructionAddress;

                if (customInstructionAddress != null)
                {
                    LuaBreakpointAdditionalData additionalData = new LuaBreakpointAdditionalData();

                    additionalData.ReadFrom(customInstructionAddress.AdditionalData.ToArray());

                    DebugHelpers.TryWriteVariable(process, processData.locations.helperBreakLineAddress, 0);

                    processData.testActiveBreakpoint = null;
                }
            }
        }
    }
}
