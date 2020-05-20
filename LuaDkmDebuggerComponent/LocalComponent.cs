using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LuaDkmDebuggerComponent
{
    internal class LuaLocalProcessData : DkmDataItem
    {
        public DkmCustomRuntimeInstance runtimeInstance = null;
        public DkmCustomModuleInstance moduleInstance = null;

        public ulong scratchMemory = 0;

        public bool workingDirectoryRequested = false;
        public string workingDirectory = null;
    }

    internal class LuaFrameData
    {
        public ulong state; // Address of the Lua state, called 'L' in Lua library

        public ulong callInfo; // Address of the CallInfo struct, called 'ci' in Lua library

        public ulong function; // Address of the Proto struct, accessible as '((LClosure*)ci->func->value_.gc)->p' in Lua library
        public string functionName;

        public int instructionLine;
        public int instructionPointer; // Current instruction within the Lua Closure, evaluated as 'ci->u.l.savedpc - p->code' in Lua library (TODO: do we need to subtract 1?)

        public string source;

        public ReadOnlyCollection<byte> Encode()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(state);

                    writer.Write(callInfo);

                    writer.Write(function);
                    writer.Write(functionName);

                    writer.Write(instructionLine);
                    writer.Write(instructionPointer);

                    writer.Write(source);

                    writer.Flush();

                    return new ReadOnlyCollection<byte>(stream.ToArray());
                }
            }
        }

        public void ReadFrom(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(stream))
                {
                    state = reader.ReadUInt64();

                    callInfo = reader.ReadUInt64();

                    function = reader.ReadUInt64();
                    functionName = reader.ReadString();

                    instructionLine = reader.ReadInt32();
                    instructionPointer = reader.ReadInt32();

                    source = reader.ReadString();
                }
            }
        }
    }

    public class LocalComponent : IDkmCallStackFilter, IDkmSymbolQuery
    {
        internal string ExecuteExpression(string expression, DkmStackContext stackContext, DkmStackWalkFrame input, bool allowZero, out ulong address)
        {
            var compilerId = new DkmCompilerId(DkmVendorId.Microsoft, DkmLanguageId.Cpp);
            var language = DkmLanguage.Create("C++", compilerId);
            var languageExpression = DkmLanguageExpression.Create(language, DkmEvaluationFlags.None, expression, null);

            var inspectionContext = DkmInspectionContext.Create(stackContext.InspectionSession, input.RuntimeInstance, stackContext.Thread, 200, DkmEvaluationFlags.None, DkmFuncEvalFlags.None, 10, language, null);

            var workList = DkmWorkList.Create(null);

            string resultText = null;
            ulong resultAddress = 0;

            inspectionContext.EvaluateExpression(workList, languageExpression, input, res =>
            {
                if (res.ErrorCode == 0)
                {
                    var result = res.ResultObject as DkmSuccessEvaluationResult;

                    if (result != null && result.TagValue == DkmEvaluationResult.Tag.SuccessResult && (allowZero || result.Address.Value != 0))
                    {
                        resultText = result.Value;
                        resultAddress = result.Address.Value;
                    }

                    res.ResultObject.Close();
                }
            });

            workList.Execute();

            address = resultAddress;
            return resultText;
        }

        internal ulong? TryEvaluateAddressExpression(string expression, DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            if (ExecuteExpression(expression, stackContext, input, true, out ulong address) != null)
                return address;

            return null;
        }

        internal long? TryEvaluateNumberExpression(string expression, DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            string result = ExecuteExpression(expression, stackContext, input, true, out _);

            if (result == null)
                return null;

            if (long.TryParse(result, out long value))
                return value;

            return null;
        }

        internal string TryEvaluateStringExpression(string expression, DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            return ExecuteExpression(expression + ",sb", stackContext, input, false, out _);
        }

        DkmStackWalkFrame[] IDkmCallStackFilter.FilterNextFrame(DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            // null input frame indicates the end of the call stack
            if (input == null)
                return null;

            if (input.InstructionAddress == null)
                return new DkmStackWalkFrame[1] { input };

            if (input.InstructionAddress.ModuleInstance == null)
                return new DkmStackWalkFrame[1] { input };

            if (input.BasicSymbolInfo == null)
                return new DkmStackWalkFrame[1] { input };

            if (input.BasicSymbolInfo.MethodName == "luaV_execute")
            {
                var process = stackContext.InspectionSession.Process;

                var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

                if (processData.runtimeInstance == null)
                {
                    // Request the RemoteComponent to create the runtime and a module
                    // NOTE: Due to issues with Visual Studio debugger, runtime and module were already created as soon as application launched
                    var message = DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.createRuntime, null, null);

                    message.SendLower();

                    processData.runtimeInstance = process.GetRuntimeInstances().OfType<DkmCustomRuntimeInstance>().FirstOrDefault(el => el.Id.RuntimeType == Guids.luaRuntimeGuid);

                    if (processData.runtimeInstance == null)
                        return new DkmStackWalkFrame[1] { input };

                    processData.moduleInstance = processData.runtimeInstance.GetModuleInstances().OfType<DkmCustomModuleInstance>().FirstOrDefault(el => el.Module != null && el.Module.CompilerId.VendorId == Guids.luaCompilerGuid);

                    if (processData.moduleInstance == null)
                        return new DkmStackWalkFrame[1] { input };
                }

                if (processData.scratchMemory == 0)
                    processData.scratchMemory = process.AllocateVirtualMemory(0, 4096, 0x3000, 0x04);

                if (processData.scratchMemory == 0)
                    return new DkmStackWalkFrame[1] { input };

                if (processData.workingDirectory == null && !processData.workingDirectoryRequested)
                {
                    processData.workingDirectoryRequested = true;

                    // Jumping through hoops, kernel32.dll should be loaded
                    string callAddress = DebugHelpers.FindFunctionAddress(process.GetNativeRuntimeInstance(), "GetCurrentDirectoryA");

                    if (callAddress != null)
                    {
                        long? length = TryEvaluateNumberExpression($"((int(*)(int, char*)){callAddress})(4095, (char*){processData.scratchMemory})", stackContext, input);

                        if (length.HasValue && length.Value != 0)
                            processData.workingDirectory = TryEvaluateStringExpression($"(const char*){processData.scratchMemory}", stackContext, input);
                    }
                }

                const int luaBaseTypeMask = 0xf;
                const int luaExtendedTypeMask = 0x3f;

                const int luaTypeFunction = 6;

                const int luaTypeLuaFunction = luaTypeFunction + (0 << 4);
                const int luaTypeExternalFunction = luaTypeFunction + (1 << 4);
                const int luaTypeExternalClosure = luaTypeFunction + (2 << 4);

                int luaStringOffset = DebugHelpers.GetPointerSize(process) * 2 + 8;

                List<DkmStackWalkFrame> luaFrames = new List<DkmStackWalkFrame>();

                var luaFrameFlags = input.Flags;

                luaFrameFlags = luaFrameFlags & ~(DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.UserStatusNotDetermined);
                luaFrameFlags = luaFrameFlags | DkmStackWalkFrameFlags.InlineOptimized;

                ulong? stateAddress = TryEvaluateAddressExpression($"L", stackContext, input);
                ulong? currCallInfo = TryEvaluateAddressExpression($"L->ci", stackContext, input);

                while (stateAddress.HasValue && currCallInfo.HasValue && currCallInfo.Value != 0)
                {
                    // TODO: some (almost all) values can be read directly from memory, avoiding expression evaluation costs
                    long? frameType = TryEvaluateNumberExpression($"((CallInfo*){currCallInfo.Value})->func->tt_", stackContext, input);

                    if (frameType.HasValue && (frameType.Value & luaBaseTypeMask) == luaTypeFunction)
                    {
                        ulong? prevCallInfo = TryEvaluateAddressExpression($"((CallInfo*){currCallInfo.Value})->previous", stackContext, input);

                        // Lua is dynamic so function names are implicit, and we have a fun way of getting them

                        // Get call status
                        long? callStatus = TryEvaluateNumberExpression($"((CallInfo*){currCallInfo.Value})->callstatus", stackContext, input);

                        // Should be there, but can timeout or made an internal error
                        if (!callStatus.HasValue)
                            break;

                        // Check for finalizer
                        if ((callStatus.Value & (int)CallStatus.Finalizer) != 0)
                        {
                            luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"__gc", input.Registers, input.Annotations));

                            currCallInfo = prevCallInfo;
                            continue;
                        }

                        // Can't get function name for tail call
                        if ((callStatus.Value & (int)CallStatus.TailCall) != 0)
                        {
                            luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[name unavailable - tail call]", input.Registers, input.Annotations));

                            currCallInfo = prevCallInfo;
                            continue;
                        }

                        // Now we need to know what the previous call info used to call us
                        if (!prevCallInfo.HasValue || prevCallInfo.Value == 0)
                            break;

                        long? previousCallStatus = TryEvaluateNumberExpression($"((CallInfo*){prevCallInfo.Value})->callstatus", stackContext, input);

                        // Should be there, but can timeout or made an internal error
                        if (!previousCallStatus.HasValue)
                            break;

                        string currFunctionName = "name unavailable";

                        // Can't get function name if previous call status is not 'Lua'
                        if ((previousCallStatus.Value & (int)CallStatus.Lua) == 0)
                        {
                            currFunctionName = $"[name unavailable - not called from Lua]";
                        }
                        else
                        {
                            long? previousFrameType = TryEvaluateNumberExpression($"((CallInfo*){prevCallInfo.Value})->func->tt_", stackContext, input);

                            // Check that it's safe to cast previous call info to a Lua Closure
                            if (!previousFrameType.HasValue || (previousFrameType.Value & luaExtendedTypeMask) != luaTypeFunction)
                                break;

                            string functionNameType = TryEvaluateStringExpression($"funcnamefromcode(L, ((CallInfo*){prevCallInfo.Value}), (const char**){processData.scratchMemory})", stackContext, input);

                            if (functionNameType != null)
                            {
                                string functionName = TryEvaluateStringExpression($"*(const char**){processData.scratchMemory}", stackContext, input);

                                if (functionName != null)
                                    currFunctionName = functionName;
                            }
                        }

                        if ((frameType.Value & luaExtendedTypeMask) == luaTypeLuaFunction)
                        {
                            ulong? currProto = TryEvaluateAddressExpression($"((LClosure*)((CallInfo*){currCallInfo.Value})->func->value_.gc)->p", stackContext, input);

                            // Should be there, but can timeout or made an internal error
                            if (!currProto.HasValue)
                                break;

                            // Find instruction pointer of the _call_ to the current function
                            long? currInstructionPointer = TryEvaluateNumberExpression($"((CallInfo*){currCallInfo.Value})->u.l.savedpc - ((Proto*){currProto.Value})->code", stackContext, input);

                            // Should be there, but can timeout or made an internal error
                            if (!currInstructionPointer.HasValue)
                                break;

                            // If the call was already made, savedpc will be offset by 1 (return location)
                            int prevInstructionPointer = currInstructionPointer.Value == 0 ? 0 : (int)currInstructionPointer.Value - 1;

                            long? currLine = TryEvaluateNumberExpression($"((Proto*){currProto.Value})->lineinfo[{prevInstructionPointer}]", stackContext, input);

                            // Should be there, but can timeout or made an internal error
                            if (!currLine.HasValue)
                                break;

                            string sourceName = TryEvaluateStringExpression($"(char*)((Proto*){currProto.Value})->source + {luaStringOffset}", stackContext, input);
                            long? lineDefined = TryEvaluateNumberExpression($"((Proto*){currProto.Value})->linedefined", stackContext, input);

                            if (sourceName != null && lineDefined.HasValue)
                            {
                                if (lineDefined == 0)
                                    currFunctionName = "__global";

                                LuaFrameData frameData = new LuaFrameData();

                                frameData.state = stateAddress.Value;

                                frameData.callInfo = currCallInfo.Value;

                                frameData.function = currProto.Value;
                                frameData.functionName = currFunctionName;

                                frameData.instructionLine = (int)currLine;
                                frameData.instructionPointer = (int)currInstructionPointer.Value;

                                frameData.source = sourceName;

                                var frameDataBytes = frameData.Encode();

                                DkmInstructionAddress instructionAddress = DkmCustomInstructionAddress.Create(processData.runtimeInstance, processData.moduleInstance, frameDataBytes, (ulong)currInstructionPointer.Value, frameDataBytes, null);

                                DkmStackWalkFrame frame = DkmStackWalkFrame.Create(stackContext.Thread, instructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"{sourceName} {currFunctionName}() Line {currLine}", input.Registers, input.Annotations);

                                luaFrames.Add(frame);
                            }
                        }
                        else if ((frameType.Value & luaExtendedTypeMask) == luaTypeExternalFunction)
                        {
                            // TODO: iirc, Lua has a single call stack with external function entries, when stack filter is performed, we should break up these parts to avoid duplication
                            luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[{currFunctionName} C function]", input.Registers, input.Annotations));
                        }
                        else if ((frameType.Value & luaExtendedTypeMask) == luaTypeExternalClosure)
                        {
                            // TODO: iirc, Lua has a single call stack with external function entries, when stack filter is performed, we should break up these parts to avoid duplication
                            luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[{currFunctionName} C closure]", input.Registers, input.Annotations));
                        }

                        currCallInfo = prevCallInfo;
                    }
                    else
                    {
                        currCallInfo = null;
                    }
                }

                luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, (input.Flags & ~DkmStackWalkFrameFlags.UserStatusNotDetermined) | DkmStackWalkFrameFlags.NonuserCode, input.Description, input.Registers, input.Annotations));

                return luaFrames.ToArray();
            }

            // Mark lua functions as non-user code
            if (input.BasicSymbolInfo.MethodName.StartsWith("luaD_") || input.BasicSymbolInfo.MethodName.StartsWith("luaV_"))
            {
                return new DkmStackWalkFrame[1] { DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, (input.Flags & ~DkmStackWalkFrameFlags.UserStatusNotDetermined) | DkmStackWalkFrameFlags.NonuserCode, input.Description, input.Registers, input.Annotations) };
            }

            return new DkmStackWalkFrame[1] { input };
        }

        DkmSourcePosition IDkmSymbolQuery.GetSourcePosition(DkmInstructionSymbol instruction, DkmSourcePositionFlags flags, DkmInspectionSession inspectionSession, out bool startOfLine)
        {
            var process = inspectionSession?.Process;

            if (process == null)
            {
                DkmCustomModuleInstance moduleInstance = instruction.Module.GetModuleInstances().OfType<DkmCustomModuleInstance>().FirstOrDefault(el => el.Module.CompilerId.VendorId == Guids.luaCompilerGuid);

                if (moduleInstance == null)
                    return instruction.GetSourcePosition(flags, inspectionSession, out startOfLine);

                process = moduleInstance.Process;
            }

            var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

            var instructionSymbol = instruction as DkmCustomInstructionSymbol;

            Debug.Assert(instructionSymbol != null);

            var frameData = new LuaFrameData();

            frameData.ReadFrom(instructionSymbol.AdditionalData.ToArray());

            string filePath;

            if (frameData.source.StartsWith("@"))
            {
                if (processData.workingDirectory != null)
                    filePath = $"{processData.workingDirectory}\\{frameData.source.Substring(1)}";
                else
                    filePath = frameData.source.Substring(1);
            }
            else
            {
                // TODO: how can we display internal scripts in the debugger?
                if (processData.workingDirectory != null)
                    filePath = $"{processData.workingDirectory}\\internal.lua";
                else
                    filePath = "internal.lua";
            }

            startOfLine = true;
            return DkmSourcePosition.Create(DkmSourceFileId.Create(filePath, null, null, null), new DkmTextSpan(frameData.instructionLine, frameData.instructionLine, 0, 0));
        }

        object IDkmSymbolQuery.GetSymbolInterface(DkmModule module, Guid interfaceID)
        {
            return module.GetSymbolInterface(interfaceID);
        }
    }
}
