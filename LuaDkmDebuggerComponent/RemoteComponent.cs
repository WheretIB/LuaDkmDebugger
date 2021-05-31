using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Exceptions;
using Microsoft.VisualStudio.Debugger.Stepping;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        public Dictionary<ulong, RegisterStateMessage> knownStates = new Dictionary<ulong, RegisterStateMessage>();
        public bool hooksEnabled = false;
        public bool hadActiveStepper = false;
    }

    public class RemoteComponent : IDkmCustomMessageForwardReceiver, IDkmRuntimeBreakpointReceived, IDkmRuntimeMonitorBreakpointHandler, IDkmRuntimeStepper, IDkmLanguageConditionEvaluator, IDkmExceptionFormatter
    {
        DkmCustomMessage IDkmCustomMessageForwardReceiver.SendLower(DkmCustomMessage customMessage)
        {
            var process = customMessage.Process;
            var processData = DebugHelpers.GetOrCreateDataItem<LuaRemoteProcessData>(process);

            if (customMessage.MessageCode == MessageToRemote.createRuntime)
            {
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

                if (processData.moduleInstance == null)
                {
                    DkmDynamicSymbolFileId symbolFileId = DkmDynamicSymbolFileId.Create(Guids.luaSymbolProviderGuid);

                    processData.moduleInstance = DkmCustomModuleInstance.Create("lua_vm", "lua.vm.code", 0, processData.runtimeInstance, null, symbolFileId, DkmModuleFlags.None, DkmModuleMemoryLayout.Unknown, 0, 1, 0, "Lua vm code", false, null, null, null);

                    processData.moduleInstance.SetModule(processData.module, true); // Can use reload?
                }
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

                LuaHelpers.luaVersion = processData.luaVersion;
            }
            else if (customMessage.MessageCode == MessageToRemote.registerLuaState)
            {
                var data = new RegisterStateMessage();

                data.ReadFrom(customMessage.Parameter1 as byte[]);

                Debug.Assert(processData.knownStates.ContainsKey(data.stateAddress) == false);

                if (processData.knownStates.ContainsKey(data.stateAddress))
                {
                    Debug.WriteLine("IDkmCustomMessageForwardReceiver.SendLower() Duplicate Lua state registration, destruction was probably missed!");
                }
                else
                {
                    processData.knownStates.Add(data.stateAddress, data);

                    if (processData.hooksEnabled)
                        SetupHooks(process, processData);
                }
            }
            else if (customMessage.MessageCode == MessageToRemote.unregisterLuaState)
            {
                var data = new UnregisterStateMessage();

                data.ReadFrom(customMessage.Parameter1 as byte[]);

                // Registration is called only for states that can be hooked, unregistration is always called
                if (processData.knownStates.ContainsKey(data.stateAddress))
                    processData.knownStates.Remove(data.stateAddress);
            }

            return null;
        }

        void SetupHooks(DkmProcess process, LuaRemoteProcessData processData)
        {
            processData.hooksEnabled = true;

            foreach (var stateKV in processData.knownStates)
            {
                var state = stateKV.Value;

                DebugHelpers.TryWritePointerVariable(process, state.hookFunctionAddress, state.helperHookFunctionAddress);

                if (processData.luaVersion == 503 || processData.luaVersion == 504)
                    DebugHelpers.TryWriteIntVariable(process, state.hookMaskAddress, 7); // LUA_HOOKLINE | LUA_HOOKCALL | LUA_HOOKRET
                else
                    DebugHelpers.TryWriteByteVariable(process, state.hookMaskAddress, 7); // LUA_HOOKLINE | LUA_HOOKCALL | LUA_HOOKRET

                DebugHelpers.TryWriteIntVariable(process, state.hookBaseCountAddress, 0);
                DebugHelpers.TryWriteIntVariable(process, state.hookCountAddress, 0);

                // Lua 5.4 has to update 'trap' flag for all Lua call stack frames
                if (processData.luaVersion == 504)
                {
                    ulong? callInfo = DebugHelpers.ReadPointerVariable(process, state.stateAddress + state.setTrapStateCallInfoOffset);

                    while (callInfo.HasValue && callInfo.Value != 0)
                    {
                        var callStatus = DebugHelpers.ReadShortVariable(process, callInfo.Value + state.setTrapCallInfoCallStatusOffset);

                        if (callStatus.HasValue && (callStatus.Value & (int)CallStatus_5_4.C) == 0)
                        {
                            if (!DebugHelpers.TryWriteIntVariable(process, callInfo.Value + state.setTrapCallInfoTrapOffset, 1))
                                break;
                        }

                        callInfo = DebugHelpers.ReadPointerVariable(process, callInfo.Value + state.setTrapCallInfoPreviousOffset);
                    }
                }
            }

            // TODO: only for luajit
            // Trigger a custom breakpoint
            DebugHelpers.TryWriteUintVariable(process, processData.locations.helperAsyncBreakCodeAddress, 1u);
        }

        void RemoveHooks(DkmProcess process, LuaRemoteProcessData processData)
        {
            processData.hooksEnabled = false;

            foreach (var stateKV in processData.knownStates)
            {
                var state = stateKV.Value;

                DebugHelpers.TryWritePointerVariable(process, state.hookFunctionAddress, 0);

                if (processData.luaVersion == 503 || processData.luaVersion == 504)
                    DebugHelpers.TryWriteIntVariable(process, state.hookMaskAddress, 0);
                else
                    DebugHelpers.TryWriteByteVariable(process, state.hookMaskAddress, 0);

                DebugHelpers.TryWriteIntVariable(process, state.hookBaseCountAddress, 0);
                DebugHelpers.TryWriteIntVariable(process, state.hookCountAddress, 0);
            }
        }

        void UpdateHooks(DkmProcess process, LuaRemoteProcessData processData)
        {
            if (processData.activeBreakpoints.Count != 0 || processData.hadActiveStepper)
            {
                if (!processData.hooksEnabled)
                    SetupHooks(process, processData);
            }
            else
            {
                if (processData.hooksEnabled)
                    RemoveHooks(process, processData);
            }
        }

        void UpdateBreakpoints(DkmProcess process, LuaRemoteProcessData processData)
        {
            // Can't update breakpoints if we don't have the hook attached
            if (processData.locations == null)
                return;

            UpdateHooks(process, processData);

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
                    // Breakpoint can get hit again after expression evaluation 'slips' the thread
                    if (processData.pauseBreakpoints)
                        return;

                    if (runtimeBreakpoint.UniqueId == processData.locations.breakpointLuaHelperBreakpointHit)
                    {
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

                var response = message.SendHigher();

                if (response?.MessageCode == MessageToRemote.throwException)
                {
                    if (runtimeBreakpoint is DkmRuntimeInstructionBreakpoint runtimeInstructionBreakpoint)
                    {
                        var exceptionInformation = DkmCustomExceptionInformation.Create(processData.runtimeInstance, Guids.luaExceptionGuid, thread, runtimeInstructionBreakpoint.InstructionAddress, "LuaError", 0, DkmExceptionProcessingStage.Thrown | DkmExceptionProcessingStage.UserVisible | DkmExceptionProcessingStage.Unhandled, null, new ReadOnlyCollection<byte>(response.Parameter1 as byte[]));

                        exceptionInformation.OnDebugMonitorException();
                    }
                }
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
                DebugHelpers.TryWriteIntVariable(process, processData.locations.helperStackDepthAtCallAddress, 0);
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

            processData.hadActiveStepper = true;

            UpdateHooks(process, processData);
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

            ulong? savedProgramCounterAddress = null;

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
                savedProgramCounterAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);
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
            else if (processData.luaVersion == 504)
            {
                DebugHelpers.SkipStructByte(process, ref temp); // status
                DebugHelpers.SkipStructByte(process, ref temp); // allowhook
                DebugHelpers.SkipStructShort(process, ref temp); // nci
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

            if (callInfoData.func == null)
            {
                inspectionSession.Close();

                stop = true;
                errorText = $"Failed to evaluate current Lua call frame function (Lua version {processData.luaVersion})";
                return;
            }

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

            if (!savedProgramCounterAddress.HasValue)
                savedProgramCounterAddress = callInfoData.savedInstructionPointerAddress;

            // Possible in bad break locations
            if (savedProgramCounterAddress < functionData.codeDataAddress)
            {
                inspectionSession.Close();

                stop = true;
                errorText = "Invalid saved program counter";
                return;
            }

            long currInstructionPointer = ((long)savedProgramCounterAddress - (long)functionData.codeDataAddress) / 4; // unsigned size instructions

            // If the call was already made, savedpc will be offset by 1 (return location)
            int prevInstructionPointer = currInstructionPointer == 0 ? 0 : (int)currInstructionPointer - 1;

            functionData.UpdateLocals(process, prevInstructionPointer);

            ExpressionEvaluation evaluation = new ExpressionEvaluation(process, null, null, functionData, callInfoData.stackBaseAddress, closureData);

            var result = evaluation.Evaluate(evaluationCondition.Source.Text, false);

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

        string IDkmExceptionFormatter.GetDescription(DkmExceptionInformation exception)
        {
            return exception.Name;
        }

        string IDkmExceptionFormatter.GetAdditionalInformation(DkmExceptionInformation exception)
        {
            var customExceptionInformation = exception as DkmCustomExceptionInformation;

            if (customExceptionInformation?.AdditionalInformation == null)
                return null;

            string message = Encoding.UTF8.GetString(customExceptionInformation.AdditionalInformation.ToArray());

            return message;
        }
    }
}
