using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    internal class LuaFrameLocalsEnumData : DkmDataItem
    {
        public LuaFrameData frameData;
        public LuaFunctionCallInfoData callInfo;
        public LuaFunctionData function;
    }

    internal class LuaEvaluationDataItem : DkmDataItem
    {
        public ulong address;
        public string type;
        public string fullName;
        public LuaValueDataBase luaValueData;
    }

    public class LocalComponent : IDkmCallStackFilter, IDkmSymbolQuery, IDkmLanguageExpressionEvaluator
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

                // TODO: Replace with enumerations from Bytecode.cs
                const int luaBaseTypeMask = 0xf;
                const int luaExtendedTypeMask = 0x3f;

                const int luaTypeFunction = 6;

                const int luaTypeLuaFunction = luaTypeFunction + (0 << 4);
                const int luaTypeExternalFunction = luaTypeFunction + (1 << 4);
                const int luaTypeExternalClosure = luaTypeFunction + (2 << 4);

                List<DkmStackWalkFrame> luaFrames = new List<DkmStackWalkFrame>();

                var luaFrameFlags = input.Flags;

                luaFrameFlags = luaFrameFlags & ~(DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.UserStatusNotDetermined);
                luaFrameFlags = luaFrameFlags | DkmStackWalkFrameFlags.InlineOptimized;

                ulong? stateAddress = TryEvaluateAddressExpression($"L", stackContext, input);

                ulong? registryAddress = TryEvaluateAddressExpression($"&L->l_G->l_registry", stackContext, input);
                long? version = TryEvaluateNumberExpression($"(int)*L->l_G->version", stackContext, input);

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

                            string sourceName = TryEvaluateStringExpression($"(char*)((Proto*){currProto.Value})->source + {LuaHelpers.GetStringDataOffset(process)}", stackContext, input);
                            long? lineDefined = TryEvaluateNumberExpression($"((Proto*){currProto.Value})->linedefined", stackContext, input);

                            if (sourceName != null && lineDefined.HasValue)
                            {
                                if (lineDefined == 0)
                                    currFunctionName = "__global";

                                LuaFunctionData functionData = new LuaFunctionData();
                                functionData.ReadFrom(process, currProto.Value);

                                string argumentList = "";

                                for (int i = 0; i < functionData.argumentCount; i++)
                                {
                                    LuaLocalVariableData argument = new LuaLocalVariableData();

                                    argument.ReadFrom(process, functionData.localVariableDataAddress + (ulong)(i * LuaLocalVariableData.StructSize(process)));

                                    argumentList += (i == 0 ? "" : ", ") + argument.name;
                                }

                                LuaFrameData frameData = new LuaFrameData();

                                frameData.state = stateAddress.Value;

                                frameData.registryAddress = registryAddress.GetValueOrDefault(0);
                                frameData.version = version.GetValueOrDefault(503);

                                frameData.callInfo = currCallInfo.Value;

                                frameData.functionAddress = currProto.Value;
                                frameData.functionName = currFunctionName;

                                frameData.instructionLine = (int)currLine;
                                frameData.instructionPointer = (int)currInstructionPointer.Value;

                                frameData.source = sourceName;

                                var frameDataBytes = frameData.Encode();

                                DkmInstructionAddress instructionAddress = DkmCustomInstructionAddress.Create(processData.runtimeInstance, processData.moduleInstance, frameDataBytes, (ulong)currInstructionPointer.Value, frameDataBytes, null);

                                var description = $"{sourceName} {currFunctionName}({argumentList}) Line {currLine}";

                                DkmStackWalkFrame frame = DkmStackWalkFrame.Create(stackContext.Thread, instructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, description, input.Registers, input.Annotations);

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

        string EvaluateValueAtLuaValue(DkmProcess process, LuaValueDataBase valueBase, uint radix, out string editableValue, ref DkmEvaluationResultFlags flags, out DkmDataAddress dataAddress, out string type)
        {
            editableValue = null;
            dataAddress = null;
            type = "unknown";

            if (valueBase == null)
                return null;

            if (valueBase as LuaValueDataNil != null)
            {
                type = "nil";
                return "nil";
            }

            if (valueBase as LuaValueDataBool != null)
            {
                var value = valueBase as LuaValueDataBool;

                type = "bool";

                if (value.value)
                {
                    flags |= DkmEvaluationResultFlags.Boolean | DkmEvaluationResultFlags.BooleanTrue;
                    editableValue = $"{value.value}";
                    return "true";
                }
                else
                {
                    flags |= DkmEvaluationResultFlags.Boolean;
                    editableValue = $"{value.value}";
                    return "false";
                }
            }

            if (valueBase as LuaValueDataLightUserData != null)
            {
                var value = valueBase as LuaValueDataLightUserData;

                type = "user_data";

                flags |= DkmEvaluationResultFlags.IsBuiltInType;
                editableValue = $"{value.value}";
                return $"0x{value.value:x}";
            }

            if (valueBase as LuaValueDataDouble != null)
            {
                var value = valueBase as LuaValueDataDouble;

                type = "double";

                flags |= DkmEvaluationResultFlags.IsBuiltInType;
                editableValue = $"{value.value}";
                return $"{value.value}";
            }

            if (valueBase as LuaValueDataInt != null)
            {
                var value = valueBase as LuaValueDataInt;

                type = "int";

                flags |= DkmEvaluationResultFlags.IsBuiltInType;
                editableValue = $"{value.value}";

                if (radix == 16)
                    return $"0x{value.value:x}";

                return $"{value.value}";
            }

            if (valueBase as LuaValueDataString != null)
            {
                var value = valueBase as LuaValueDataString;

                type = value.extendedType == LuaExtendedType.ShortString ? "short_string" : "long_string";

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.RawString | DkmEvaluationResultFlags.ReadOnly;
                dataAddress = DkmDataAddress.Create(process.GetNativeRuntimeInstance(), value.targetAddress, null);
                return $"0x{value.targetAddress:x} \"{value.value}\"";
            }

            if (valueBase as LuaValueDataTable != null)
            {
                var value = valueBase as LuaValueDataTable;

                type = "table";

                value.value.LoadValues(process);
                value.value.LoadMetaTable(process);

                flags |= DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable;

                if (value.value.arrayElements.Count != 0 && value.value.nodeElements.Count != 0)
                    return $"table [{value.value.arrayElements.Count} elements and {value.value.nodeElements.Count} keys]";

                if (value.value.arrayElements.Count != 0)
                    return $"table [{value.value.arrayElements.Count} elements]";

                if (value.value.nodeElements.Count != 0)
                    return $"table [{value.value.nodeElements.Count} keys]";

                if (value.value.metaTable == null)
                {
                    flags &= ~DkmEvaluationResultFlags.Expandable;

                    return "table [empty]";
                }

                return "table";
            }

            if (valueBase as LuaValueDataLuaFunction != null)
            {
                var value = valueBase as LuaValueDataLuaFunction;

                type = "lua_function";

                flags |= DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress}";
            }

            if (valueBase as LuaValueDataExternalFunction != null)
            {
                var value = valueBase as LuaValueDataExternalFunction;

                type = "c_function";

                flags |= DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress}";
            }

            if (valueBase as LuaValueDataExternalClosure != null)
            {
                var value = valueBase as LuaValueDataExternalClosure;

                type = "c_closure";

                flags |= DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress}";
            }

            if (valueBase as LuaValueDataUserData != null)
            {
                var value = valueBase as LuaValueDataUserData;

                type = "user_data";

                flags |= DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress}";
            }

            if (valueBase as LuaValueDataThread != null)
            {
                var value = valueBase as LuaValueDataThread;

                type = "thread";

                flags |= DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress}";
            }

            return null;
        }

        string EvaluateValueAtAddress(DkmProcess process, ulong address, uint radix, out string editableValue, ref DkmEvaluationResultFlags flags, out DkmDataAddress dataAddress, out string type, out LuaValueDataBase luaValueData)
        {
            editableValue = null;
            dataAddress = null;
            type = "unknown";

            luaValueData = LuaHelpers.ReadValue(process, address);

            if (luaValueData == null)
                return null;

            return EvaluateValueAtLuaValue(process, luaValueData, radix, out editableValue, ref flags, out dataAddress, out type);
        }

        DkmEvaluationResult EvaluateDataAtAddress(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string name, string fullName, ulong address, DkmEvaluationResultFlags flags, DkmEvaluationResultAccessType access, DkmEvaluationResultStorageType storage)
        {
            var process = stackFrame.Process;

            if (address == 0)
                return DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, name, fullName, "Null pointer access", DkmEvaluationResultFlags.Invalid, null);

            string value = EvaluateValueAtAddress(process, address, inspectionContext.Radix, out string editableValue, ref flags, out DkmDataAddress dataAddress, out string type, out LuaValueDataBase luaValueData);

            if (value == null)
                return DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, name, fullName, "Failed to read value", DkmEvaluationResultFlags.Invalid, null);

            DkmEvaluationResultCategory category = DkmEvaluationResultCategory.Data;
            DkmEvaluationResultTypeModifierFlags typeModifiers = DkmEvaluationResultTypeModifierFlags.None;

            var dataItem = new LuaEvaluationDataItem
            {
                address = address,
                type = type,
                fullName = fullName,
                luaValueData = luaValueData
            };

            return DkmSuccessEvaluationResult.Create(inspectionContext, stackFrame, name, fullName, flags, value, editableValue, type, category, access, storage, typeModifiers, dataAddress, null, null, dataItem);
        }

        DkmEvaluationResult EvaluateDataAtLuaValue(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string name, string fullName, LuaValueDataBase luaValue, DkmEvaluationResultFlags flags, DkmEvaluationResultAccessType access, DkmEvaluationResultStorageType storage)
        {
            var process = stackFrame.Process;

            if (luaValue == null)
                return DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, name, fullName, "Null pointer access", DkmEvaluationResultFlags.Invalid, null);

            string value = EvaluateValueAtLuaValue(process, luaValue, inspectionContext.Radix, out string editableValue, ref flags, out DkmDataAddress dataAddress, out string type);

            if (value == null)
                return DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, name, fullName, "Failed to read value", DkmEvaluationResultFlags.Invalid, null);

            DkmEvaluationResultCategory category = DkmEvaluationResultCategory.Data;
            DkmEvaluationResultTypeModifierFlags typeModifiers = DkmEvaluationResultTypeModifierFlags.None;

            var dataItem = new LuaEvaluationDataItem
            {
                address = luaValue.originalAddress,
                type = type,
                fullName = fullName,
                luaValueData = luaValue
            };

            return DkmSuccessEvaluationResult.Create(inspectionContext, stackFrame, name, fullName, flags, value, editableValue, type, category, access, storage, typeModifiers, dataAddress, null, null, dataItem);
        }

        void IDkmLanguageExpressionEvaluator.EvaluateExpression(DkmInspectionContext inspectionContext, DkmWorkList workList, DkmLanguageExpression expression, DkmStackWalkFrame stackFrame, DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine)
        {
            completionRoutine(new DkmEvaluateExpressionAsyncResult(DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, expression.Text, expression.Text, "Not implemented", DkmEvaluationResultFlags.Invalid, null)));
        }

        DkmEvaluationResult GetTableChildAtIndex(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string fullName, LuaValueDataTable value, int index)
        {
            var process = stackFrame.Process;

            if (index < value.value.arrayElements.Count)
            {
                var element = value.value.arrayElements[index];

                return EvaluateDataAtLuaValue(inspectionContext, stackFrame, $"[{index}]", $"{fullName}[{index}]", element, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
            }

            index = index - value.value.arrayElements.Count;

            if (index < value.value.nodeElements.Count)
            {
                var node = value.value.nodeElements[index];

                DkmEvaluationResultFlags flags = DkmEvaluationResultFlags.None;
                string name = EvaluateValueAtLuaValue(process, node.key, 10, out _, ref flags, out _, out _);

                var keyString = node.key as LuaValueDataString;

                if (keyString != null)
                    name = keyString.value;

                if (name == null)
                    name = "%error-name%";

                return EvaluateDataAtLuaValue(inspectionContext, stackFrame, name, $"{fullName}.{name}", node.value, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
            }

            index = index - value.value.nodeElements.Count;

            if (index == 0)
            {
                var metaTableValue = new LuaValueDataTable
                {
                    baseType = LuaBaseType.Table,
                    extendedType = LuaExtendedType.Table,
                    originalAddress = 0, // Not available as TValue
                    value = value.value.metaTable,
                    targetAddress = value.value.metaTableDataAddress
                };

                return EvaluateDataAtLuaValue(inspectionContext, stackFrame, "!metatable", $"{fullName}.!metatable", metaTableValue, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
            }

            Debug.Assert(false, "Invalid child index");

            return null;
        }

        void IDkmLanguageExpressionEvaluator.GetChildren(DkmEvaluationResult result, DkmWorkList workList, int initialRequestSize, DkmInspectionContext inspectionContext, DkmCompletionRoutine<DkmGetChildrenAsyncResult> completionRoutine)
        {
            var process = result.StackFrame.Process;

            var evalData = result.GetDataItem<LuaEvaluationDataItem>();

            // Shouldn't happen
            if (evalData == null)
            {
                completionRoutine(new DkmGetChildrenAsyncResult(new DkmEvaluationResult[0], DkmEvaluationResultEnumContext.Create(0, result.StackFrame, inspectionContext, null)));
                return;
            }

            if (evalData.luaValueData as LuaValueDataTable != null)
            {
                var value = evalData.luaValueData as LuaValueDataTable;

                value.value.LoadValues(process);
                value.value.LoadMetaTable(process);

                int actualSize = value.value.arrayElements.Count + value.value.nodeElements.Count;

                if (value.value.metaTable != null)
                    actualSize += 1;

                int finalInitialSize = initialRequestSize < actualSize ? initialRequestSize : actualSize;

                DkmEvaluationResult[] initialResults = new DkmEvaluationResult[finalInitialSize];

                for (int i = 0; i < initialResults.Length; i++)
                    initialResults[i] = GetTableChildAtIndex(inspectionContext, result.StackFrame, result.FullName, value, i);

                var enumerator = DkmEvaluationResultEnumContext.Create(actualSize, result.StackFrame, inspectionContext, evalData);

                completionRoutine(new DkmGetChildrenAsyncResult(initialResults, enumerator));
                return;
            }

            // Shouldn't happen
            completionRoutine(new DkmGetChildrenAsyncResult(new DkmEvaluationResult[0], DkmEvaluationResultEnumContext.Create(0, result.StackFrame, inspectionContext, null)));
        }

        void IDkmLanguageExpressionEvaluator.GetFrameArguments(DkmInspectionContext inspectionContext, DkmWorkList workList, DkmStackWalkFrame frame, DkmCompletionRoutine<DkmGetFrameArgumentsAsyncResult> completionRoutine)
        {
            completionRoutine(new DkmGetFrameArgumentsAsyncResult(new DkmEvaluationResult[0]));
        }

        void IDkmLanguageExpressionEvaluator.GetFrameLocals(DkmInspectionContext inspectionContext, DkmWorkList workList, DkmStackWalkFrame stackFrame, DkmCompletionRoutine<DkmGetFrameLocalsAsyncResult> completionRoutine)
        {
            var process = stackFrame.Process;

            // Load frame data from instruction
            var instructionAddress = stackFrame.InstructionAddress as DkmCustomInstructionAddress;

            Debug.Assert(instructionAddress != null);

            var frameData = new LuaFrameData();

            frameData.ReadFrom(instructionAddress.AdditionalData.ToArray());

            // Load call info data
            LuaFunctionCallInfoData callInfoData = new LuaFunctionCallInfoData();

            callInfoData.ReadFrom(process, frameData.callInfo); // TODO: cache?

            // Load function data
            LuaFunctionData functionData = new LuaFunctionData();

            functionData.ReadFrom(process, frameData.functionAddress); // TODO: cache?

            var frameLocalsEnumData = new LuaFrameLocalsEnumData
            {
                frameData = frameData,
                callInfo = callInfoData,
                function = functionData
            };

            functionData.ReadLocals(process, frameData.instructionPointer);

            int count = 1 + functionData.activeLocals.Count; // 1 pseudo variable for '[registry]' table

            completionRoutine(new DkmGetFrameLocalsAsyncResult(DkmEvaluationResultEnumContext.Create(functionData.localVariableSize, stackFrame, inspectionContext, frameLocalsEnumData)));
        }

        void IDkmLanguageExpressionEvaluator.GetItems(DkmEvaluationResultEnumContext enumContext, DkmWorkList workList, int startIndex, int count, DkmCompletionRoutine<DkmEvaluationEnumAsyncResult> completionRoutine)
        {
            var process = enumContext.StackFrame.Process;

            var frameLocalsEnumData = enumContext.GetDataItem<LuaFrameLocalsEnumData>();

            if (frameLocalsEnumData != null)
            {
                frameLocalsEnumData.function.ReadLocals(process, frameLocalsEnumData.frameData.instructionPointer);

                // Visual Studio doesn't respect enumeration size for GetFrameLocals, so we need to limit it back
                var actualCount = 1 + frameLocalsEnumData.function.activeLocals.Count;

                int finalCount = actualCount - startIndex;

                finalCount = finalCount < 0 ? 0 : (finalCount < count ? finalCount : count);

                var results = new DkmEvaluationResult[finalCount];

                for (int i = startIndex; i < startIndex + finalCount; i++)
                {
                    if (i == 0)
                    {
                        ulong address = frameLocalsEnumData.frameData.registryAddress;

                        string name = "[registry]";

                        results[i - startIndex] = EvaluateDataAtAddress(enumContext.InspectionContext, enumContext.StackFrame, name, name, address, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
                    }
                    else
                    {
                        int index = i - 1;

                        // Base stack contains arguments and locals that are live at the current instruction
                        ulong address = frameLocalsEnumData.callInfo.stackBaseAddress + (ulong)index * LuaHelpers.GetValueSize(process);

                        string name = frameLocalsEnumData.function.activeLocals[index].name;

                        results[i - startIndex] = EvaluateDataAtAddress(enumContext.InspectionContext, enumContext.StackFrame, name, name, address, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
                    }
                }

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));
            }

            var evalData = enumContext.GetDataItem<LuaEvaluationDataItem>();

            // Shouldn't happen
            if (evalData == null)
            {
                completionRoutine(new DkmEvaluationEnumAsyncResult(new DkmEvaluationResult[0]));
                return;
            }

            if (evalData.luaValueData as LuaValueDataTable != null)
            {
                var value = evalData.luaValueData as LuaValueDataTable;

                value.value.LoadValues(process);
                value.value.LoadMetaTable(process);

                var results = new DkmEvaluationResult[count];

                for (int i = startIndex; i < startIndex + count; i++)
                    results[i - startIndex] = GetTableChildAtIndex(enumContext.InspectionContext, enumContext.StackFrame, evalData.fullName, value, i);

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));
                return;
            }

            completionRoutine(new DkmEvaluationEnumAsyncResult(new DkmEvaluationResult[0]));
        }

        string IDkmLanguageExpressionEvaluator.GetUnderlyingString(DkmEvaluationResult result)
        {
            var process = result.StackFrame.Process;

            var success = result as DkmSuccessEvaluationResult;

            if (success == null)
                return "Failed to evaluate";

            var target = DebugHelpers.ReadStringVariable(process, success.Address.Value, 32 * 1024);

            if (target != null)
                return target;

            return "Failed to read data";
        }

        void IDkmLanguageExpressionEvaluator.SetValueAsString(DkmEvaluationResult result, string value, int timeout, out string errorText)
        {
            errorText = "Missing evaluation data";
        }
    }
}
