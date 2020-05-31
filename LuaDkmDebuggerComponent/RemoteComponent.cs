using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Stepping;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace LuaDkmDebuggerComponent
{
    public class LuaBreakpoint
    {
        // Main level - source:line
        public string source = null;
        public int line = 0;

        // Extended level - function (Proto) address, to avoid string comparisons in the line hook function
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
        public int luaVersion = 0;

        public List<LuaBreakpoint> activeBreakpoints = new List<LuaBreakpoint>();

        public bool pauseBreakpoints = false;

        public DkmStepper activeStepper = null;

        public Dictionary<ulong, LuaFunctionData> functionDataCache = new Dictionary<ulong, LuaFunctionData>();
    }

    public class RemoteComponent : IDkmProcessExecutionNotification, IDkmRuntimeInstanceLoadCompleteNotification, IDkmCustomMessageForwardReceiver, IDkmRuntimeBreakpointReceived, IDkmRuntimeMonitorBreakpointHandler, IDkmRuntimeStepper, IDkmLanguageConditionEvaluator
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
            else if (customMessage.MessageCode == MessageToRemote.luaVersionInfo)
            {
                processData.luaVersion = (customMessage.Parameter1 as int?).GetValueOrDefault(0);
            }

            return null;
        }

        void UpdateBreakpoints(DkmProcess process, LuaRemoteProcessData processData)
        {
            // Can't update breakpoints if we don't have the hook attached
            if (processData.locations == null)
                return;

            int count = processData.activeBreakpoints.Count;

            if (count > 256)
                count = 256;

            ulong pointerSize = (ulong)DebugHelpers.GetPointerSize(process);

            ulong sourceNameAddress = processData.locations.helperBreakSourcesAddress;

            for (int i = 0; i < count; i++)
            {
                ulong dataAddress = processData.locations.helperBreakDataAddress + (ulong)i * 3 * pointerSize;

                var breakpoint = processData.activeBreakpoints[i];

                DebugHelpers.TryWritePointerVariable(process, dataAddress, (ulong)breakpoint.line);

                if (breakpoint.functionAddress == 0)
                {
                    Debug.Assert(breakpoint.source != null);

                    byte[] sourceNameBytes = Encoding.UTF8.GetBytes(breakpoint.source);

                    DebugHelpers.TryWriteRawBytes(process, sourceNameAddress, sourceNameBytes);
                    DebugHelpers.TryWriteByteVariable(process, sourceNameAddress + (ulong)sourceNameBytes.Length, 0);

                    ulong currSourceNameAddress = sourceNameAddress;
                    sourceNameAddress += (ulong)sourceNameBytes.Length + 1;

                    DebugHelpers.TryWritePointerVariable(process, dataAddress + pointerSize, 0);
                    DebugHelpers.TryWritePointerVariable(process, dataAddress + pointerSize * 2, currSourceNameAddress);
                }
                else
                {
                    DebugHelpers.TryWritePointerVariable(process, dataAddress + pointerSize, breakpoint.functionAddress);
                    DebugHelpers.TryWritePointerVariable(process, dataAddress + pointerSize * 2, 0);
                }
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

                        var breakpointPos = DebugHelpers.ReadUintVariable(process, processData.locations.helperBreakHitIdAddress);

                        if (!breakpointPos.HasValue)
                            return;

                        if (breakpointPos.Value < processData.activeBreakpoints.Count)
                        {
                            try
                            {
                                var breakpoint = processData.activeBreakpoints[(int)breakpointPos.Value];

                                if (breakpoint.runtimeBreakpoint != null)
                                    breakpoint.runtimeBreakpoint.OnHit(thread, false);
                            }
                            catch (System.ObjectDisposedException)
                            {
                                // Breakpoint was implicitly closed
                                processData.activeBreakpoints.RemoveAt((int)breakpointPos.Value);
                            }
                            catch (DkmException)
                            {
                                // In case another component evaluates a function
                            }
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

                    var breakpoint = new LuaBreakpoint
                    {
                        source = entityData.source,
                        line = entityData.line,

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

                    if (additionalData.line == 0)
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

                    processData.activeBreakpoints.RemoveAll(el => el.line == entityData.line && el.source == entityData.source && el.functionAddress == entityData.functionAddress);
                    UpdateBreakpoints(process, processData);
                }
            }
        }

        void ClearStepperData(DkmProcess process, LuaRemoteProcessData processData)
        {
            if (processData.locations != null)
            {
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepOverAddress, 0);
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepIntoAddress, 0);
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStepOutAddress, 0);
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperSkipDepthAddress, 0);
            }

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

            if (processData.locations == null)
                return false;

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

        void IDkmLanguageConditionEvaluator.ParseCondition(DkmEvaluationBreakpointCondition evaluationCondition, out string errorText)
        {
            // This place could be used to typecheck of pre-compile the expression
            errorText = null;
        }

        void IDkmLanguageConditionEvaluator.EvaluateCondition(DkmEvaluationBreakpointCondition evaluationCondition, DkmStackWalkFrame stackFrame, out bool stop, out string errorText)
        {
            DkmProcess process = stackFrame.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(process);

            if (processData.locations == null)
            {
                stop = true;
                errorText = "Debug helper data for conditional breakpoint is missing";
                return;
            }

            DkmInspectionSession inspectionSession = DkmInspectionSession.Create(process, null);

            ulong stateAddress = DebugHelpers.ReadPointerVariable(process, processData.locations.helperBreakHitLuaStateAddress).GetValueOrDefault(0);

            if (stateAddress == 0)
            {
                inspectionSession.Close();

                stop = true;
                errorText = "Failed to evaluate current Lua state address";
                return;
            }

            ulong callInfoAddress = 0;

            // Read lua_State
            ulong temp = stateAddress;

            // CommonHeader
            DebugHelpers.SkipStructPointer(process, ref temp);
            DebugHelpers.SkipStructByte(process, ref temp);
            DebugHelpers.SkipStructByte(process, ref temp);

            if (processData.luaVersion == 501)
            {
                DebugHelpers.SkipStructByte(process, ref temp); // status
                DebugHelpers.SkipStructPointer(process, ref temp); // top
                DebugHelpers.SkipStructPointer(process, ref temp); // base
                DebugHelpers.SkipStructPointer(process, ref temp); // l_G
                callInfoAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);
            }
            else if (processData.luaVersion == 502)
            {
                DebugHelpers.SkipStructByte(process, ref temp); // status
                DebugHelpers.SkipStructPointer(process, ref temp); // top
                DebugHelpers.SkipStructPointer(process, ref temp); // l_G
                callInfoAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);

            }
            else if (processData.luaVersion == 503)
            {
                DebugHelpers.SkipStructShort(process, ref temp); // nci
                DebugHelpers.SkipStructByte(process, ref temp); // status
                DebugHelpers.SkipStructPointer(process, ref temp); // top
                DebugHelpers.SkipStructPointer(process, ref temp); // l_G
                callInfoAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);
            }

            if (callInfoAddress == 0)
            {
                inspectionSession.Close();

                stop = true;
                errorText = $"Failed to evaluate current Lua call frame (Lua version {processData.luaVersion})";
                return;
            }

            // Load call info data (to get base stack address)
            LuaFunctionCallInfoData callInfoData = new LuaFunctionCallInfoData();

            callInfoData.ReadFrom(process, callInfoAddress); // TODO: cache?

            callInfoData.ReadFunction(process);

            if (callInfoData.func.extendedType != LuaExtendedType.LuaFunction)
            {
                inspectionSession.Close();

                stop = true;
                errorText = "Breakpoint location has to be inside a Lua function";
                return;
            }

            LuaValueDataLuaFunction currCallLuaFunction = callInfoData.func as LuaValueDataLuaFunction;

            LuaClosureData closureData = currCallLuaFunction.value;

            LuaFunctionData functionData = null;

            if (processData.functionDataCache.ContainsKey(closureData.functionAddress))
            {
                functionData = processData.functionDataCache[closureData.functionAddress];
            }
            else
            {
                functionData = closureData.ReadFunction(process);

                functionData.ReadUpvalues(process);
                functionData.ReadLocals(process, -1);

                processData.functionDataCache.Add(closureData.functionAddress, functionData);
            }

            // Possible in bad break locations
            if (callInfoData.savedInstructionPointerAddress < functionData.codeDataAddress)
            {
                inspectionSession.Close();

                stop = true;
                errorText = "Invalid saved program counter";
                return;
            }

            long currInstructionPointer = ((long)callInfoData.savedInstructionPointerAddress - (long)functionData.codeDataAddress) / 4; // unsigned size instructions

            // If the call was already made, savedpc will be offset by 1 (return location)
            int prevInstructionPointer = currInstructionPointer == 0 ? 0 : (int)currInstructionPointer - 1;

            functionData.UpdateLocals(process, prevInstructionPointer);

            ExpressionEvaluation evaluation = new ExpressionEvaluation(process, functionData, callInfoData.stackBaseAddress, closureData);

            var result = evaluation.Evaluate(evaluationCondition.Source.Text);

            if (result as LuaValueDataError != null)
            {
                var resultAsError = result as LuaValueDataError;

                inspectionSession.Close();

                stop = true;
                errorText = resultAsError.value;
                return;
            }

            if (result.baseType == LuaBaseType.Nil)
            {
                inspectionSession.Close();

                stop = false;
                errorText = null;
                return;
            }

            if (result is LuaValueDataBool resultBool)
            {
                inspectionSession.Close();

                stop = resultBool.value;
                errorText = null;
                return;
            }

            var resultNumber = result as LuaValueDataNumber;

            if (resultNumber == null)
            {
                inspectionSession.Close();

                stop = true;
                errorText = $"Value can't be used as condition: {result.AsSimpleDisplayString(10)}";
                return;
            }

            inspectionSession.Close();

            stop = resultNumber.value != 0.0;
            errorText = null;
        }
    }
}
