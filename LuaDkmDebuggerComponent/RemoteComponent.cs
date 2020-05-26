using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Stepping;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LuaDkmDebuggerComponent
{
    public class LuaBreakpoint
    {
        public int instructionLine = 0;
        public ulong functionAddress = 0;

        public DkmRuntimeBreakpoint runtimeBreakpoint = null;
    }

    internal class LuaRemoteProcessData : DkmDataItem
    {
        public bool unrelatedRuntimeLoaded = false;

        public DkmLanguage language = null;
        public DkmCompilerId compilerId;

        public DkmCustomRuntimeInstance runtimeInstance = null;
        public DkmModule module = null;
        public DkmCustomModuleInstance moduleInstance = null;

        public HelperLocationsMessage locations;

        public List<LuaBreakpoint> activeBreakpoints = new List<LuaBreakpoint>();

        public bool pauseBreakpoints = false;

        public DkmStepper activeStepper = null;
    }

    public class RemoteComponent : IDkmProcessExecutionNotification, IDkmRuntimeInstanceLoadCompleteNotification, IDkmCustomMessageForwardReceiver, IDkmRuntimeBreakpointReceived, IDkmRuntimeMonitorBreakpointHandler, IDkmRuntimeStepper
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
                var data = new HelperLocationsMessage();

                data.ReadFrom(customMessage.Parameter1 as byte[]);

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

        void UpdateBreakpoints(DkmProcess process, LuaRemoteProcessData processData)
        {
            int count = processData.activeBreakpoints.Count;

            if (count > 256)
                count = 256;

            ulong pointerSize = (ulong)DebugHelpers.GetPointerSize(process);

            for (int i = 0; i < count; i++)
            {
                ulong dataAddress = processData.locations.helperBreakDataAddress + (ulong)i * 2 * pointerSize;

                var breakpoint = processData.activeBreakpoints[i];

                DebugHelpers.TryWritePointerVariable(process, dataAddress, (ulong)breakpoint.instructionLine);
                DebugHelpers.TryWritePointerVariable(process, dataAddress + pointerSize, breakpoint.functionAddress);
            }

            DebugHelpers.TryWriteIntVariable(process, processData.locations.helperBreakCountAddress, count);
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
                            var breakpointPos = DebugHelpers.ReadUintVariable(process, processData.locations.helperBreakHitIdAddress);

                            if (!breakpointPos.HasValue)
                                return;

                            if (breakpointPos.Value < processData.activeBreakpoints.Count)
                            {
                                var breakpoint = processData.activeBreakpoints[(int)breakpointPos.Value];

                                breakpoint.runtimeBreakpoint.OnHit(thread, false);
                            }
                        }
                        catch (DkmException)
                        {
                            // In case another component evaluates a function
                        }

                        return;
                    }
                    else if (runtimeBreakpoint.UniqueId == processData.locations.breakpointLuaHelperStepComplete)
                    {
                        if (processData.activeStepper != null)
                        {
                            var activeStepper = processData.activeStepper;

                            ClearStepperData(process, processData);

                            activeStepper.OnStepComplete(thread, false);
                        }

                        return;
                    }
                    else if (runtimeBreakpoint.UniqueId == processData.locations.breakpointLuaHelperStepInto)
                    {
                        if (processData.activeStepper != null)
                        {
                            var activeStepper = processData.activeStepper;

                            ClearStepperData(process, processData);

                            activeStepper.OnStepComplete(thread, false);
                        }

                        return;
                    }
                    else if (runtimeBreakpoint.UniqueId == processData.locations.breakpointLuaHelperStepOut)
                    {
                        if (processData.activeStepper != null)
                        {
                            var activeStepper = processData.activeStepper;

                            ClearStepperData(process, processData);

                            activeStepper.OnStepComplete(thread, false);
                        }

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

                var message = DkmCustomMessage.Create(process.Connection, process, MessageToLocal.guid, MessageToLocal.luaSupportBreakpointHit, data.Encode(), null);

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
                    LuaAddressEntityData entityData = new LuaAddressEntityData();

                    entityData.ReadFrom(customInstructionAddress.EntityId.ToArray());

                    LuaBreakpointAdditionalData additionalData = new LuaBreakpointAdditionalData();

                    additionalData.ReadFrom(customInstructionAddress.AdditionalData.ToArray());

                    var breakpoint = new LuaBreakpoint
                    {
                        instructionLine = additionalData.instructionLine,
                        functionAddress = entityData.functionAddress,
                        runtimeBreakpoint = runtimeBreakpoint
                    };

                    processData.activeBreakpoints.Add(breakpoint);
                    UpdateBreakpoints(process, processData);
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
                    LuaAddressEntityData entityData = new LuaAddressEntityData();

                    entityData.ReadFrom(customInstructionAddress.EntityId.ToArray());

                    LuaBreakpointAdditionalData additionalData = new LuaBreakpointAdditionalData();

                    additionalData.ReadFrom(customInstructionAddress.AdditionalData.ToArray());

                    processData.activeBreakpoints.RemoveAll(el => el.instructionLine == additionalData.instructionLine && el.functionAddress == entityData.functionAddress);
                    UpdateBreakpoints(process, processData);
                }
            }
        }

        void ClearStepperData(DkmProcess process, LuaRemoteProcessData processData)
        {
            DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepOverAddress, 0);
            DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepIntoAddress, 0);
            DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepOutAddress, 0);
            DebugHelpers.TryWriteIntVariable(process, processData.locations.helperSkipDepthAddress, 0);

            processData.activeStepper = null;
        }

        void IDkmRuntimeStepper.BeforeEnableNewStepper(DkmRuntimeInstance runtimeInstance, DkmStepper stepper)
        {
            // Don't have anything to do here right now
        }

        bool IDkmRuntimeStepper.OwnsCurrentExecutionLocation(DkmRuntimeInstance runtimeInstance, DkmStepper stepper, DkmStepArbitrationReason reason)
        {
            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(runtimeInstance.Process);

            // Can't handle steps without an address
            if (stepper.StartingAddress == null)
                return false;

            // Stepping can be performed if we are inside the debug helper or inside luaV_execute
            var instructionAddress = stepper.StartingAddress.CPUInstructionPart.InstructionPointer;

            if (instructionAddress >= processData.locations.helperStartAddress && instructionAddress < processData.locations.helperEndAddress)
                return true;

            if (instructionAddress >= processData.locations.executionStartAddress && instructionAddress < processData.locations.executionEndAddress)
                return true;

            return false;
        }

        void IDkmRuntimeStepper.Step(DkmRuntimeInstance runtimeInstance, DkmStepper stepper, DkmStepArbitrationReason reason)
        {
            var process = runtimeInstance.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(runtimeInstance.Process);

            if (stepper.StepKind == DkmStepKind.StepIntoSpecific)
                throw new NotSupportedException();

            if (processData.activeStepper != null)
            {
                processData.activeStepper.CancelStepper(processData.runtimeInstance);
                processData.activeStepper = null;
            }

            if (stepper.StepKind == DkmStepKind.Over)
            {
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepOverAddress, 1);
            }
            else if (stepper.StepKind == DkmStepKind.Into)
            {
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepOverAddress, 1);
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepIntoAddress, 1);
            }
            else if (stepper.StepKind == DkmStepKind.Out)
            {
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepOutAddress, 1);
            }

            processData.activeStepper = stepper;
        }

        void IDkmRuntimeStepper.StopStep(DkmRuntimeInstance runtimeInstance, DkmStepper stepper)
        {
            var process = runtimeInstance.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(runtimeInstance.Process);

            ClearStepperData(process, processData);
        }

        void IDkmRuntimeStepper.AfterSteppingArbitration(DkmRuntimeInstance runtimeInstance, DkmStepper stepper, DkmStepArbitrationReason reason, DkmRuntimeInstance newControllingRuntimeInstance)
        {
            // Don't have anything to do here right now
        }

        void IDkmRuntimeStepper.OnNewControllingRuntimeInstance(DkmRuntimeInstance runtimeInstance, DkmStepper stepper, DkmStepArbitrationReason reason, DkmRuntimeInstance controllingRuntimeInstance)
        {
            var process = runtimeInstance.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(runtimeInstance.Process);

            ClearStepperData(process, processData);
        }

        bool IDkmRuntimeStepper.StepControlRequested(DkmRuntimeInstance runtimeInstance, DkmStepper stepper, DkmStepArbitrationReason reason, DkmRuntimeInstance callingRuntimeInstance)
        {
            return true;
        }

        void IDkmRuntimeStepper.TakeStepControl(DkmRuntimeInstance runtimeInstance, DkmStepper stepper, bool leaveGuardsInPlace, DkmStepArbitrationReason reason, DkmRuntimeInstance callingRuntimeInstance)
        {
            var process = runtimeInstance.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(runtimeInstance.Process);

            ClearStepperData(process, processData);
        }

        void IDkmRuntimeStepper.NotifyStepComplete(DkmRuntimeInstance runtimeInstance, DkmStepper stepper)
        {
        }
    }
}
