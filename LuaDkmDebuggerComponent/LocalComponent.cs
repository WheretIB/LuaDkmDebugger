using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace LuaDkmDebuggerComponent
{
    internal class LuaDebugConfiguration
    {
        public List<string> ScriptPaths = new List<string>();
    }

    internal class LuaLocalProcessData : DkmDataItem
    {
        public DkmCustomRuntimeInstance runtimeInstance = null;
        public DkmCustomModuleInstance moduleInstance = null;

        public ulong scratchMemory = 0;

        public bool workingDirectoryRequested = false;
        public string workingDirectory = null;

        public LuaDebugConfiguration configuration;

        // Stack walk data for multiple switches between Lua and C++
        public bool seenLuaFrame = false;
        public int skipFrames = 0; // How many Lua frames to skip
        public int seenFrames = 0; // How many Lua frames we have seen
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

    public class LocalComponent : IDkmCallStackFilter, IDkmSymbolQuery, IDkmSymbolCompilerIdQuery, IDkmSymbolDocumentCollectionQuery, IDkmLanguageExpressionEvaluator
    {
        internal string ExecuteExpression(string expression, DkmStackContext stackContext, DkmStackWalkFrame input, bool allowZero, out ulong address)
        {
            var compilerId = new DkmCompilerId(DkmVendorId.Microsoft, DkmLanguageId.Cpp);
            var language = DkmLanguage.Create("C++", compilerId);
            var languageExpression = DkmLanguageExpression.Create(language, DkmEvaluationFlags.None, expression, null);

            var inspectionContext = DkmInspectionContext.Create(stackContext.InspectionSession, input.RuntimeInstance, stackContext.Thread, 200, DkmEvaluationFlags.None, DkmFuncEvalFlags.None, 10, language, null);

            var workList = DkmWorkList.Create(null);

            try
            {
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
            catch (OperationCanceledException)
            {
                address = 0;
                return null;
            }
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

        internal void LoadConfigurationFile(DkmProcess process, LuaLocalProcessData processData)
        {
            // Check if already loaded
            if (processData.configuration != null)
                return;

            bool TryLoad(string path)
            {
                if (File.Exists(path))
                {
                    var serializer = new JavaScriptSerializer();

                    try
                    {
                        processData.configuration = serializer.Deserialize<LuaDebugConfiguration>(File.ReadAllText(path));

                        return processData.configuration != null;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Failed to load configuration: " + e.Message);
                    }
                }

                return false;
            }

            string pathA = $"{Path.GetDirectoryName(process.Path)}\\";

            if (TryLoad(pathA + "lua_dkm_debug.json"))
                return;

            if (processData.workingDirectory == null)
                return;

            if (TryLoad($"{processData.workingDirectory}\\" + "lua_dkm_debug.json"))
                return;
        }

        DkmStackWalkFrame[] IDkmCallStackFilter.FilterNextFrame(DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            // null input frame indicates the end of the call stack
            if (input == null)
            {
                var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(stackContext.InspectionSession.Process);

                // Reset stack walk frame position
                processData.seenLuaFrame = false;
                processData.skipFrames = 0;
                processData.seenFrames = 0;

                return null;
            }

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

                // Find out the current process working directory (Lua script files will be resolved from that location)
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

                LoadConfigurationFile(process, processData);

                bool isTopFrame = (input.Flags & DkmStackWalkFrameFlags.TopFrame) != 0;

                List<DkmStackWalkFrame> luaFrames = new List<DkmStackWalkFrame>();

                var luaFrameFlags = input.Flags;

                luaFrameFlags &= ~(DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.UserStatusNotDetermined);
                luaFrameFlags |= DkmStackWalkFrameFlags.InlineOptimized;

                if (isTopFrame)
                    luaFrameFlags |= DkmStackWalkFrameFlags.TopFrame;

                ulong? stateAddress = TryEvaluateAddressExpression($"L", stackContext, input);

                ulong? registryAddress = TryEvaluateAddressExpression($"&L->l_G->l_registry", stackContext, input);
                long? version = TryEvaluateNumberExpression($"(int)*L->l_G->version", stackContext, input);

                LuaHelpers.luaVersion = (int)version.GetValueOrDefault(501); // Lua 5.1 doesn't have version field

                string GetLuaFunctionName(ulong callInfoAddress)
                {
                    string functionNameType = null;

                    // Note that in Lua 5.1 call info address if for current call info as opposed to previous call info in future versions
                    if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == 502)
                        functionNameType = TryEvaluateStringExpression($"getfuncname(L, ((CallInfo*){callInfoAddress}), (const char**){processData.scratchMemory})", stackContext, input);
                    else
                        functionNameType = TryEvaluateStringExpression($"funcnamefromcode(L, ((CallInfo*){callInfoAddress}), (const char**){processData.scratchMemory})", stackContext, input);

                    if (functionNameType != null)
                    {
                        ulong? functionNameAddress = DebugHelpers.ReadPointerVariable(process, processData.scratchMemory);

                        if (functionNameAddress.HasValue && functionNameAddress.Value != 0)
                            return DebugHelpers.ReadStringVariable(process, functionNameAddress.Value, 1024);
                    }

                    return null;
                }

                DkmStackWalkFrame GetLuaFunctionStackWalkFrame(ulong callInfoAddress, LuaFunctionCallInfoData callInfoData, LuaValueDataLuaFunction callLuaFunction, string functionName)
                {
                    var currFunctionData = callLuaFunction.value.ReadFunction(process);

                    Debug.Assert(currFunctionData != null);

                    if (currFunctionData == null)
                        return null;

                    Debug.Assert(callInfoData.savedInstructionPointerAddress >= currFunctionData.codeDataAddress);

                    if (callInfoData.savedInstructionPointerAddress < currFunctionData.codeDataAddress)
                        return null;

                    long currInstructionPointer = ((long)callInfoData.savedInstructionPointerAddress - (long)currFunctionData.codeDataAddress) / 4; // unsigned size instructions

                    // If the call was already made, savedpc will be offset by 1 (return location)
                    int prevInstructionPointer = currInstructionPointer == 0 ? 0 : (int)currInstructionPointer - 1;

                    int currLine = currFunctionData.ReadLineInfoFor(process, prevInstructionPointer);

                    string sourceName = currFunctionData.ReadSource(process);

                    if (sourceName != null)
                    {
                        if (currFunctionData.definitionStartLine == 0)
                            functionName = "main";

                        LuaFunctionData functionData = currFunctionData;

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
                        frameData.version = (int)version.GetValueOrDefault(503);

                        frameData.callInfo = callInfoAddress;

                        frameData.functionAddress = callLuaFunction.value.functionAddress;
                        frameData.functionName = functionName;

                        frameData.instructionLine = (int)currLine;
                        frameData.instructionPointer = prevInstructionPointer;

                        frameData.source = sourceName;

                        var frameDataBytes = frameData.Encode();

                        DkmInstructionAddress instructionAddress = DkmCustomInstructionAddress.Create(processData.runtimeInstance, processData.moduleInstance, frameDataBytes, (ulong)currInstructionPointer, frameDataBytes, null);

                        var description = $"{sourceName} {functionName}({argumentList}) Line {currLine}";

                        return DkmStackWalkFrame.Create(stackContext.Thread, instructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, description, input.Registers, input.Annotations);
                    }

                    return null;
                }

                if (LuaHelpers.luaVersion == 501)
                {
                    // Read lua_State
                    ulong temp = stateAddress.Value;

                    // CommonHeader
                    DebugHelpers.SkipStructPointer(process, ref temp);
                    DebugHelpers.SkipStructByte(process, ref temp);
                    DebugHelpers.SkipStructByte(process, ref temp);

                    DebugHelpers.SkipStructByte(process, ref temp); // status
                    DebugHelpers.SkipStructPointer(process, ref temp); // top
                    DebugHelpers.SkipStructPointer(process, ref temp); // base
                    DebugHelpers.SkipStructPointer(process, ref temp); // l_G
                    ulong callInfoAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);
                    ulong savedProgramCounterAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);
                    DebugHelpers.SkipStructPointer(process, ref temp); // stack_last
                    DebugHelpers.SkipStructPointer(process, ref temp); // stack
                    DebugHelpers.SkipStructPointer(process, ref temp); // end_ci
                    ulong baseCallInfoAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);

                    ulong currCallInfoAddress = callInfoAddress;

                    while (currCallInfoAddress > baseCallInfoAddress)
                    {
                        LuaFunctionCallInfoData currCallInfoData = new LuaFunctionCallInfoData();

                        currCallInfoData.ReadFrom(process, currCallInfoAddress);
                        currCallInfoData.ReadFunction(process);

                        // Last function call info program counter is saved in lua_State
                        if (currCallInfoAddress == callInfoAddress)
                            currCallInfoData.savedInstructionPointerAddress = savedProgramCounterAddress;

                        if (currCallInfoData.func == null)
                            break;

                        LuaFunctionCallInfoData prevCallInfoData = new LuaFunctionCallInfoData();

                        prevCallInfoData.ReadFrom(process, currCallInfoAddress - (DebugHelpers.Is64Bit(process) ? 40ul : 24ul));
                        prevCallInfoData.ReadFunction(process);

                        if (prevCallInfoData.func == null)
                            break;

                        if (processData.skipFrames != 0)
                        {
                            processData.skipFrames--;

                            currCallInfoAddress = currCallInfoAddress - (DebugHelpers.Is64Bit(process) ? 40ul : 24ul);
                            continue;
                        }

                        if (currCallInfoData.func.baseType != LuaBaseType.Function)
                            break;

                        var currCallLuaFunction = currCallInfoData.func as LuaValueDataLuaFunction;

                        Debug.Assert(currCallLuaFunction != null);

                        if (currCallLuaFunction == null)
                            break;

                        var prevCallLuaFunction = prevCallInfoData.func as LuaValueDataLuaFunction;

                        string currFunctionName = "[name unavailable]";

                        // Can't get function name if calling function is unknown because of a tail call or if call was not from Lua
                        if (currCallLuaFunction.value.isC_5_1 == 0 && currCallInfoData.tailCallCount_5_1 > 0)
                        {
                            currFunctionName = $"[name unavailable - tail call]";
                        }
                        else if (prevCallLuaFunction != null && prevCallLuaFunction.value.isC_5_1 != 0)
                        {
                            currFunctionName = $"[name unavailable - not called from Lua]";
                        }
                        else
                        {
                            string functionName = GetLuaFunctionName(currCallInfoAddress);

                            if (functionName != null)
                                currFunctionName = functionName;
                        }

                        if (currCallLuaFunction.value.isC_5_1 == 0)
                        {
                            processData.seenLuaFrame = true;
                            processData.seenFrames++;

                            var frame = GetLuaFunctionStackWalkFrame(currCallInfoAddress, currCallInfoData, currCallLuaFunction, currFunctionName);

                            if (frame != null)
                            {
                                luaFrames.Add(frame);

                                luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                            }
                        }
                        else
                        {
                            if (processData.seenLuaFrame)
                            {
                                processData.seenLuaFrame = false;
                                processData.skipFrames = processData.seenFrames;
                                break;
                            }

                            luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[{currFunctionName} C function]", input.Registers, input.Annotations));

                            luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                        }

                        currCallInfoAddress = currCallInfoAddress - (DebugHelpers.Is64Bit(process) ? 40ul : 24ul);
                    }
                }
                else
                {
                    ulong? currCallInfoAddress = TryEvaluateAddressExpression($"L->ci", stackContext, input);

                    while (stateAddress.HasValue && currCallInfoAddress.HasValue && currCallInfoAddress.Value != 0)
                    {
                        LuaFunctionCallInfoData currCallInfoData = new LuaFunctionCallInfoData();

                        currCallInfoData.ReadFrom(process, currCallInfoAddress.Value);
                        currCallInfoData.ReadFunction(process);

                        if (currCallInfoData.func == null)
                            break;

                        if (currCallInfoData.func.baseType == LuaBaseType.Function)
                        {
                            if (processData.skipFrames != 0)
                            {
                                processData.skipFrames--;

                                currCallInfoAddress = currCallInfoData.previousAddress;
                                continue;
                            }

                            // Now we need to know what the previous call info used to call us
                            if (currCallInfoData.previousAddress == 0)
                                break;

                            LuaFunctionCallInfoData prevCallInfoData = new LuaFunctionCallInfoData();

                            prevCallInfoData.ReadFrom(process, currCallInfoData.previousAddress);
                            prevCallInfoData.ReadFunction(process);

                            string currFunctionName = "[name unavailable]";

                            // Can't get function name if previous call status is not 'Lua'
                            if (currCallInfoData.CheckCallStatusFinalizer())
                            {
                                currFunctionName = "__gc";
                            }
                            else if (currCallInfoData.CheckCallStatusTailCall())
                            {
                                currFunctionName = $"[name unavailable - tail call]";
                            }
                            else if (!prevCallInfoData.CheckCallStatusLua())
                            {
                                currFunctionName = $"[name unavailable - not called from Lua]";
                            }
                            else
                            {
                                // Check that it's safe to cast previous call info to a Lua Closure
                                if (prevCallInfoData.func.extendedType != LuaExtendedType.LuaFunction)
                                    break;

                                string functionName = GetLuaFunctionName(currCallInfoData.previousAddress);

                                if (functionName != null)
                                    currFunctionName = functionName;
                            }

                            if (currCallInfoData.func.extendedType == LuaExtendedType.LuaFunction)
                            {
                                var currCallLuaFunction = currCallInfoData.func as LuaValueDataLuaFunction;

                                Debug.Assert(currCallLuaFunction != null);

                                if (currCallLuaFunction == null)
                                    break;

                                processData.seenLuaFrame = true;
                                processData.seenFrames++;

                                var frame = GetLuaFunctionStackWalkFrame(currCallInfoAddress.Value, currCallInfoData, currCallLuaFunction, currFunctionName);

                                if (frame != null)
                                {
                                    luaFrames.Add(frame);

                                    luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                                }
                            }
                            else if (currCallInfoData.func.extendedType == LuaExtendedType.ExternalFunction)
                            {
                                if (processData.seenLuaFrame)
                                {
                                    processData.seenLuaFrame = false;
                                    processData.skipFrames = processData.seenFrames;
                                    break;
                                }

                                luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[{currFunctionName} C function]", input.Registers, input.Annotations));

                                luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                            }
                            else if (currCallInfoData.func.extendedType == LuaExtendedType.ExternalClosure)
                            {
                                if (processData.seenLuaFrame)
                                {
                                    processData.seenLuaFrame = false;
                                    processData.skipFrames = processData.seenFrames;
                                    break;
                                }

                                luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[{currFunctionName} C closure]", input.Registers, input.Annotations));

                                luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                            }

                            currCallInfoAddress = currCallInfoData.previousAddress;
                        }
                        else
                        {
                            currCallInfoAddress = null;
                        }
                    }
                }

                var originalFlags = (input.Flags & ~DkmStackWalkFrameFlags.UserStatusNotDetermined) | DkmStackWalkFrameFlags.NonuserCode;

                if (luaFrames.Count != 0 && isTopFrame)
                    originalFlags &= ~DkmStackWalkFrameFlags.TopFrame;

                luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, originalFlags, input.Description, input.Registers, input.Annotations));

                return luaFrames.ToArray();
            }

            // Mark lua functions as non-user code
            if (input.BasicSymbolInfo.MethodName.StartsWith("luaD_") || input.BasicSymbolInfo.MethodName.StartsWith("luaV_") || input.BasicSymbolInfo.MethodName.StartsWith("luaG_") || input.BasicSymbolInfo.MethodName.StartsWith("luaF_") || input.BasicSymbolInfo.MethodName.StartsWith("luaB_") || input.BasicSymbolInfo.MethodName.StartsWith("luaH_"))
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

            string CheckConfigPaths(string winSourcePath)
            {
                if (processData.configuration != null && processData.configuration.ScriptPaths != null)
                {
                    foreach (var path in processData.configuration.ScriptPaths)
                    {
                        var finalPath = path.Replace('/', '\\');

                        if (!Path.IsPathRooted(finalPath))
                        {
                            if (processData.workingDirectory != null)
                            {
                                string test = Path.GetFullPath(Path.Combine(processData.workingDirectory, finalPath)) + winSourcePath;

                                if (File.Exists(test))
                                    return test;
                            }

                            {
                                string test = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(process.Path), finalPath)) + winSourcePath;

                                if (File.Exists(test))
                                    return test;
                            }
                        }
                        else
                        {
                            string test = finalPath + winSourcePath;

                            if (File.Exists(test))
                                return test;
                        }
                    }
                }

                // Check 'empty' path
                if (processData.workingDirectory != null)
                {
                    string test = Path.GetFullPath(Path.Combine(processData.workingDirectory, winSourcePath));

                    if (File.Exists(test))
                        return test;
                }

                {
                    string test = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(process.Path), winSourcePath));

                    if (File.Exists(test))
                        return test;
                }

                return null;
            }

            string filePath;

            if (frameData.source.StartsWith("@"))
            {
                string winSourcePath = frameData.source.Replace('/', '\\');

                filePath = CheckConfigPaths(winSourcePath.Substring(1));

                if (filePath == null)
                {
                    if (processData.workingDirectory != null)
                        filePath = $"{processData.workingDirectory}\\{winSourcePath.Substring(1)}";
                    else
                        filePath = winSourcePath.Substring(1);
                }
            }
            else
            {
                string winSourcePath = frameData.source.Replace('/', '\\');

                filePath = CheckConfigPaths(winSourcePath);

                if (filePath == null)
                {
                    // TODO: how can we display internal scripts in the debugger?
                    if (processData.workingDirectory != null)
                        filePath = $"{processData.workingDirectory}\\internal.lua";
                    else
                        filePath = "internal.lua";
                }
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

            if (valueBase as LuaValueDataNumber != null)
            {
                var value = valueBase as LuaValueDataNumber;

                if (value.extendedType == LuaExtendedType.IntegerNumber)
                {
                    type = "int";

                    flags |= DkmEvaluationResultFlags.IsBuiltInType;
                    editableValue = $"{value.value}";

                    if (radix == 16)
                        return $"0x{value.value:x}";

                    return $"{value.value}";
                }
                else
                {
                    type = "double";

                    flags |= DkmEvaluationResultFlags.IsBuiltInType;
                    editableValue = $"{value.value}";
                    return $"{value.value}";
                }
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

            functionData.ReadLocals(process, frameData.instructionPointer);

            ExpressionEvaluation evaluation = new ExpressionEvaluation(process, functionData, callInfoData.stackBaseAddress);

            var result = evaluation.Evaluate(expression.Text);

            if (result as LuaValueDataError != null)
            {
                var resultAsError = result as LuaValueDataError;

                completionRoutine(new DkmEvaluateExpressionAsyncResult(DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, expression.Text, expression.Text, resultAsError.value, DkmEvaluationResultFlags.Invalid, null)));
                return;
            }

            // If result is an 'l-value' re-evaluate as a Lua value at address
            if (result.originalAddress != 0)
            {
                completionRoutine(new DkmEvaluateExpressionAsyncResult(EvaluateDataAtLuaValue(inspectionContext, stackFrame, expression.Text, expression.Text, result, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None)));
                return;
            }

            var resultStr = result.AsSimpleDisplayString(inspectionContext.Radix);
            var type = result.GetLuaType();

            DkmEvaluationResultCategory category = DkmEvaluationResultCategory.Data;
            DkmEvaluationResultAccessType accessType = DkmEvaluationResultAccessType.None;
            DkmEvaluationResultStorageType storageType = DkmEvaluationResultStorageType.None;
            DkmEvaluationResultTypeModifierFlags typeModifiers = DkmEvaluationResultTypeModifierFlags.None;

            DkmDataAddress dataAddress = null;

            if (result as LuaValueDataString != null)
            {
                var resultAsString = result as LuaValueDataString;

                if (resultAsString.targetAddress != 0)
                    dataAddress = DkmDataAddress.Create(process.GetNativeRuntimeInstance(), resultAsString.targetAddress, null);
            }

            if (result as LuaValueDataTable != null)
            {
                var resultAsTable = result as LuaValueDataTable;

                resultAsTable.value.LoadValues(process);
                resultAsTable.value.LoadMetaTable(process);

                if (resultAsTable.value.arrayElements.Count == 0 && resultAsTable.value.nodeElements.Count == 0 && resultAsTable.value.metaTable == null)
                    result.evaluationFlags &= ~DkmEvaluationResultFlags.Expandable;
            }

            var dataItem = new LuaEvaluationDataItem
            {
                address = result.originalAddress,
                type = type,
                fullName = expression.Text,
                luaValueData = result
            };

            completionRoutine(new DkmEvaluateExpressionAsyncResult(DkmSuccessEvaluationResult.Create(inspectionContext, stackFrame, expression.Text, expression.Text, result.evaluationFlags, resultStr, null, type, category, accessType, storageType, typeModifiers, dataAddress, null, null, dataItem)));
        }

        DkmEvaluationResult GetTableChildAtIndex(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string fullName, LuaValueDataTable value, int index)
        {
            var process = stackFrame.Process;

            if (index < value.value.arrayElements.Count)
            {
                var element = value.value.arrayElements[index];

                return EvaluateDataAtLuaValue(inspectionContext, stackFrame, $"[{index + 1}]", $"{fullName}[{index + 1}]", element, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
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

                if (name == null || name.Length == 0)
                    name = "%error-name%";

                // Check if name is an identifier
                bool isIdentifierName = false;

                if (char.IsLetter(name[0]) || name[0] == '_')
                {
                    int pos = 1;

                    while (pos < name.Length && (char.IsLetterOrDigit(name[pos]) || name[pos] == '_'))
                        pos++;

                    isIdentifierName = pos == name.Length;
                }

                if (isIdentifierName)
                    return EvaluateDataAtLuaValue(inspectionContext, stackFrame, name, $"{fullName}.{name}", node.value, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);

                return EvaluateDataAtLuaValue(inspectionContext, stackFrame, $"\"{name}\"", $"{fullName}[\"{name}\"]", node.value, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
            }

            index = index - value.value.nodeElements.Count;

            if (index == 0)
            {
                var metaTableValue = new LuaValueDataTable
                {
                    baseType = LuaBaseType.Table,
                    extendedType = LuaExtendedType.Table,
                    evaluationFlags = DkmEvaluationResultFlags.ReadOnly,
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

            if (success.Address.Value == 0)
                return "Null pointer access";

            var target = DebugHelpers.ReadStringVariable(process, success.Address.Value, 32 * 1024);

            if (target != null)
                return target;

            return "Failed to read data";
        }

        void IDkmLanguageExpressionEvaluator.SetValueAsString(DkmEvaluationResult result, string value, int timeout, out string errorText)
        {
            errorText = "Missing evaluation data";
        }

        DkmCompilerId IDkmSymbolCompilerIdQuery.GetCompilerId(DkmInstructionSymbol instruction, DkmInspectionSession inspectionSession)
        {
            return new DkmCompilerId(Guids.luaCompilerGuid, Guids.luaLanguageGuid);
        }

        DkmResolvedDocument[] IDkmSymbolDocumentCollectionQuery.FindDocuments(DkmModule module, DkmSourceFileId sourceFileId)
        {
            // TODO: can we find a mapping from source line to loaded Lua scripts?
            return module.FindDocuments(sourceFileId);
        }
    }
}
