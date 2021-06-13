using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.DefaultPort;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Native;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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

        public bool configurationMissing = false;
        public LuaDebugConfiguration configuration = null;

        public LuaSymbolStore symbolStore = new LuaSymbolStore();

        public DkmNativeModuleInstance moduleWithLoadedLua = null;
        public ulong loadLibraryAddress = 0;

        public bool schemaLoaded = false;

        public bool helperInjectRequested = false;
        public bool helperInjected = false;
        public bool helperInjectionFailed = false;
        public bool helperInitializationWaitActive = false;
        public bool helperInitializationWaitUsed = false;
        public bool helperInitialized = false;
        public bool helperFailed = false;
        public DkmThread helperInitializationSuspensionThread;

        public ulong helperWorkingDirectoryAddress = 0;
        public ulong helperHookFunctionAddress_5_1 = 0;
        public ulong helperHookFunctionAddress_5_2 = 0;
        public ulong helperHookFunctionAddress_5_3 = 0;
        public ulong helperHookFunctionAddress_5_4 = 0;
        public ulong helperHookFunctionAddress_luajit = 0;

        public ulong helperBreakCountAddress = 0;
        public ulong helperBreakDataAddress = 0;
        public ulong helperBreakHitIdAddress = 0;
        public ulong helperBreakHitLuaStateAddress = 0;
        public ulong helperBreakSourcesAddress = 0;

        public ulong helperHookFunctionAddress_5_234_compat = 0;

        public ulong helperCompatLuaDebugEventOffset = 0;
        public ulong helperCompatLuaDebugCurrentLineOffset = 0;
        public ulong helperCompatLuaStateCallInfoOffset = 0;
        public ulong helperCompatCallInfoFunctionOffset = 0;
        public ulong helperCompatTaggedValueTypeTagOffset = 0;
        public ulong helperCompatTaggedValueValueOffset = 0;
        public ulong helperCompatLuaClosureProtoOffset = 0;
        public ulong helperCompatLuaFunctionSourceOffset = 0;
        public ulong helperCompatStringContentOffset = 0;

        public ulong helperLuajitGetInfoAddress = 0;
        public ulong helperLuajitGetStackAddress = 0;

        public ulong helperStepOverAddress = 0;
        public ulong helperStepIntoAddress = 0;
        public ulong helperStepOutAddress = 0;
        public ulong helperSkipDepthAddress = 0;
        public ulong helperStackDepthAtCall = 0;
        public ulong helperAsyncBreakCodeAddress = 0;
        public ulong helperAsyncBreakDataAddress = 0;

        public LuaLocationsMessage luaLocations;

        public Guid breakpointLuaInitialization;

        public bool skipNextInternalCreate = false;
        public Guid breakpointLuaThreadCreate;
        public Guid breakpointLuaThreadCreateExternalStart;
        public Guid breakpointLuaThreadCreateExternalEnd;

        public bool skipNextInternalDestroy = false;
        public Guid breakpointLuaThreadDestroy;
        public Guid breakpointLuaThreadDestroyInternal;

        public Guid breakpointLuaFileLoaded;
        public Guid breakpointLuaFileLoadedSolCompat;

        public bool skipNextInternalBufferLoaded = false;
        public Guid breakpointLuaBufferLoaded;
        public Guid breakpointLuaBufferLoadedExternalStart;
        public Guid breakpointLuaBufferLoadedExternalEnd;

        public bool skipNextRawLoad = false;
        public Guid breakpointLuaLoad;

        // Two-stage Lua exception handling
        public Guid breakpointLuaBreakError;
        public Guid breakpointLuaRuntimeError;
        public ulong breakpointLuaThrowAddress = 0;
        public DkmRuntimeInstructionBreakpoint breakpointLuaThrow;

        public Guid breakpointLuaJitErrThrow;

        public Guid breakpointLuaHelperInitialized;

        public Guid breakpointLuaHelperBreakpointHit;
        public Guid breakpointLuaHelperStepComplete;
        public Guid breakpointLuaHelperStepInto;
        public Guid breakpointLuaHelperStepOut;
        public Guid breakpointLuaHelperAsyncBreak;

        public ulong helperStartAddress = 0;
        public ulong helperEndAddress = 0;

        public ulong executionStartAddress = 0;
        public ulong executionEndAddress = 0;

        public ulong luaSetHookAddress = 0;
        public ulong luaGetInfoAddress = 0;
        public ulong luaGetStackAddress = 0;

        public bool canAccessBasicSymbolInfo = true;
        public Dictionary<ulong, string> knownStackFilterMethodNames = new Dictionary<ulong, string>();

        // Increasing number for scripts without a name
        public int unnamedScriptId = 1;

        public Dictionary<string, string> filePathResolveMap = new Dictionary<string, string>();

        public bool pendingBreakpointDocumentsReady = false;
        public HashSet<string> pendingBreakpointDocuments = new HashSet<string>();

        public bool versionNotificationSent = false;

        public bool schemaLoadedLuajit = false;
        public bool skipStateCreationRecovery = false;

        public Guid ljStackCacheInspectionContextGuid;
        public Dictionary<ulong, List<DkmStackWalkFrame>> ljStackCache = new Dictionary<ulong, List<DkmStackWalkFrame>>();
    }

    // DkmWorkerProcessConnection is only available from VS 2019, so we need an indirection to avoid the type load error
    internal class LuaWorkerConnectionWrapper : DkmDataItem
    {
        public DkmWorkerProcessConnection workerConnection = null;

        public DkmInspectionContext CreateInspectionSession(DkmInspectionSession inspectionSession, DkmRuntimeInstance runtimeInstance, DkmThread thread, DkmEvaluationFlags flags, DkmLanguage language)
        {
            return DkmInspectionContext.Create(inspectionSession, runtimeInstance, thread, 200, flags, DkmFuncEvalFlags.None, 10, language, null, null, DkmCompiledVisualizationDataPriority.None, null, workerConnection);
        }
    }

    internal class LuaStackContextData : DkmDataItem
    {
        // Stack walk data for multiple switches between Lua and C++
        public ulong stateAddress = 0;
        public bool seenLuaFrame = false;
        public int skipFrames = 0; // How many Lua frames to skip
        public int seenFrames = 0; // How many Lua frames we have seen

        public bool hideTopLuaLibraryFrames = false;
        public bool hideInternalLuaLibraryFrames = false;
    }

    internal class LuaFrameLocalsEnumData : DkmDataItem
    {
        public LuaFrameData frameData;
        public LuaFunctionCallInfoData callInfo;
        public LuaFunctionData function;
    }

    internal class LuaNativeTypeEnumData : DkmDataItem
    {
        public string expression;
        public string type;
        public ulong address;
    }

    internal class LuaEvaluationDataItem : DkmDataItem
    {
        public ulong address;
        public string type;
        public string fullName;
        public LuaValueDataBase luaValueData;
    }

    internal class LuaResolvedDocumentItem : DkmDataItem
    {
        public LuaSourceSymbols source;
        public LuaScriptSymbols script;
    }

    internal class LuaEvaluationSessionData : DkmDataItem
    {
        public Dictionary<ulong, LuaFunctionCallInfoData> callInfoDataMap = new Dictionary<ulong, LuaFunctionCallInfoData>();
        public Dictionary<ulong, LuaFunctionData> functionDataMap = new Dictionary<ulong, LuaFunctionData>();
    }

    internal class LuaStackWalkFrameParentData : DkmDataItem
    {
        public DkmStackWalkFrame originalFrame;
        public ulong stateAddress;
    }

    public class LocalComponent : IDkmCallStackFilter, IDkmSymbolQuery, IDkmSymbolCompilerIdQuery, IDkmSymbolDocumentCollectionQuery, IDkmLanguageExpressionEvaluator, IDkmSymbolDocumentSpanQuery, IDkmModuleInstanceLoadNotification, IDkmCustomMessageCallbackReceiver, IDkmLanguageInstructionDecoder, IDkmModuleUserCodeDeterminer
    {
        public static bool attachOnLaunch = true;
        public static bool breakOnError = true;
        public static bool releaseDebugLogs = false;
        public static bool showHiddenFrames = false;
        public static bool useSchema = false;

#if DEBUG
        public static Log log = new Log(Log.LogLevel.Debug, true);
#else
        public static Log log = new Log(Log.LogLevel.Error, true);
#endif

        internal void LoadConfigurationFile(DkmProcess process, LuaLocalProcessData processData)
        {
            // Check if already loaded
            if (processData.configuration != null || processData.configurationMissing)
                return;

            log.Debug($"Loading configuration data");

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

            processData.configurationMissing = true;
        }

        string GetInstructionMethodNameFromBasicSymbolInfo(DkmStackWalkFrame input)
        {
            if (input.BasicSymbolInfo != null)
                return input.BasicSymbolInfo.MethodName;

            return null;
        }

        string GetFrameMethodName(LuaLocalProcessData processData, DkmStackWalkFrame input)
        {
            string methodName = null;

            if (processData.canAccessBasicSymbolInfo)
            {
                try
                {
                    // Only available from VS 2019
                    methodName = GetInstructionMethodNameFromBasicSymbolInfo(input);
                }
                catch (Exception)
                {
                    processData.canAccessBasicSymbolInfo = false;
                }
            }

            if (!processData.canAccessBasicSymbolInfo)
            {
                if (processData.knownStackFilterMethodNames.ContainsKey(input.InstructionAddress.CPUInstructionPart.InstructionPointer))
                {
                    methodName = processData.knownStackFilterMethodNames[input.InstructionAddress.CPUInstructionPart.InstructionPointer];
                }
                else
                {
                    var languageInstructionAddress = DkmLanguageInstructionAddress.Create(DkmLanguage.Create("C++", new DkmCompilerId(DkmVendorId.Microsoft, DkmLanguageId.Cpp)), input.InstructionAddress);

                    try
                    {
                        methodName = languageInstructionAddress.GetMethodName(DkmVariableInfoFlags.None);

                        processData.knownStackFilterMethodNames.Add(input.InstructionAddress.CPUInstructionPart.InstructionPointer, methodName);
                    }
                    catch (Exception)
                    {
                        processData.knownStackFilterMethodNames.Add(input.InstructionAddress.CPUInstructionPart.InstructionPointer, null);
                    }
                }
            }

            return methodName;
        }

        void UpdateEvaluationHelperWorkerConnection(DkmProcess process)
        {
            LuaWorkerConnectionWrapper wrapper = process.GetDataItem<LuaWorkerConnectionWrapper>();

            // If available and haven't been set yet, use local symbols worker connection for faster expression evaluation
            if (wrapper == null)
            {
                DkmWorkerProcessConnection workerConnection = DkmWorkerProcessConnection.GetLocalSymbolsConnection();

                if (workerConnection != null)
                {
                    wrapper = DebugHelpers.GetOrCreateDataItem<LuaWorkerConnectionWrapper>(process);

                    wrapper.workerConnection = workerConnection;
                }
            }
        }

        bool OnFoundLuaCallStack(DkmProcess process, LuaLocalProcessData processData, DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            if (processData.runtimeInstance == null)
            {
                processData.runtimeInstance = process.GetRuntimeInstances().OfType<DkmCustomRuntimeInstance>().FirstOrDefault(el => el.Id.RuntimeType == Guids.luaRuntimeGuid);

                if (processData.runtimeInstance == null)
                    return false;

                processData.moduleInstance = processData.runtimeInstance.GetModuleInstances().OfType<DkmCustomModuleInstance>().FirstOrDefault(el => el.Module != null && el.Module.CompilerId.VendorId == Guids.luaCompilerGuid);

                if (processData.moduleInstance == null)
                    return false;
            }

            if (process.LivePart != null)
            {
                if (processData.scratchMemory == 0)
                    processData.scratchMemory = process.AllocateVirtualMemory(0, 4096, 0x3000, 0x04);

                if (processData.scratchMemory == 0)
                    return false;
            }

            // Find out the current process working directory (Lua script files will be resolved from that location)
            if (processData.workingDirectory == null && !processData.workingDirectoryRequested)
            {
                processData.workingDirectoryRequested = true;

                try
                {
                    // Only available from VS 2019
                    UpdateEvaluationHelperWorkerConnection(process);
                }
                catch (Exception)
                {
                    log.Debug("IDkmCallStackFilter.FilterNextFrame Local symbols connection is not available");
                }

                // Jumping through hoops, kernel32.dll should be loaded
                ulong callAddress = DebugHelpers.FindFunctionAddress(process.GetNativeRuntimeInstance(), "GetCurrentDirectoryA");

                if (callAddress != 0 && processData.scratchMemory != 0)
                {
                    long? length = EvaluationHelpers.TryEvaluateNumberExpression($"((int(*)(int, char*))0x{callAddress:x})(4095, (char*){processData.scratchMemory})", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);

                    if (length.HasValue && length.Value != 0)
                        processData.workingDirectory = EvaluationHelpers.TryEvaluateStringExpression($"(const char*){processData.scratchMemory}", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                }
            }

            LoadConfigurationFile(process, processData);

            // If we haven't attached at launch, prepare Compatibility Mode data here
            if (useSchema && !processData.schemaLoaded)
                LoadSchema(processData, stackContext.InspectionSession, stackContext.Thread, input);

            return true;
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

            var stackContextData = DebugHelpers.GetOrCreateDataItem<LuaStackContextData>(stackContext);

            if (input.ModuleInstance != null && (input.ModuleInstance.Name == "LuaDebugHelper_x86.dll" || input.ModuleInstance.Name == "LuaDebugHelper_x64.dll"))
            {
                stackContextData.hideTopLuaLibraryFrames = true;

                return new DkmStackWalkFrame[1] { DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.Hidden, "[Lua Debugger Helper]", input.Registers, input.Annotations) };
            }

            var process = stackContext.InspectionSession.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

            string methodName = GetFrameMethodName(processData, input);

            if (methodName == null)
                return new DkmStackWalkFrame[1] { input };

            if (input.InstructionAddress.CPUInstructionPart.InstructionPointer == processData.breakpointLuaThrowAddress)
            {
                stackContextData.hideTopLuaLibraryFrames = true;
            }

            if (methodName == "luaV_execute")
            {
                log.Verbose($"IDkmCallStackFilter.FilterNextFrame Got 'luaV_execute' stack frame");

                bool fromHook = stackContextData.hideTopLuaLibraryFrames;

                stackContextData.hideTopLuaLibraryFrames = false;
                stackContextData.hideInternalLuaLibraryFrames = false;

                if (!OnFoundLuaCallStack(process, processData, stackContext, input))
                    return new DkmStackWalkFrame[1] { input };

                bool isTopFrame = (input.Flags & DkmStackWalkFrameFlags.TopFrame) != 0;

                List<DkmStackWalkFrame> luaFrames = new List<DkmStackWalkFrame>();

                var luaFrameFlags = input.Flags;

                luaFrameFlags &= ~(DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.UserStatusNotDetermined);

                if (isTopFrame)
                    luaFrameFlags |= DkmStackWalkFrameFlags.TopFrame;

                ulong? stateAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                // Reset Lua frame skip data if we have switched Lua state
                if (stackContextData.stateAddress != stateAddress.GetValueOrDefault(0))
                {
                    stackContextData.stateAddress = stateAddress.GetValueOrDefault(0);
                    stackContextData.seenLuaFrame = false;
                    stackContextData.skipFrames = 0;
                    stackContextData.seenFrames = 0;
                }

                ulong? registryAddress = EvaluationHelpers.TryEvaluateAddressExpression($"&L->l_G->l_registry", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                if (LuaHelpers.luaVersion == 0)
                {
                    long? version = EvaluationHelpers.TryEvaluateNumberExpression($"(int)*L->l_G->version", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    // Sadly, version field was only added in 5.1 and was removed in 5.4
                    if (!version.HasValue)
                    {
                        // Warning function was added in 5.4
                        if (EvaluationHelpers.TryEvaluateNumberExpression($"(int)L->l_G->ud_warn", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects).HasValue)
                            version = 504;
                    }

                    LuaHelpers.luaVersion = (int)version.GetValueOrDefault(501); // Lua 5.1 doesn't have version field

                    SendVersionNotification(process, processData);
                }

                string GetLuaFunctionName(ulong currCallInfoAddress, ulong prevCallInfoAddress, ulong closureAddress)
                {
                    if (processData.scratchMemory == 0)
                        return null;

                    string functionNameType = null;

                    DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.pauseBreakpoints, null, null).SendLower();

                    // Note that in Lua 5.1 call info address if for current call info as opposed to previous call info in future versions
                    if (LuaHelpers.luaVersion == 501)
                        functionNameType = EvaluationHelpers.TryEvaluateStringExpression($"getfuncname(L, ((CallInfo*){currCallInfoAddress}), (const char**){processData.scratchMemory})", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);
                    else if (LuaHelpers.luaVersion == 502)
                        functionNameType = EvaluationHelpers.TryEvaluateStringExpression($"getfuncname(L, ((CallInfo*){prevCallInfoAddress}), (const char**){processData.scratchMemory})", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);
                    else
                        functionNameType = EvaluationHelpers.TryEvaluateStringExpression($"funcnamefromcode(L, ((CallInfo*){prevCallInfoAddress}), (const char**){processData.scratchMemory})", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);

                    if (functionNameType == null && closureAddress != 0)
                    {
                        long? result = EvaluationHelpers.TryEvaluateNumberExpression($"auxgetinfo(L, \"n\", {processData.scratchMemory}, {closureAddress}, {currCallInfoAddress})", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);

                        if (result.GetValueOrDefault(0) == 1)
                        {
                            string functionName = EvaluationHelpers.TryEvaluateStringExpression($"((lua_Debug*){processData.scratchMemory})->name", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);

                            if (functionName != null && functionName != "?")
                            {
                                DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.resumeBreakpoints, null, null).SendLower();

                                return functionName;
                            }
                        }
                    }

                    DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.resumeBreakpoints, null, null).SendLower();

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

                    if (currFunctionData == null)
                    {
                        log.Error("IDkmCallStackFilter.FilterNextFrame Failed to read Lua function data (Proto)");

                        return null;
                    }

                    long currInstructionPointer = 0;

                    // Invalid value is possible in bad break locations
                    if (callInfoData.savedInstructionPointerAddress >= currFunctionData.codeDataAddress)
                        currInstructionPointer = ((long)callInfoData.savedInstructionPointerAddress - (long)currFunctionData.codeDataAddress) / 4; // unsigned size instructions

                    // If the call was already made, savedpc will be offset by 1 (return location)
                    int prevInstructionPointer = currInstructionPointer == 0 ? 0 : (int)currInstructionPointer - 1;

                    int currLine = currFunctionData.ReadLineInfoFor(process, prevInstructionPointer);

                    string sourceName = currFunctionData.ReadSource(process);

                    lock (processData.symbolStore)
                    {
                        LuaStateSymbols stateSymbols = processData.symbolStore.FetchOrCreate(stateAddress.Value);

                        if (stateSymbols.unnamedScriptMapping.ContainsKey(sourceName))
                            sourceName = stateSymbols.unnamedScriptMapping[sourceName];
                    }

                    if (sourceName != null)
                    {
                        if (currFunctionData.hasDefinitionLineInfo && currFunctionData.definitionStartLine_opt == 0)
                            functionName = "main";

                        LuaFunctionData functionData = currFunctionData;

                        lock (processData.symbolStore)
                        {
                            processData.symbolStore.FetchOrCreate(stateAddress.Value).AddSourceFromFunction(process, functionData);
                        }

                        string argumentList = "";

                        for (int i = 0; i < functionData.argumentCount; i++)
                        {
                            LuaLocalVariableData argument = new LuaLocalVariableData();

                            argument.ReadFrom(process, functionData.localVariableDataAddress + (ulong)(i * LuaLocalVariableData.StructSize(process)));

                            argumentList += (i == 0 ? "" : ", ") + argument.name;
                        }

                        LuaAddressEntityData entityData = new LuaAddressEntityData
                        {
                            source = sourceName,
                            line = currLine,

                            functionAddress = callLuaFunction.value.functionAddress,
                            functionInstructionPointer = prevInstructionPointer,
                        };

                        LuaFrameData frameData = new LuaFrameData
                        {
                            state = stateAddress.Value,

                            registryAddress = registryAddress.GetValueOrDefault(0),
                            version = LuaHelpers.luaVersion,

                            callInfo = callInfoAddress,

                            functionAddress = callLuaFunction.value.functionAddress,
                            functionName = functionName,

                            instructionLine = (int)currLine,
                            instructionPointer = prevInstructionPointer,

                            source = sourceName
                        };

                        var entityDataBytes = entityData.Encode();
                        var frameDataBytes = frameData.Encode();

                        DkmInstructionAddress instructionAddress = DkmCustomInstructionAddress.Create(processData.runtimeInstance, processData.moduleInstance, entityDataBytes, (ulong)((currLine << 16) + prevInstructionPointer), frameDataBytes, null);

                        var description = $"{sourceName} {functionName}({argumentList}) Line {currLine}";

                        var parentFrameData = DkmStackWalkFrameData.Create(stackContext.InspectionSession, new LuaStackWalkFrameParentData { originalFrame = input, stateAddress = stateAddress.Value });

                        return DkmStackWalkFrame.Create(stackContext.Thread, instructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, description, input.Registers, input.Annotations, null, null, parentFrameData);
                    }

                    return null;
                }

                if (LuaHelpers.luaVersion == 501)
                {
                    ulong callInfoAddress = 0;
                    ulong? savedProgramCounterAddress = null;
                    ulong baseCallInfoAddress = 0;

                    if (Schema.LuaStateData.available && stateAddress.HasValue && Schema.LuaStateData.baseCallInfoAddress_5_1.HasValue)
                    {
                        callInfoAddress = DebugHelpers.ReadPointerVariable(process, stateAddress.Value + Schema.LuaStateData.callInfoAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                        baseCallInfoAddress = DebugHelpers.ReadPointerVariable(process, stateAddress.Value + Schema.LuaStateData.baseCallInfoAddress_5_1.GetValueOrDefault(0)).GetValueOrDefault(0);

                        if (Schema.LuaStateData.savedProgramCounterAddress_5_1_opt.HasValue)
                            savedProgramCounterAddress = DebugHelpers.ReadPointerVariable(process, stateAddress.Value + Schema.LuaStateData.savedProgramCounterAddress_5_1_opt.GetValueOrDefault(0)).GetValueOrDefault(0);
                    }
                    else if (stateAddress.HasValue)
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
                        callInfoAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);
                        savedProgramCounterAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);
                        DebugHelpers.SkipStructPointer(process, ref temp); // stack_last
                        DebugHelpers.SkipStructPointer(process, ref temp); // stack
                        DebugHelpers.SkipStructPointer(process, ref temp); // end_ci
                        baseCallInfoAddress = DebugHelpers.ReadStructPointer(process, ref temp).GetValueOrDefault(0);
                    }

                    ulong currCallInfoAddress = callInfoAddress;

                    while (currCallInfoAddress > baseCallInfoAddress)
                    {
                        LuaFunctionCallInfoData currCallInfoData = new LuaFunctionCallInfoData();

                        currCallInfoData.ReadFrom(process, currCallInfoAddress);
                        currCallInfoData.ReadFunction(process);

                        // Last function call info program counter is saved in lua_State
                        if (currCallInfoAddress == callInfoAddress && savedProgramCounterAddress.HasValue)
                            currCallInfoData.savedInstructionPointerAddress = savedProgramCounterAddress.Value;

                        if (currCallInfoData.func == null)
                            break;

                        ulong prevCallInfoDataAddress;

                        if (Schema.LuaFunctionCallInfoData.available)
                            prevCallInfoDataAddress = currCallInfoAddress - (ulong)Schema.LuaFunctionCallInfoData.structSize;
                        else
                            prevCallInfoDataAddress = currCallInfoAddress - (DebugHelpers.Is64Bit(process) ? 40ul : 24ul);

                        LuaFunctionCallInfoData prevCallInfoData = new LuaFunctionCallInfoData();

                        prevCallInfoData.ReadFrom(process, prevCallInfoDataAddress);
                        prevCallInfoData.ReadFunction(process);

                        if (prevCallInfoData.func == null)
                            break;

                        if (stackContextData.skipFrames != 0)
                        {
                            stackContextData.skipFrames--;

                            currCallInfoAddress = prevCallInfoDataAddress;
                            continue;
                        }

                        if (currCallInfoData.func.baseType != LuaBaseType.Function)
                            break;

                        var currCallLuaFunction = currCallInfoData.func as LuaValueDataLuaFunction;
                        var currCallExternalClosure = currCallInfoData.func as LuaValueDataExternalClosure;

                        Debug.Assert(currCallLuaFunction != null || currCallExternalClosure != null);

                        if (currCallLuaFunction == null && currCallExternalClosure == null)
                            break;

                        var prevCallLuaFunction = prevCallInfoData.func as LuaValueDataLuaFunction;

                        string currFunctionName = "[name unavailable]";

                        // Can't get function name if calling function is unknown because of a tail call or if call was not from Lua
                        if (currCallInfoData.tailCallCount_5_1 > 0)
                        {
                            currFunctionName = $"[name unavailable - tail call]";
                        }
                        else if (prevCallLuaFunction == null)
                        {
                            currFunctionName = $"[name unavailable - not called from Lua]";
                        }
                        else
                        {
                            LuaStateSymbols stateSymbols;

                            lock (processData.symbolStore)
                            {
                                stateSymbols = processData.symbolStore.FetchOrCreate(stateAddress.Value);
                            }

                            if (currCallLuaFunction != null)
                            {
                                string functionName = stateSymbols.FetchFunctionName(currCallLuaFunction.value.functionAddress);

                                if (functionName == null)
                                {
                                    functionName = GetLuaFunctionName(currCallInfoAddress, prevCallInfoDataAddress, currCallLuaFunction.targetAddress);

                                    if (functionName != null)
                                        stateSymbols.AddFunctionName(currCallLuaFunction.value.functionAddress, functionName);
                                }

                                if (functionName != null)
                                    currFunctionName = functionName;
                            }
                            else if (currCallExternalClosure != null)
                            {
                                string functionName = stateSymbols.FetchFunctionName(currCallExternalClosure.value.functionAddress);

                                if (functionName == null)
                                {
                                    functionName = GetLuaFunctionName(currCallInfoAddress, prevCallInfoDataAddress, currCallExternalClosure.targetAddress);

                                    if (functionName != null)
                                        stateSymbols.AddFunctionName(currCallExternalClosure.value.functionAddress, functionName);
                                }

                                if (functionName != null)
                                    currFunctionName = functionName;
                            }
                        }

                        if (currCallLuaFunction != null)
                        {
                            stackContextData.seenLuaFrame = true;
                            stackContextData.seenFrames++;

                            var frame = GetLuaFunctionStackWalkFrame(currCallInfoAddress, currCallInfoData, currCallLuaFunction, currFunctionName);

                            if (frame != null)
                            {
                                luaFrames.Add(frame);

                                luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                            }
                        }
                        else
                        {
                            if (stackContextData.seenLuaFrame)
                            {
                                stackContextData.seenLuaFrame = false;
                                stackContextData.skipFrames = stackContextData.seenFrames;
                                break;
                            }

                            stackContextData.seenFrames++;

                            if (!fromHook)
                                luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[{currFunctionName} C closure]", input.Registers, input.Annotations));

                            luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                        }

                        currCallInfoAddress = prevCallInfoDataAddress;
                    }
                }
                else
                {
                    ulong? currCallInfoAddress;

                    if (Schema.LuaStateData.available && stateAddress.HasValue)
                        currCallInfoAddress = DebugHelpers.ReadPointerVariable(process, stateAddress.Value + Schema.LuaStateData.callInfoAddress.GetValueOrDefault(0));
                    else
                        currCallInfoAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L->ci", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    while (stateAddress.HasValue && currCallInfoAddress.HasValue && currCallInfoAddress.Value != 0)
                    {
                        LuaFunctionCallInfoData currCallInfoData = new LuaFunctionCallInfoData();

                        currCallInfoData.ReadFrom(process, currCallInfoAddress.Value);
                        currCallInfoData.ReadFunction(process);

                        if (currCallInfoData.func == null)
                            break;

                        if (currCallInfoData.func.baseType == LuaBaseType.Function)
                        {
                            if (stackContextData.skipFrames != 0)
                            {
                                stackContextData.skipFrames--;

                                currCallInfoAddress = currCallInfoData.previousAddress;
                                continue;
                            }

                            // Now we need to know what the previous call info used to call us
                            if (currCallInfoData.previousAddress == 0)
                                break;

                            LuaFunctionCallInfoData prevCallInfoData = new LuaFunctionCallInfoData();

                            prevCallInfoData.ReadFrom(process, currCallInfoData.previousAddress);
                            prevCallInfoData.ReadFunction(process);

                            LuaValueDataLuaFunction currCallLuaFunction = null;
                            LuaValueDataExternalFunction currCallExternalFunction = null;
                            LuaValueDataExternalClosure currCallExternalClosure = null;

                            if (currCallInfoData.func.extendedType == LuaExtendedType.LuaFunction)
                                currCallLuaFunction = currCallInfoData.func as LuaValueDataLuaFunction;
                            else if (currCallInfoData.func.extendedType == LuaExtendedType.ExternalFunction)
                                currCallExternalFunction = currCallInfoData.func as LuaValueDataExternalFunction;
                            else if (currCallInfoData.func.extendedType == LuaExtendedType.ExternalClosure)
                                currCallExternalClosure = currCallInfoData.func as LuaValueDataExternalClosure;

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

                                LuaStateSymbols stateSymbols;

                                lock (processData.symbolStore)
                                {
                                    stateSymbols = processData.symbolStore.FetchOrCreate(stateAddress.Value);
                                }

                                if (currCallLuaFunction != null)
                                {
                                    string functionName = stateSymbols.FetchFunctionName(currCallLuaFunction.value.functionAddress);

                                    if (functionName == null)
                                    {
                                        functionName = GetLuaFunctionName(currCallInfoAddress.Value, currCallInfoData.previousAddress, currCallLuaFunction.targetAddress);

                                        if (functionName != null)
                                            stateSymbols.AddFunctionName(currCallLuaFunction.value.functionAddress, functionName);
                                    }

                                    if (functionName != null)
                                        currFunctionName = functionName;
                                }
                                else if (currCallExternalFunction != null)
                                {
                                    string functionName = stateSymbols.FetchFunctionName(currCallExternalFunction.targetAddress);

                                    if (functionName == null)
                                    {
                                        functionName = GetLuaFunctionName(currCallInfoAddress.Value, currCallInfoData.previousAddress, 0);

                                        if (functionName != null)
                                            stateSymbols.AddFunctionName(currCallExternalFunction.targetAddress, functionName);
                                    }

                                    if (functionName != null)
                                        currFunctionName = functionName;
                                }
                                else if (currCallExternalClosure != null)
                                {
                                    string functionName = stateSymbols.FetchFunctionName(currCallExternalClosure.value.functionAddress);

                                    if (functionName == null)
                                    {
                                        functionName = GetLuaFunctionName(currCallInfoAddress.Value, currCallInfoData.previousAddress, currCallExternalClosure.targetAddress);

                                        if (functionName != null)
                                            stateSymbols.AddFunctionName(currCallExternalClosure.value.functionAddress, functionName);
                                    }

                                    if (functionName != null)
                                        currFunctionName = functionName;
                                }
                                else
                                {
                                    log.Warning($"IDkmCallStackFilter.FilterNextFrame unknown functiontype");
                                }
                            }

                            if (currCallInfoData.func.extendedType == LuaExtendedType.LuaFunction)
                            {
                                Debug.Assert(currCallLuaFunction != null);

                                if (currCallLuaFunction == null)
                                    break;

                                stackContextData.seenLuaFrame = true;
                                stackContextData.seenFrames++;

                                var frame = GetLuaFunctionStackWalkFrame(currCallInfoAddress.Value, currCallInfoData, currCallLuaFunction, currFunctionName);

                                if (frame != null)
                                {
                                    luaFrames.Add(frame);

                                    luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                                }

                                // When Lua function is called from Lua through C functions (for example, metatable operations), we will have a fresh 'luaV_execute' frame
                                if (LuaHelpers.luaVersion == 503 && prevCallInfoData.CheckCallStatusLua() && (currCallInfoData.callStatus & (int)CallStatus_5_3.Fresh) != 0)
                                {
                                    stackContextData.skipFrames = stackContextData.seenFrames;

                                    stackContextData.hideInternalLuaLibraryFrames = true;
                                    break;
                                }

                                // In Lua 5.4, Lua calls are made with a fresh 'luaV_execute' frame
                                if (LuaHelpers.luaVersion == 504 && prevCallInfoData.CheckCallStatusLua())
                                {
                                    stackContextData.skipFrames = stackContextData.seenFrames;

                                    stackContextData.hideInternalLuaLibraryFrames = true;
                                    break;
                                }
                            }
                            else if (currCallInfoData.func.extendedType == LuaExtendedType.ExternalFunction)
                            {
                                if (stackContextData.seenLuaFrame)
                                {
                                    stackContextData.seenLuaFrame = false;
                                    stackContextData.skipFrames = stackContextData.seenFrames;
                                    break;
                                }

                                stackContextData.seenFrames++;

                                if (!fromHook)
                                    luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[{currFunctionName} C function]", input.Registers, input.Annotations));

                                luaFrameFlags &= ~DkmStackWalkFrameFlags.TopFrame;
                            }
                            else if (currCallInfoData.func.extendedType == LuaExtendedType.ExternalClosure)
                            {
                                if (stackContextData.seenLuaFrame)
                                {
                                    stackContextData.seenLuaFrame = false;
                                    stackContextData.skipFrames = stackContextData.seenFrames;
                                    break;
                                }

                                stackContextData.seenFrames++;

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

                if (!stackContextData.hideInternalLuaLibraryFrames)
                    luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, null, input.FrameBase, input.FrameSize, DkmStackWalkFrameFlags.NonuserCode, "[Transition to Lua]", input.Registers, input.Annotations));

                log.Verbose($"IDkmCallStackFilter.FilterNextFrame Completed 'luaV_execute' stack frame");

                return luaFrames.ToArray();
            }

            // Mark lua functions as non-user code
            if (methodName.StartsWith("luaD_") || methodName.StartsWith("luaV_") || methodName.StartsWith("luaG_") || methodName.StartsWith("luaF_") || methodName.StartsWith("luaB_") || methodName.StartsWith("luaH_") || methodName.StartsWith("luaT_"))
            {
                var flags = (input.Flags & ~DkmStackWalkFrameFlags.UserStatusNotDetermined) | DkmStackWalkFrameFlags.NonuserCode;

                if ((stackContextData.hideTopLuaLibraryFrames || stackContextData.hideInternalLuaLibraryFrames) && !showHiddenFrames)
                    flags |= DkmStackWalkFrameFlags.Hidden;

                return new DkmStackWalkFrame[1] { DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, flags, input.Description, input.Registers, input.Annotations) };
            }

            if (stackContextData.hideTopLuaLibraryFrames && (methodName == "callhook" || methodName == "callrethooks" || methodName == "traceexec" || methodName == "lua_error" || methodName == "tryfuncTM") && !showHiddenFrames)
            {
                var flags = (input.Flags & ~DkmStackWalkFrameFlags.UserStatusNotDetermined) | DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.Hidden;

                return new DkmStackWalkFrame[1] { DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, flags, input.Description, input.Registers, input.Annotations) };
            }

            // Luajit support for x86
            if (methodName.StartsWith("_lj_") || methodName.StartsWith("lj_BC_") || methodName.StartsWith("lj_vm_inshook") || methodName.StartsWith("lj_vm_hotcall"))
            {
                log.Verbose($"IDkmCallStackFilter.FilterNextFrame Found a Luajit frame");

                stackContextData.hideTopLuaLibraryFrames = false;
                stackContextData.hideInternalLuaLibraryFrames = false;

                LuaHelpers.luaVersion = LuaHelpers.luaVersionLuajit;

                if (!OnFoundLuaCallStack(process, processData, stackContext, input))
                    return new DkmStackWalkFrame[1] { input };

                LoadSchemaLuajit(processData, stackContext.InspectionSession, stackContext.Thread, input);

                SendVersionNotification(process, processData);

                // Invalidate cache
                if (processData.ljStackCacheInspectionContextGuid != stackContext.InspectionSession.UniqueId)
                {
                    processData.ljStackCache = new Dictionary<ulong, List<DkmStackWalkFrame>>();
                    processData.ljStackCacheInspectionContextGuid = stackContext.InspectionSession.UniqueId;
                }

                // Check cache
                if (processData.ljStackCache.ContainsKey(input.FrameBase))
                {
                    log.Verbose($"IDkmCallStackFilter.FilterNextFrame Completed Luajit stack frame (cached)");

                    return processData.ljStackCache[input.FrameBase].ToArray();
                }

                ulong? stateAddress = EvaluationHelpers.TryEvaluateAddressExpression(DebugHelpers.Is64Bit(process) ? $"@rbp" : $"@ebp", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                if (!stateAddress.HasValue)
                    return new DkmStackWalkFrame[1] { input };

                bool isTopFrame = (input.Flags & DkmStackWalkFrameFlags.TopFrame) != 0;

                List<DkmStackWalkFrame> luaFrames = new List<DkmStackWalkFrame>();

                var luaFrameFlags = input.Flags;

                luaFrameFlags &= ~(DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.UserStatusNotDetermined);

                if (isTopFrame)
                    luaFrameFlags |= DkmStackWalkFrameFlags.TopFrame;

                LuajitStateData luajitStateData = null;

                if (Schema.LuajitStateData.available)
                {
                    luajitStateData = new LuajitStateData();
                    luajitStateData.ReadFrom(process, stateAddress.Value);
                }

                DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.pauseBreakpoints, null, null).SendLower();

                int frameNum = 0;
                ulong frameAddress = LuajitHelpers.FindDebugFrame(process, luajitStateData, frameNum, out int frameSize, out int debugFrameIndex);

                while (frameAddress != 0)
                {
                    ulong functionAddress = Schema.Luajit.fullPointer ? frameAddress - LuaHelpers.GetValueSize(process) : frameAddress;

                    LuaValueDataBase callFunction = LuaHelpers.ReadValueOfType(process, (int)LuaExtendedType.LuaFunction, 0, functionAddress);

                    long? status;

                    if (Schema.LuaDebugData.ici.HasValue)
                    {
                        DebugHelpers.TryWriteIntVariable(process, processData.scratchMemory + Schema.LuaDebugData.ici.Value, debugFrameIndex);

                        status = EvaluationHelpers.TryEvaluateNumberExpression($"lua_getinfo((lua_State*){stateAddress}, \"Sln\", (lua_Debug*){processData.scratchMemory})", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);
                    }
                    else
                    {
                        EvaluationHelpers.TryEvaluateNumberExpression($"lua_getstack((lua_State*){stateAddress}, {frameNum}, (lua_Debug*){processData.scratchMemory})", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);

                        status = EvaluationHelpers.TryEvaluateNumberExpression($"lua_getinfo((lua_State*){stateAddress}, \"Sln\", (lua_Debug*){processData.scratchMemory})", stackContext.InspectionSession, stackContext.Thread, input, DkmEvaluationFlags.None);
                    }

                    if (status.HasValue && status.Value == 1)
                    {
                        LuaDebugData debugData = new LuaDebugData();

                        debugData.ReadFrom(process, processData.scratchMemory);

                        if (debugData.name != null && debugData.source != null && debugData.what != null)
                        {
                            if (debugData.what == "Lua" || debugData.what == "main")
                            {
                                LuaValueDataLuaFunction callLuaFunction = callFunction as LuaValueDataLuaFunction;

                                int instructionPointer = -1;

                                if (callLuaFunction != null)
                                {
                                    void RegisterMissingScript()
                                    {
                                        var currFunctionData = callLuaFunction.value.ReadFunction(process);

                                        if (currFunctionData == null)
                                            return;

                                        string source = currFunctionData.ReadSource(process);

                                        if (source == null)
                                            return;

                                        bool foundNewScript = false;

                                        lock (processData.symbolStore)
                                        {
                                            LuaStateSymbols stateSymbols = processData.symbolStore.FetchOrCreate(stateAddress.Value);

                                            if (stateSymbols.FetchScriptSource(source) == null)
                                            {
                                                stateSymbols.AddScriptSource(source, null, null);

                                                foundNewScript = true;
                                            }
                                        }

                                        if (!foundNewScript)
                                            return;

                                        string filePath = TryGetSourceFilePath(process, processData, source);

                                        if (filePath == null)
                                            return;

                                        log.Debug($"IDkmCallStackFilter.FilterNextFrame Resolved new script {source} to {filePath}");

                                        if (HasPendingBreakpoint(process, processData, filePath))
                                        {
                                            log.Debug($"IDkmCallStackFilter.FilterNextFrame Reloading script {source} pending breakpoints");

                                            var message = DkmCustomMessage.Create(process.Connection, process, Guid.Empty, MessageToVsService.reloadBreakpoints, Encoding.UTF8.GetBytes(filePath), null);

                                            message.SendToVsService(Guids.luaVsPackageComponentGuid, false);
                                        }
                                    }

                                    RegisterMissingScript();

                                    ulong nextframe = frameSize != 0 ? frameAddress + (ulong)frameSize * LuaHelpers.GetValueSize(process) : 0u;

                                    instructionPointer = LuajitHelpers.FindFrameInstructionPointer(process, luajitStateData, callLuaFunction, nextframe);
                                }

                                string argumentList = "";

                                LuaAddressEntityData entityData = new LuaAddressEntityData
                                {
                                    source = debugData.source,
                                    line = debugData.currentLine,

                                    functionAddress = callLuaFunction != null ? callLuaFunction.value.functionAddress : 0,
                                    functionInstructionPointer = instructionPointer,
                                };

                                LuaFrameData frameData = new LuaFrameData
                                {
                                    state = stateAddress.Value,

                                    registryAddress = luajitStateData != null ? luajitStateData.envAddress : 0,
                                    version = LuaHelpers.luaVersion,

                                    callInfo = frameAddress,

                                    functionAddress = callLuaFunction != null ? callLuaFunction.value.functionAddress : 0,
                                    functionName = debugData.name,

                                    instructionLine = debugData.currentLine,
                                    instructionPointer = instructionPointer,

                                    source = debugData.source
                                };

                                var entityDataBytes = entityData.Encode();
                                var frameDataBytes = frameData.Encode();

                                DkmInstructionAddress instructionAddress = DkmCustomInstructionAddress.Create(processData.runtimeInstance, processData.moduleInstance, entityDataBytes, (ulong)((debugData.currentLine << 16) + instructionPointer), frameDataBytes, null);

                                var description = $"{debugData.source} {debugData.name}({argumentList}) Line {debugData.currentLine} PC {instructionPointer} FS {frameSize}";

                                var parentFrameData = DkmStackWalkFrameData.Create(stackContext.InspectionSession, new LuaStackWalkFrameParentData { originalFrame = input, stateAddress = stateAddress.Value });

                                luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, instructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, description, input.Registers, input.Annotations, null, null, parentFrameData));
                            }
                            else if (debugData.what == "C")
                            {
                                luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"[{debugData.name} C function]", input.Registers, input.Annotations));
                            }
                        }
                    }

                    frameNum++;

                    frameAddress = LuajitHelpers.FindDebugFrame(process, luajitStateData, frameNum, out frameSize, out debugFrameIndex);
                }

                if (!stackContextData.hideInternalLuaLibraryFrames)
                {
                    if (luaFrames.Count == 0)
                    {
                        DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.resumeBreakpoints, null, null).SendLower();

                        return new DkmStackWalkFrame[1] { input };
                    }

                    luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, null, input.FrameBase, input.FrameSize, DkmStackWalkFrameFlags.NonuserCode, "[Transition to Luajit]", input.Registers, input.Annotations));
                }

                // Cache the result
                processData.ljStackCache[input.FrameBase] = luaFrames;

                DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.resumeBreakpoints, null, null).SendLower();

                log.Verbose($"IDkmCallStackFilter.FilterNextFrame Completed Luajit stack frame");

                return luaFrames.ToArray();
            }

            if (stackContextData.hideTopLuaLibraryFrames && (methodName == "lj_dispatch_ins" || methodName == "lj_dispatch_call" || methodName == "lj_cf_error" || methodName.StartsWith("lj_err_") || methodName.StartsWith("lj_ffh_")) && !showHiddenFrames)
            {
                var flags = (input.Flags & ~DkmStackWalkFrameFlags.UserStatusNotDetermined) | DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.Hidden;

                return new DkmStackWalkFrame[1] { DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, flags, input.Description, input.Registers, input.Annotations) };
            }

            return new DkmStackWalkFrame[1] { input };
        }

        string CheckConfigPaths(string processPath, LuaLocalProcessData processData, string winSourcePath, int skipDepth)
        {
            log.Verbose($"Checking for file in configuration paths");

            if (processData.configuration != null && processData.configuration.ScriptPaths != null)
            {
                foreach (var path in processData.configuration.ScriptPaths)
                {
                    var finalPath = path.Replace('/', '\\');

                    try
                    {
                        if (!Path.IsPathRooted(finalPath))
                        {
                            if (processData.workingDirectory != null)
                            {
                                string test = Path.GetFullPath(Path.GetFullPath(Path.Combine(processData.workingDirectory, finalPath)) + winSourcePath);

                                if (File.Exists(test))
                                    return test;
                            }

                            {
                                string test = Path.GetFullPath(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(processPath), finalPath)) + winSourcePath);

                                if (File.Exists(test))
                                    return test;
                            }
                        }
                        else
                        {
                            string test = Path.GetFullPath(finalPath + winSourcePath);

                            if (File.Exists(test))
                                return test;
                        }
                    }
                    catch (Exception e)
                    {
                        log.Debug($"Exception while checking search path '{finalPath}': {e.Message}");
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
                string test = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(processPath), winSourcePath));

                if (File.Exists(test))
                    return test;
            }

            if (skipDepth < 2)
            {
                int folderPos = winSourcePath.IndexOf('\\');

                if (folderPos != -1)
                    return CheckConfigPaths(processPath, processData, winSourcePath.Substring(folderPos + 1), skipDepth + 1);
            }

            return null;
        }

        string TryFindSourcePath(string processPath, LuaLocalProcessData processData, string source, string content, bool saveToTemp, out string status)
        {
            string filePath = null;

            if (source.StartsWith("@"))
                source = source.Substring(1);

            string winSourcePath = source.Replace('/', '\\');

            try
            {
                if (processData.filePathResolveMap.ContainsKey(winSourcePath))
                {
                    filePath = processData.filePathResolveMap[winSourcePath];
                }
                else
                {
                    filePath = CheckConfigPaths(processPath, processData, winSourcePath, 0);

                    processData.filePathResolveMap.Add(winSourcePath, filePath);
                }
            }
            catch (Exception)
            {
                log.Error($"Failed to check config paths for {winSourcePath}");
            }

            if (filePath == null)
            {
                // If we have source data, write it to the temp directory and return it
                if (content != null && content.Length != 0)
                {
                    string pattern = "[<>:\"/\\\\\\|\\?\\*]";
                    string cleanPath = Regex.Replace(winSourcePath, pattern, "+");
                    string tempPath = $"{Path.GetTempPath()}{cleanPath}";

                    if (!tempPath.EndsWith(".lua"))
                        tempPath += ".lua";

                    log.Debug($"Writing {source} content (length {content.Length}) to temp path {tempPath}");

                    try
                    {
                        if (saveToTemp)
                        {
                            File.WriteAllText(tempPath, content);

                            status = "Loaded from memory";
                        }
                        else
                        {
                            status = "Stored in memory";
                        }

                        return tempPath;
                    }
                    catch (Exception)
                    {
                        log.Error($"Failed to write {source} content to temp path {tempPath}");
                    }
                }

                if (processData.workingDirectory != null)
                    filePath = $"{processData.workingDirectory}\\{winSourcePath}";
                else
                    filePath = winSourcePath;

                status = "Failed to find";
            }
            else
            {
                status = "Loaded from file";
            }

            return filePath;
        }

        string TryGetSourceFilePath(DkmProcess process, LuaLocalProcessData processData, string source)
        {
            if (source == null)
                return null;

            LuaScriptSymbols scriptSource;

            lock (processData.symbolStore)
            {
                scriptSource = processData.symbolStore.FetchScriptSource(source);
            }

            if (scriptSource?.resolvedFileName != null)
                return scriptSource.resolvedFileName;

            string filePath = TryFindSourcePath(process.Path, processData, source, scriptSource?.scriptContent, true, out string loadStatus);

            if (scriptSource != null)
                scriptSource.resolvedFileName = filePath;

            return filePath;
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

            log.Debug($"IDkmSymbolQuery.GetSourcePosition begin");

            var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

            var instructionSymbol = instruction as DkmCustomInstructionSymbol;

            Debug.Assert(instructionSymbol != null);

            if (instructionSymbol.EntityId != null)
            {
                var addressEntityData = new LuaAddressEntityData();

                addressEntityData.ReadFrom(instructionSymbol.EntityId.ToArray());

                string filePath = TryGetSourceFilePath(process, processData, addressEntityData.source);

                log.Debug($"IDkmSymbolQuery.GetSourcePosition success");

                startOfLine = true;
                return DkmSourcePosition.Create(DkmSourceFileId.Create(filePath, null, null, null), new DkmTextSpan(addressEntityData.line, addressEntityData.line, 0, 0));
            }

            log.Error($"IDkmSymbolQuery.GetSourcePosition failure");

            return instruction.GetSourcePosition(flags, inspectionSession, out startOfLine);
        }

        object IDkmSymbolQuery.GetSymbolInterface(DkmModule module, Guid interfaceID)
        {
            return module.GetSymbolInterface(interfaceID);
        }

        void GetEvaluationSessionData(DkmProcess process, DkmInspectionSession inspectionSession, LuaFrameData frameData, out LuaFunctionCallInfoData callInfoData, out LuaFunctionData functionData, out LuaClosureData closureData)
        {
            var evaluationSession = DebugHelpers.GetOrCreateDataItem<LuaEvaluationSessionData>(inspectionSession);

            if (evaluationSession.callInfoDataMap.ContainsKey(frameData.callInfo))
            {
                callInfoData = evaluationSession.callInfoDataMap[frameData.callInfo];
            }
            else
            {
                callInfoData = new LuaFunctionCallInfoData();

                callInfoData.ReadFrom(process, frameData.callInfo);
                callInfoData.ReadFunction(process);

                evaluationSession.callInfoDataMap.Add(frameData.callInfo, callInfoData);
            }

            if (evaluationSession.functionDataMap.ContainsKey(frameData.functionAddress))
            {
                functionData = evaluationSession.functionDataMap[frameData.functionAddress];
            }
            else
            {
                functionData = new LuaFunctionData();

                functionData.ReadFrom(process, frameData.functionAddress);

                functionData.ReadUpvalues(process);
                functionData.ReadLocals(process, frameData.instructionPointer); // We can cache evaluation for target instruction pointer in a session

                evaluationSession.functionDataMap.Add(frameData.functionAddress, functionData);
            }

            if (callInfoData.func != null && callInfoData.func.extendedType == LuaExtendedType.LuaFunction)
                closureData = (callInfoData.func as LuaValueDataLuaFunction).value;
            else
                closureData = null;
        }

        void ClearEvaluationSessionData(DkmInspectionSession inspectionSession)
        {
            if (inspectionSession != null)
            {
                var evaluationSession = inspectionSession.GetDataItem<LuaEvaluationSessionData>();

                if (evaluationSession != null)
                {
                    evaluationSession.callInfoDataMap.Clear();
                    evaluationSession.functionDataMap.Clear();
                }
            }
        }

        void IDkmLanguageExpressionEvaluator.EvaluateExpression(DkmInspectionContext inspectionContext, DkmWorkList workList, DkmLanguageExpression expression, DkmStackWalkFrame stackFrame, DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine)
        {
            log.Debug($"IDkmLanguageExpressionEvaluator.EvaluateExpression begin (session {inspectionContext.InspectionSession.UniqueId})");

            var process = stackFrame.Process;

            // Load frame data from instruction
            var instructionAddress = stackFrame.InstructionAddress as DkmCustomInstructionAddress;

            Debug.Assert(instructionAddress != null);

            var frameData = new LuaFrameData();

            if (!frameData.ReadFrom(instructionAddress.AdditionalData.ToArray()))
            {
                log.Error($"IDkmLanguageExpressionEvaluator.EvaluateExpression failure (no frame data)");

                completionRoutine(new DkmEvaluateExpressionAsyncResult(DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, expression.Text, expression.Text, "Missing function frame data", DkmEvaluationResultFlags.Invalid, null)));
                return;
            }

            GetEvaluationSessionData(process, inspectionContext.InspectionSession, frameData, out LuaFunctionCallInfoData callInfoData, out LuaFunctionData functionData, out LuaClosureData closureData);

            ExpressionEvaluation evaluation = new ExpressionEvaluation(process, stackFrame, inspectionContext.InspectionSession, functionData, callInfoData.stackBaseAddress, closureData);

            bool ideDisplayFormat = false;
            string expressionText = expression.Text;

            if (expressionText.StartsWith("```"))
            {
                ideDisplayFormat = true;
                expressionText = expressionText.Substring(3);
            }

            bool allowSideEffects = !inspectionContext.EvaluationFlags.HasFlag(DkmEvaluationFlags.NoSideEffects);

            var result = evaluation.Evaluate(expressionText, allowSideEffects);

            if (result as LuaValueDataError != null)
            {
                var resultAsError = result as LuaValueDataError;

                log.Warning($"IDkmLanguageExpressionEvaluator.EvaluateExpression failure (error result)");

                if (resultAsError.stoppedOnSideEffect)
                    completionRoutine(new DkmEvaluateExpressionAsyncResult(DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, expressionText, expressionText, resultAsError.value, DkmEvaluationResultFlags.UnflushedSideEffects, null)));
                else
                    completionRoutine(new DkmEvaluateExpressionAsyncResult(DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, expressionText, expressionText, resultAsError.value, DkmEvaluationResultFlags.Invalid, null)));

                return;
            }

            // If result is an 'l-value' re-evaluate as a Lua value at address
            if (result.originalAddress != 0 && ideDisplayFormat == false)
            {
                log.Debug($"IDkmLanguageExpressionEvaluator.EvaluateExpression completed (l-value)");

                if (result as LuaValueDataExternalFunction != null)
                {
                    var value = result as LuaValueDataExternalFunction;

                    completionRoutine(new DkmEvaluateExpressionAsyncResult(EvaluationHelpers.EvaluateCppValueAtAddress(inspectionContext, stackFrame, expressionText, "void*", value.targetAddress, true)));
                }

                if (result as LuaValueDataExternalClosure != null)
                {
                    var value = result as LuaValueDataExternalClosure;

                    completionRoutine(new DkmEvaluateExpressionAsyncResult(EvaluationHelpers.EvaluateCppValueAtAddress(inspectionContext, stackFrame, expressionText, "void*", value.value.functionAddress, true)));
                }

                DkmEvaluationResultFlags resultFlags = result.evaluationFlags & (DkmEvaluationResultFlags.UnflushedSideEffects | DkmEvaluationResultFlags.SideEffect);

                completionRoutine(new DkmEvaluateExpressionAsyncResult(EvaluationHelpers.EvaluateDataAtLuaValue(inspectionContext, stackFrame, expressionText, expressionText, result, resultFlags, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None)));
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

                var arrayElementCount = resultAsTable.value.GetArrayElementCount(process);
                var nodeElementCount = resultAsTable.value.GetNodeElementCount(process);

                if (arrayElementCount == 0 && nodeElementCount == 0 && !resultAsTable.value.HasMetaTable())
                    result.evaluationFlags &= ~DkmEvaluationResultFlags.Expandable;

                if (arrayElementCount != 0 && nodeElementCount != 0)
                    resultStr = $"[{arrayElementCount} element(s) and {nodeElementCount} key(s)]";
                else if (arrayElementCount != 0)
                    resultStr = $"[{arrayElementCount} element(s)]";
                else if (nodeElementCount != 0)
                    resultStr = $"[{nodeElementCount} key(s)]";
                else if (resultAsTable.value.HasMetaTable())
                    resultStr = "[metatable]";
                else
                    resultStr = "[]";
            }

            var dataItem = new LuaEvaluationDataItem
            {
                address = result.originalAddress,
                type = type,
                fullName = expression.Text,
                luaValueData = result
            };

            // Special result format to parse into components on IDE side (EnvDTE.Expression doesn't get Type and Name from DkmSuccessEvaluationResult)
            if (ideDisplayFormat)
            {
                resultStr = $"{type}```{expressionText}```{resultStr}";

                result.evaluationFlags &= ~DkmEvaluationResultFlags.Expandable;
            }

            completionRoutine(new DkmEvaluateExpressionAsyncResult(DkmSuccessEvaluationResult.Create(inspectionContext, stackFrame, expressionText, expressionText, result.evaluationFlags, resultStr, null, type, category, accessType, storageType, typeModifiers, dataAddress, null, null, dataItem)));

            log.Debug($"IDkmLanguageExpressionEvaluator.EvaluateExpression completed");
        }

        DkmEvaluationResult GetNativeTypePseudoMember(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string type, ulong address)
        {
            // Create pseudo expandable node that will evaluate C++ value on expansion (since it's expensive)
            var dataItem = new LuaNativeTypeEnumData
            {
                expression = $"({type}*)0x{address:x}",
                type = type,
                address = address
            };

            return DkmSuccessEvaluationResult.Create(inspectionContext, stackFrame, "[native]", $"({type}*)0x{address:x}", DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable, $"0x{address:x} {type}", null, "", DkmEvaluationResultCategory.Data, DkmEvaluationResultAccessType.Internal, DkmEvaluationResultStorageType.None, DkmEvaluationResultTypeModifierFlags.None, null, null, null, dataItem);
        }

        void IDkmLanguageExpressionEvaluator.GetChildren(DkmEvaluationResult result, DkmWorkList workList, int initialRequestSize, DkmInspectionContext inspectionContext, DkmCompletionRoutine<DkmGetChildrenAsyncResult> completionRoutine)
        {
            log.Debug($"IDkmLanguageExpressionEvaluator.GetChildren begin");

            var process = result.StackFrame.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

            var nativeTypeEnumData = result.GetDataItem<LuaNativeTypeEnumData>();

            if (nativeTypeEnumData != null)
            {
                var parentFrameData = result.StackFrame.Data.GetDataItem<LuaStackWalkFrameParentData>();

                int actualSize = 1;

                int finalInitialSize = initialRequestSize < actualSize ? initialRequestSize : actualSize;

                DkmEvaluationResult[] initialResults = new DkmEvaluationResult[finalInitialSize];

                if (initialResults.Length != 0)
                    initialResults[0] = EvaluationHelpers.ExecuteRawExpression(nativeTypeEnumData.expression, inspectionContext.InspectionSession, inspectionContext.Thread, parentFrameData.originalFrame, inspectionContext.Thread.Process.GetNativeRuntimeInstance(), DkmEvaluationFlags.None);

                var enumerator = DkmEvaluationResultEnumContext.Create(1, result.StackFrame, inspectionContext, nativeTypeEnumData);

                completionRoutine(new DkmGetChildrenAsyncResult(initialResults, enumerator));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetChildren success (C++ native type)");
                return;
            }

            var evalData = result.GetDataItem<LuaEvaluationDataItem>();

            // Shouldn't happen
            if (evalData == null)
            {
                log.Error($"IDkmLanguageExpressionEvaluator.GetChildren failure");

                completionRoutine(new DkmGetChildrenAsyncResult(new DkmEvaluationResult[0], DkmEvaluationResultEnumContext.Create(0, result.StackFrame, inspectionContext, null)));
                return;
            }

            if (evalData.luaValueData as LuaValueDataTable != null)
            {
                var value = evalData.luaValueData as LuaValueDataTable;

                int actualSize = value.value.GetArrayElementCount(process) + value.value.GetNodeElementCount(process);

                if (value.value.HasMetaTable())
                    actualSize += 1;

                int finalInitialSize = initialRequestSize < actualSize ? initialRequestSize : actualSize;

                DkmEvaluationResult[] initialResults = new DkmEvaluationResult[finalInitialSize];

                for (int i = 0; i < initialResults.Length; i++)
                    initialResults[i] = EvaluationHelpers.GetTableChildAtIndex(inspectionContext, result.StackFrame, result.FullName, value.value, i);

                var enumerator = DkmEvaluationResultEnumContext.Create(actualSize, result.StackFrame, inspectionContext, evalData);

                completionRoutine(new DkmGetChildrenAsyncResult(initialResults, enumerator));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetChildren success (table)");
                return;
            }

            if (evalData.luaValueData as LuaValueDataLuaFunction != null)
            {
                var value = evalData.luaValueData as LuaValueDataLuaFunction;

                int finalInitialSize = initialRequestSize < 1 ? initialRequestSize : 1;

                DkmEvaluationResult[] initialResults = new DkmEvaluationResult[finalInitialSize];

                if (initialResults.Length != 0)
                    initialResults[0] = EvaluationHelpers.GetLuaFunctionChildAtIndex(inspectionContext, result.StackFrame, result.FullName, value.value, 0);

                var enumerator = DkmEvaluationResultEnumContext.Create(1, result.StackFrame, inspectionContext, evalData);

                completionRoutine(new DkmGetChildrenAsyncResult(initialResults, enumerator));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetChildren success (lua_function)");
                return;
            }

            if (evalData.luaValueData as LuaValueDataExternalFunction != null)
            {
                var value = evalData.luaValueData as LuaValueDataExternalFunction;

                int finalInitialSize = initialRequestSize < 1 ? initialRequestSize : 1;

                DkmEvaluationResult[] initialResults = new DkmEvaluationResult[finalInitialSize];

                if (initialResults.Length != 0)
                    initialResults[0] = EvaluationHelpers.EvaluateCppValueAtAddress(inspectionContext, result.StackFrame, "[function]", "void*", value.targetAddress, true);

                var enumerator = DkmEvaluationResultEnumContext.Create(1, result.StackFrame, inspectionContext, evalData);

                completionRoutine(new DkmGetChildrenAsyncResult(initialResults, enumerator));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetChildren success (c_function)");
                return;
            }

            if (evalData.luaValueData as LuaValueDataExternalClosure != null)
            {
                var value = evalData.luaValueData as LuaValueDataExternalClosure;

                int finalInitialSize = initialRequestSize < 1 ? initialRequestSize : 1;

                DkmEvaluationResult[] initialResults = new DkmEvaluationResult[finalInitialSize];

                if (initialResults.Length != 0)
                    initialResults[0] = EvaluationHelpers.EvaluateCppValueAtAddress(inspectionContext, result.StackFrame, "[function]", "void*", value.value.functionAddress, true);

                var enumerator = DkmEvaluationResultEnumContext.Create(1, result.StackFrame, inspectionContext, evalData);

                completionRoutine(new DkmGetChildrenAsyncResult(initialResults, enumerator));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetChildren success (c_closure)");
                return;
            }

            if (evalData.luaValueData as LuaValueDataUserData != null)
            {
                var value = evalData.luaValueData as LuaValueDataUserData;

                value.value.LoadMetaTable(process);

                if (value.value.metaTable == null)
                {
                    log.Error($"IDkmLanguageExpressionEvaluator.GetChildren failure (no user data metatable)");

                    completionRoutine(new DkmGetChildrenAsyncResult(new DkmEvaluationResult[0], DkmEvaluationResultEnumContext.Create(0, result.StackFrame, inspectionContext, null)));
                    return;
                }

                var parentFrameData = result.StackFrame.Data.GetDataItem<LuaStackWalkFrameParentData>();

                string nativeTypeName = null;

                if (parentFrameData != null)
                    nativeTypeName = value.value.GetNativeType(process);

                int actualSize = value.value.metaTable.GetArrayElementCount(process) + value.value.metaTable.GetNodeElementCount(process) + (nativeTypeName != null ? 1 : 0);

                int finalInitialSize = initialRequestSize < actualSize ? initialRequestSize : actualSize;

                DkmEvaluationResult[] initialResults = new DkmEvaluationResult[finalInitialSize];

                for (int i = 0; i < initialResults.Length; i++)
                {
                    int index = i;

                    if (nativeTypeName != null)
                    {
                        if (index == 0)
                        {
                            initialResults[i] = GetNativeTypePseudoMember(inspectionContext, result.StackFrame, nativeTypeName, value.value.pointerAtValueStart);
                            continue;
                        }

                        index -= 1;
                    }

                    initialResults[i] = EvaluationHelpers.GetTableChildAtIndex(inspectionContext, result.StackFrame, result.FullName, value.value.metaTable, index);
                }

                var enumerator = DkmEvaluationResultEnumContext.Create(actualSize, result.StackFrame, inspectionContext, evalData);

                completionRoutine(new DkmGetChildrenAsyncResult(initialResults, enumerator));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetChildren success (table)");
                return;
            }

            log.Error($"IDkmLanguageExpressionEvaluator.GetChildren failure (unexpected)");

            // Shouldn't happen
            completionRoutine(new DkmGetChildrenAsyncResult(new DkmEvaluationResult[0], DkmEvaluationResultEnumContext.Create(0, result.StackFrame, inspectionContext, null)));
        }

        void IDkmLanguageExpressionEvaluator.GetFrameArguments(DkmInspectionContext inspectionContext, DkmWorkList workList, DkmStackWalkFrame frame, DkmCompletionRoutine<DkmGetFrameArgumentsAsyncResult> completionRoutine)
        {
            completionRoutine(new DkmGetFrameArgumentsAsyncResult(new DkmEvaluationResult[0]));
        }

        void IDkmLanguageExpressionEvaluator.GetFrameLocals(DkmInspectionContext inspectionContext, DkmWorkList workList, DkmStackWalkFrame stackFrame, DkmCompletionRoutine<DkmGetFrameLocalsAsyncResult> completionRoutine)
        {
            log.Debug($"IDkmLanguageExpressionEvaluator.GetFrameLocals begin");

            var process = stackFrame.Process;

            // Load frame data from instruction
            var instructionAddress = stackFrame.InstructionAddress as DkmCustomInstructionAddress;

            Debug.Assert(instructionAddress != null);

            var frameData = new LuaFrameData();

            if (!frameData.ReadFrom(instructionAddress.AdditionalData.ToArray()))
            {
                log.Error($"IDkmLanguageExpressionEvaluator.GetFrameLocals failure");

                completionRoutine(new DkmGetFrameLocalsAsyncResult(DkmEvaluationResultEnumContext.Create(0, stackFrame, inspectionContext, null)));
                return;
            }

            GetEvaluationSessionData(process, inspectionContext.InspectionSession, frameData, out LuaFunctionCallInfoData callInfoData, out LuaFunctionData functionData, out _);

            var frameLocalsEnumData = new LuaFrameLocalsEnumData
            {
                frameData = frameData,
                callInfo = callInfoData,
                function = functionData
            };

            int count = 1 + functionData.activeLocals.Count; // 1 pseudo variable for '[registry]' table

            // Add upvalue list for Lua functions
            if (callInfoData.func != null && callInfoData.func.extendedType == LuaExtendedType.LuaFunction)
                count += functionData.upvalues.Count;

            completionRoutine(new DkmGetFrameLocalsAsyncResult(DkmEvaluationResultEnumContext.Create(count, stackFrame, inspectionContext, frameLocalsEnumData)));

            log.Debug($"IDkmLanguageExpressionEvaluator.GetFrameLocals success");
        }

        void IDkmLanguageExpressionEvaluator.GetItems(DkmEvaluationResultEnumContext enumContext, DkmWorkList workList, int startIndex, int count, DkmCompletionRoutine<DkmEvaluationEnumAsyncResult> completionRoutine)
        {
            log.Debug($"IDkmLanguageExpressionEvaluator.GetItems begin");

            var process = enumContext.StackFrame.Process;

            var frameLocalsEnumData = enumContext.GetDataItem<LuaFrameLocalsEnumData>();

            if (frameLocalsEnumData != null)
            {
                var function = frameLocalsEnumData.function;

                function.ReadUpvalues(process);
                function.ReadLocals(process, frameLocalsEnumData.frameData.instructionPointer);

                // Visual Studio doesn't respect enumeration size for GetFrameLocals, so we need to limit it back
                var actualCount = 1 + function.activeLocals.Count;

                LuaClosureData closureData = null;

                if (frameLocalsEnumData.callInfo.func != null && frameLocalsEnumData.callInfo.func.extendedType == LuaExtendedType.LuaFunction)
                {
                    closureData = (frameLocalsEnumData.callInfo.func as LuaValueDataLuaFunction).value;

                    actualCount += function.upvalues.Count;
                }

                int finalCount = actualCount - startIndex;

                finalCount = finalCount < 0 ? 0 : (finalCount < count ? finalCount : count);

                var results = new DkmEvaluationResult[finalCount];

                for (int i = startIndex; i < startIndex + finalCount; i++)
                {
                    int index = i;

                    if (index == 0)
                    {
                        ulong address = frameLocalsEnumData.frameData.registryAddress;

                        string name = "[registry]";

                        if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                        {
                            LuaTableData target = new LuaTableData();

                            target.ReadFrom(process, address);

                            LuaValueDataTable envTable = new LuaValueDataTable()
                            {
                                baseType = LuaBaseType.Table,
                                extendedType = LuaExtendedType.Table,
                                evaluationFlags = DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable,
                                tagAddress = 0,
                                originalAddress = 0,
                                value = target,
                                targetAddress = address
                            };

                            results[i - startIndex] = EvaluationHelpers.EvaluateDataAtLuaValue(enumContext.InspectionContext, enumContext.StackFrame, name, name, envTable, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
                        }
                        else
                        {
                            results[i - startIndex] = EvaluationHelpers.EvaluateDataAtAddress(enumContext.InspectionContext, enumContext.StackFrame, name, name, address, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
                        }
                        continue;
                    }

                    index -= 1;

                    if (index < function.activeLocals.Count)
                    {
                        // Base stack contains arguments and locals that are live at the current instruction
                        ulong address = frameLocalsEnumData.callInfo.stackBaseAddress + (ulong)index * LuaHelpers.GetValueSize(process);

                        string name = function.activeLocals[index].name;

                        results[i - startIndex] = EvaluationHelpers.EvaluateDataAtAddress(enumContext.InspectionContext, enumContext.StackFrame, name, name, address, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
                        continue;
                    }

                    index -= function.activeLocals.Count;

                    if (index < function.upvalues.Count)
                    {
                        var upvalueData = closureData.ReadUpvalue(process, index, function.upvalueSize);

                        string name = function.upvalues[index].name;

                        if (upvalueData == null)
                            results[i - startIndex] = DkmFailedEvaluationResult.Create(enumContext.InspectionContext, enumContext.StackFrame, name, name, "[internal error: missing upvalue]", DkmEvaluationResultFlags.Invalid, null);
                        else
                            results[i - startIndex] = EvaluationHelpers.EvaluateDataAtLuaValue(enumContext.InspectionContext, enumContext.StackFrame, name, name, upvalueData.value, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);

                        continue;
                    }
                }

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetItems success (locals)");
                return;
            }

            var nativeTypeEnumData = enumContext.GetDataItem<LuaNativeTypeEnumData>();

            if (nativeTypeEnumData != null)
            {
                Debug.Assert(startIndex == 0);
                Debug.Assert(count == 1);

                var parentFrameData = enumContext.StackFrame.Data.GetDataItem<LuaStackWalkFrameParentData>();

                var results = new DkmEvaluationResult[1];

                results[0] = EvaluationHelpers.ExecuteRawExpression(nativeTypeEnumData.expression, enumContext.InspectionSession, enumContext.InspectionContext.Thread, parentFrameData.originalFrame, enumContext.InspectionContext.Thread.Process.GetNativeRuntimeInstance(), DkmEvaluationFlags.None);

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetItems success (C++ native type)");
                return;
            }

            var evalData = enumContext.GetDataItem<LuaEvaluationDataItem>();

            // Shouldn't happen
            if (evalData == null)
            {
                log.Error($"IDkmLanguageExpressionEvaluator.GetItems failure");

                completionRoutine(new DkmEvaluationEnumAsyncResult(new DkmEvaluationResult[0]));
                return;
            }

            if (evalData.luaValueData as LuaValueDataTable != null)
            {
                var value = evalData.luaValueData as LuaValueDataTable;

                var results = new DkmEvaluationResult[count];

                for (int i = startIndex; i < startIndex + count; i++)
                    results[i - startIndex] = EvaluationHelpers.GetTableChildAtIndex(enumContext.InspectionContext, enumContext.StackFrame, evalData.fullName, value.value, i);

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetItems success (table)");
                return;
            }

            if (evalData.luaValueData as LuaValueDataLuaFunction != null)
            {
                var value = evalData.luaValueData as LuaValueDataLuaFunction;

                var results = new DkmEvaluationResult[count];

                for (int i = startIndex; i < startIndex + count; i++)
                {
                    if (i == 0)
                        results[i - startIndex] = EvaluationHelpers.GetLuaFunctionChildAtIndex(enumContext.InspectionContext, enumContext.StackFrame, evalData.fullName, value.value, 0);
                }

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetItems success (lua_function)");
                return;
            }

            if (evalData.luaValueData as LuaValueDataExternalFunction != null)
            {
                var value = evalData.luaValueData as LuaValueDataExternalFunction;

                var results = new DkmEvaluationResult[count];

                for (int i = startIndex; i < startIndex + count; i++)
                {
                    if (i == 0)
                        results[i - startIndex] = EvaluationHelpers.EvaluateCppValueAtAddress(enumContext.InspectionContext, enumContext.StackFrame, "[function]", "void*", value.targetAddress, true);
                }

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetItems success (c_function)");
                return;
            }

            if (evalData.luaValueData as LuaValueDataExternalClosure != null)
            {
                var value = evalData.luaValueData as LuaValueDataExternalClosure;

                var results = new DkmEvaluationResult[count];

                for (int i = startIndex; i < startIndex + count; i++)
                {
                    if (i == 0)
                        results[i - startIndex] = EvaluationHelpers.EvaluateCppValueAtAddress(enumContext.InspectionContext, enumContext.StackFrame, "[function]", "void*", value.value.functionAddress, true);
                }

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetItems success (c_closure)");
                return;
            }

            if (evalData.luaValueData as LuaValueDataUserData != null)
            {
                var value = evalData.luaValueData as LuaValueDataUserData;

                value.value.LoadMetaTable(process);

                var parentFrameData = enumContext.StackFrame.Data.GetDataItem<LuaStackWalkFrameParentData>();

                string nativeTypeName = null;

                if (parentFrameData != null)
                    nativeTypeName = value.value.GetNativeType(process);

                var results = new DkmEvaluationResult[count];

                for (int i = startIndex; i < startIndex + count; i++)
                {
                    int index = i;

                    if (nativeTypeName != null)
                    {
                        if (index == 0)
                        {
                            results[i - startIndex] = GetNativeTypePseudoMember(enumContext.InspectionContext, enumContext.StackFrame, nativeTypeName, value.value.pointerAtValueStart);
                            continue;
                        }

                        index -= 1;
                    }

                    results[i - startIndex] = EvaluationHelpers.GetTableChildAtIndex(enumContext.InspectionContext, enumContext.StackFrame, evalData.fullName, value.value.metaTable, index);
                }

                completionRoutine(new DkmEvaluationEnumAsyncResult(results));

                log.Debug($"IDkmLanguageExpressionEvaluator.GetItems success (table)");
                return;
            }

            completionRoutine(new DkmEvaluationEnumAsyncResult(new DkmEvaluationResult[0]));

            log.Error($"IDkmLanguageExpressionEvaluator.GetItems failure (empty)");
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
            log.Debug($"IDkmLanguageInstructionDecoder.SetValueAsString (session {result.InspectionSession.UniqueId})");

            var evalData = result.GetDataItem<LuaEvaluationDataItem>();

            if (evalData == null)
            {
                errorText = "Missing evaluation data";
                return;
            }

            var process = result.StackFrame.Process;
            var address = evalData.luaValueData.originalAddress;

            if (evalData.luaValueData.originalAddress == 0)
            {
                errorText = "Result value doesn't have an address in memory";
                return;
            }

            // Clear memory cache inside inspection session to refresh changed values
            ClearEvaluationSessionData(result.InspectionSession);

            if (evalData.luaValueData as LuaValueDataBool != null)
            {
                if (value == "true")
                {
                    if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                    {
                        LuaHelpers.TryWriteValue(process, result.StackFrame, result.InspectionSession, evalData.luaValueData.tagAddress, address, new LuaValueDataBool(true), out errorText);
                        return;
                    }

                    if (LuaHelpers.luaVersion == 504 && evalData.luaValueData.tagAddress != 0)
                    {
                        if (!DebugHelpers.TryWriteIntVariable(process, evalData.luaValueData.tagAddress, (int)LuaExtendedType.BooleanTrue))
                        {
                            errorText = "Failed to modify target process memory";
                            return;
                        }
                    }

                    if (!DebugHelpers.TryWriteIntVariable(process, address, 1))
                        errorText = "Failed to modify target process memory";
                    else
                        errorText = null;

                    return;
                }
                else if (value == "false")
                {
                    if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                    {
                        LuaHelpers.TryWriteValue(process, result.StackFrame, result.InspectionSession, evalData.luaValueData.tagAddress, address, new LuaValueDataBool(false), out errorText);
                        return;
                    }

                    if (LuaHelpers.luaVersion == 504 && evalData.luaValueData.tagAddress != 0)
                    {
                        if (!DebugHelpers.TryWriteIntVariable(process, evalData.luaValueData.tagAddress, (int)LuaExtendedType.Boolean))
                        {
                            errorText = "Failed to modify target process memory";
                            return;
                        }
                    }

                    if (!DebugHelpers.TryWriteIntVariable(process, address, 0))
                        errorText = "Failed to modify target process memory";
                    else
                        errorText = null;

                    return;
                }
                else if (int.TryParse(value, out int intValue))
                {
                    if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                    {
                        LuaHelpers.TryWriteValue(process, result.StackFrame, result.InspectionSession, evalData.luaValueData.tagAddress, address, new LuaValueDataBool(intValue != 0), out errorText);
                        return;
                    }

                    if (LuaHelpers.luaVersion == 504 && evalData.luaValueData.tagAddress != 0)
                    {
                        if (!DebugHelpers.TryWriteIntVariable(process, evalData.luaValueData.tagAddress, (int)(intValue != 0 ? LuaExtendedType.BooleanTrue : LuaExtendedType.Boolean)))
                        {
                            errorText = "Failed to modify target process memory";
                            return;
                        }
                    }

                    if (!DebugHelpers.TryWriteIntVariable(process, address, intValue != 0 ? 1 : 0))
                        errorText = "Failed to modify target process memory";
                    else
                        errorText = null;

                    return;
                }

                errorText = "Failed to convert string to bool";
                return;
            }

            if (evalData.luaValueData as LuaValueDataLightUserData != null)
            {
                if (ulong.TryParse(value, out ulong ulongValue))
                {
                    if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                    {
                        LuaHelpers.TryWriteValue(process, result.StackFrame, result.InspectionSession, evalData.luaValueData.tagAddress, address, new LuaValueDataLightUserData(ulongValue), out errorText);
                        return;
                    }

                    if (!DebugHelpers.TryWritePointerVariable(process, address, ulongValue))
                        errorText = "Failed to modify target process memory";
                    else
                        errorText = null;

                    return;
                }

                errorText = "Failed to convert string to address";
                return;
            }

            if (evalData.luaValueData as LuaValueDataNumber != null)
            {
                if (LuaHelpers.HasIntegerNumberExtendedType() && (evalData.luaValueData as LuaValueDataNumber).extendedType == LuaHelpers.GetIntegerNumberExtendedType())
                {
                    if (int.TryParse(value, out int intValue))
                    {
                        if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                        {
                            LuaHelpers.TryWriteValue(process, result.StackFrame, result.InspectionSession, evalData.luaValueData.tagAddress, address, new LuaValueDataNumber(intValue), out errorText);
                            return;
                        }

                        if (!DebugHelpers.TryWriteIntVariable(process, address, intValue))
                            errorText = "Failed to modify target process memory";
                        else
                            errorText = null;

                        return;
                    }

                    errorText = "Failed to convert string to int";
                    return;
                }
                else
                {
                    if (double.TryParse(value, out double doubleValue))
                    {
                        if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                        {
                            LuaHelpers.TryWriteValue(process, result.StackFrame, result.InspectionSession, evalData.luaValueData.tagAddress, address, new LuaValueDataNumber(doubleValue), out errorText);
                            return;
                        }

                        if (!DebugHelpers.TryWriteDoubleVariable(process, address, doubleValue))
                            errorText = "Failed to modify target process memory";
                        else
                            errorText = null;

                        return;
                    }

                    errorText = "Failed to convert string to double";
                    return;
                }
            }

            if (evalData.luaValueData as LuaValueDataString != null)
            {
                if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                {
                    LuaHelpers.TryWriteValue(process, result.StackFrame, result.InspectionSession, evalData.luaValueData.tagAddress, address, new LuaValueDataString(value), out errorText);
                    return;
                }

                var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

                if (processData.scratchMemory == 0)
                    processData.scratchMemory = process.AllocateVirtualMemory(0, 4096, 0x3000, 0x04);

                if (processData.scratchMemory != 0)
                {
                    byte[] data = Encoding.UTF8.GetBytes(value);

                    if (data.Length >= 4096)
                    {
                        errorText = "String is too large to update";
                        return;
                    }

                    var parentFrameData = result.StackFrame.Data.GetDataItem<LuaStackWalkFrameParentData>();

                    if (!DebugHelpers.TryWriteRawBytes(process, processData.scratchMemory, data))
                    {
                        errorText = "Failed to write new string into the target process";
                        return;
                    }

                    var frame = parentFrameData.originalFrame;

                    ulong? registryAddress = EvaluationHelpers.TryEvaluateAddressExpression($"luaS_newlstr({parentFrameData.stateAddress}, {processData.scratchMemory}, {data.Length})", result.InspectionSession, frame.Thread, frame, DkmEvaluationFlags.None);

                    if (!registryAddress.HasValue)
                    {
                        errorText = "Failed to create Lua string value";
                        return;
                    }

                    if (!DebugHelpers.TryWritePointerVariable(process, evalData.luaValueData.originalAddress, registryAddress.Value))
                        errorText = "Failed to modify target process memory";
                    else
                        errorText = null;

                    return;
                }
            }

            errorText = "Missing evaluation data";
        }

        string IDkmLanguageInstructionDecoder.GetMethodName(DkmLanguageInstructionAddress languageInstructionAddress, DkmVariableInfoFlags argumentFlags)
        {
            log.Debug($"IDkmLanguageInstructionDecoder.GetMethodName begin");

            var process = languageInstructionAddress.Address.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

            var customInstructionAddress = languageInstructionAddress.Address as DkmCustomInstructionAddress;

            if (customInstructionAddress == null)
                return languageInstructionAddress.GetMethodName(argumentFlags);

            var addressEntityData = new LuaAddressEntityData();

            addressEntityData.ReadFrom(customInstructionAddress.EntityId.ToArray());

            if (addressEntityData.functionAddress == 0)
            {
                log.Debug($"IDkmLanguageInstructionDecoder.GetMethodName success (weak match)");

                return $"[{addressEntityData.source}:{addressEntityData.line}](...)";
            }

            var functionData = new LuaFunctionData();

            functionData.ReadFrom(process, addressEntityData.functionAddress);

            string argumentList = "";

            for (int i = 0; i < functionData.argumentCount; i++)
            {
                LuaLocalVariableData argument = new LuaLocalVariableData();

                argument.ReadFrom(process, functionData.localVariableDataAddress + (ulong)(i * LuaLocalVariableData.StructSize(process)));

                argumentList += (i == 0 ? "" : ", ") + argument.name;
            }

            lock (processData.symbolStore)
            {
                foreach (var state in processData.symbolStore.knownStates)
                {
                    string functionName = state.Value.FetchFunctionName(addressEntityData.functionAddress);

                    if (functionName != null)
                    {
                        log.Debug($"IDkmLanguageInstructionDecoder.GetMethodName success");

                        return $"{functionName}({argumentList})";
                    }
                }
            }

            log.Debug($"IDkmLanguageInstructionDecoder.GetMethodName success (no name)");

            return $"[{addressEntityData.source}:{addressEntityData.line}]({argumentList})";
        }

        DkmCompilerId IDkmSymbolCompilerIdQuery.GetCompilerId(DkmInstructionSymbol instruction, DkmInspectionSession inspectionSession)
        {
            return new DkmCompilerId(Guids.luaCompilerGuid, Guids.luaLanguageGuid);
        }

        DkmResolvedDocument[] IDkmSymbolDocumentCollectionQuery.FindDocuments(DkmModule module, DkmSourceFileId sourceFileId)
        {
            DkmCustomModuleInstance moduleInstance = module.GetModuleInstances().OfType<DkmCustomModuleInstance>().FirstOrDefault(el => el.Module.CompilerId.VendorId == Guids.luaCompilerGuid);

            if (moduleInstance == null)
                return module.FindDocuments(sourceFileId);

            log.Debug($"IDkmSymbolDocumentCollectionQuery.FindDocuments begin ({sourceFileId.DocumentName})");

            var process = moduleInstance.Process;
            var processData = process.GetDataItem<LuaLocalProcessData>();

            lock (processData.symbolStore)
            {
                foreach (var state in processData.symbolStore.knownStates)
                {
                    foreach (var source in state.Value.knownSources)
                    {
                        // Resolve script path if it's not resolved yet
                        if (source.Value.resolvedFileName == null)
                        {
                            var scriptSource = processData.symbolStore.FetchScriptSource(source.Key);

                            if (scriptSource?.resolvedFileName != null)
                                source.Value.resolvedFileName = scriptSource.resolvedFileName;
                            else
                                source.Value.resolvedFileName = TryFindSourcePath(process.Path, processData, source.Key, scriptSource?.scriptContent, true, out string loadStatus);

                            if (source.Value.resolvedFileName != null)
                                log.Debug($"IDkmSymbolDocumentCollectionQuery.FindDocuments Resolved {source.Value.sourceFileName} to {source.Value.resolvedFileName}");
                        }

                        var fileName = source.Value.resolvedFileName;

                        if (sourceFileId.DocumentName == fileName)
                        {
                            var dataItem = new LuaResolvedDocumentItem
                            {
                                source = source.Value
                            };

                            log.Debug($"IDkmSymbolDocumentCollectionQuery.FindDocuments success (known source '{source}')");

                            return new DkmResolvedDocument[1] { DkmResolvedDocument.Create(module, sourceFileId.DocumentName, null, DkmDocumentMatchStrength.FullPath, DkmResolvedDocumentWarning.None, false, dataItem) };
                        }
                    }
                }

                foreach (var state in processData.symbolStore.knownStates)
                {
                    foreach (var script in state.Value.knownScripts)
                    {
                        // Resolve script path if it's not resolved yet
                        if (script.Value.resolvedFileName == null)
                        {
                            // Check match based on hash
                            if (sourceFileId.SHA1HashPart != null && script.Value.sha1Hash != null)
                            {
                                int value0 = (int)(((uint)script.Value.sha1Hash[3] << 24) | ((uint)script.Value.sha1Hash[2] << 16) | ((uint)script.Value.sha1Hash[1] << 8) | (uint)script.Value.sha1Hash[0]);
                                int value1 = (int)(((uint)script.Value.sha1Hash[7] << 24) | ((uint)script.Value.sha1Hash[6] << 16) | ((uint)script.Value.sha1Hash[5] << 8) | (uint)script.Value.sha1Hash[4]);
                                int value2 = (int)(((uint)script.Value.sha1Hash[11] << 24) | ((uint)script.Value.sha1Hash[10] << 16) | ((uint)script.Value.sha1Hash[9] << 8) | (uint)script.Value.sha1Hash[8]);
                                int value3 = (int)(((uint)script.Value.sha1Hash[15] << 24) | ((uint)script.Value.sha1Hash[14] << 16) | ((uint)script.Value.sha1Hash[13] << 8) | (uint)script.Value.sha1Hash[12]);
                                int value4 = (int)(((uint)script.Value.sha1Hash[19] << 24) | ((uint)script.Value.sha1Hash[18] << 16) | ((uint)script.Value.sha1Hash[17] << 8) | (uint)script.Value.sha1Hash[16]);

                                if (sourceFileId.SHA1HashPart.Value.Value0 == value0 && sourceFileId.SHA1HashPart.Value.Value1 == value1 && sourceFileId.SHA1HashPart.Value.Value2 == value2 && sourceFileId.SHA1HashPart.Value.Value3 == value3 && sourceFileId.SHA1HashPart.Value.Value4 == value4)
                                {
                                    log.Debug($"IDkmSymbolDocumentCollectionQuery.FindDocuments Resolved {script.Value.sourceFileName} to {sourceFileId.DocumentName} based on SHA-1 hash");

                                    script.Value.resolvedFileName = sourceFileId.DocumentName;
                                }
                            }

                            if (script.Value.resolvedFileName == null)
                            {
                                script.Value.resolvedFileName = TryFindSourcePath(process.Path, processData, script.Key, script.Value.scriptContent, true, out string loadStatus);

                                if (script.Value.resolvedFileName != null)
                                    log.Debug($"IDkmSymbolDocumentCollectionQuery.FindDocuments Resolved {script.Value.sourceFileName} to {script.Value.resolvedFileName}");
                            }
                        }

                        var fileName = script.Value.resolvedFileName;

                        if (sourceFileId.DocumentName == fileName)
                        {
                            var dataItem = new LuaResolvedDocumentItem
                            {
                                script = script.Value
                            };

                            log.Debug($"IDkmSymbolDocumentCollectionQuery.FindDocuments success (known script '{script}')");

                            return new DkmResolvedDocument[1] { DkmResolvedDocument.Create(module, sourceFileId.DocumentName, null, DkmDocumentMatchStrength.FullPath, DkmResolvedDocumentWarning.None, false, dataItem) };
                        }
                    }
                }
            }

            log.Error($"IDkmSymbolDocumentCollectionQuery.FindDocuments failure {sourceFileId.DocumentName}");

            return module.FindDocuments(sourceFileId);
        }

        bool FindFunctionInstructionForLine(DkmProcess process, LuaFunctionData function, int startLine, int endLine, out LuaFunctionData targetFunction, out int targetInstructionPointer, out int targetLine)
        {
            // TODO: Reverse search in line map
            if (function.absLineInfoSize_5_4.HasValue)
            {
                targetFunction = null;
                targetInstructionPointer = 0;
                targetLine = 0;
                return false;
            }

            function.ReadLocalFunctions(process);
            function.ReadLineInfo(process);

            foreach (var localFunction in function.localFunctions)
            {
                if (FindFunctionInstructionForLine(process, localFunction, startLine, endLine, out targetFunction, out targetInstructionPointer, out targetLine))
                    return true;
            }

            // Check only first line in range
            int line = startLine;

            for (int instruction = 0; instruction < function.lineInfo.Length; instruction++)
            {
                if (function.lineInfo[instruction] == line)
                {
                    targetFunction = function;
                    targetInstructionPointer = instruction;
                    targetLine = line;
                    return true;
                }
            }

            targetFunction = null;
            targetInstructionPointer = 0;
            targetLine = 0;
            return false;
        }

        DkmInstructionSymbol[] IDkmSymbolDocumentSpanQuery.FindSymbols(DkmResolvedDocument resolvedDocument, DkmTextSpan textSpan, string text, out DkmSourcePosition[] symbolLocation)
        {
            log.Debug($"IDkmSymbolDocumentSpanQuery.FindSymbols begin");

            var documentData = DebugHelpers.GetOrCreateDataItem<LuaResolvedDocumentItem>(resolvedDocument);

            if (documentData == null)
            {
                log.Error($"IDkmSymbolDocumentSpanQuery.FindSymbols failure (no document data)");

                return resolvedDocument.FindSymbols(textSpan, text, out symbolLocation);
            }

            DkmCustomModuleInstance moduleInstance = resolvedDocument.Module.GetModuleInstances().OfType<DkmCustomModuleInstance>().FirstOrDefault(el => el.Module.CompilerId.VendorId == Guids.luaCompilerGuid);

            if (moduleInstance == null)
            {
                log.Error($"IDkmSymbolDocumentSpanQuery.FindSymbols failure (no module)");

                return resolvedDocument.FindSymbols(textSpan, text, out symbolLocation);
            }

            var process = moduleInstance.Process;
            var processData = process.GetDataItem<LuaLocalProcessData>();

            var scriptSymbols = documentData.script;

            if (documentData.source != null)
            {
                if (scriptSymbols == null)
                {
                    lock (processData.symbolStore)
                    {
                        scriptSymbols = processData.symbolStore.FetchScriptSource(documentData.source.sourceFileName);
                    }
                }

                foreach (var el in documentData.source.knownFunctions)
                {
                    if (FindFunctionInstructionForLine(process, el.Value, textSpan.StartLine, textSpan.EndLine, out LuaFunctionData luaFunctionData, out int instructionPointer, out int line))
                    {
                        var sourceFileId = DkmSourceFileId.Create(resolvedDocument.DocumentName, null, null, null);

                        var resultSpan = new DkmTextSpan(line, line, 0, 0);

                        symbolLocation = new DkmSourcePosition[1] { DkmSourcePosition.Create(sourceFileId, resultSpan) };

                        LuaAddressEntityData entityData = new LuaAddressEntityData
                        {
                            source = documentData.source.sourceFileName,
                            line = line,

                            functionAddress = luaFunctionData.originalAddress,
                            functionInstructionPointer = instructionPointer,
                        };

                        LuaBreakpointAdditionalData additionalData = new LuaBreakpointAdditionalData
                        {
                            source = documentData.source.sourceFileName,
                            line = line,
                        };

                        var entityDataBytes = entityData.Encode();
                        var additionalDataBytes = additionalData.Encode();

                        log.Debug($"IDkmSymbolDocumentSpanQuery.FindSymbols success (strong match '{entityData.source}' Line {entityData.line})");

                        return new DkmInstructionSymbol[1] { DkmCustomInstructionSymbol.Create(resolvedDocument.Module, Guids.luaRuntimeGuid, entityDataBytes, (ulong)((line << 16) + instructionPointer), additionalDataBytes) };
                    }
                }
            }

            if (scriptSymbols != null)
            {
                var sourceFileId = DkmSourceFileId.Create(resolvedDocument.DocumentName, null, null, null);

                var resultSpan = new DkmTextSpan(textSpan.StartLine, textSpan.StartLine, 0, 0);

                symbolLocation = new DkmSourcePosition[1] { DkmSourcePosition.Create(sourceFileId, resultSpan) };

                LuaAddressEntityData entityData = new LuaAddressEntityData
                {
                    source = scriptSymbols.sourceFileName,
                    line = textSpan.StartLine,

                    functionAddress = 0,
                    functionInstructionPointer = 0,
                };

                LuaBreakpointAdditionalData additionalData = new LuaBreakpointAdditionalData
                {
                    source = scriptSymbols.sourceFileName,
                    line = textSpan.StartLine,
                };

                var entityDataBytes = entityData.Encode();
                var additionalDataBytes = additionalData.Encode();

                log.Debug($"IDkmSymbolDocumentSpanQuery.FindSymbols success (weak match '{entityData.source}' Line {entityData.line})");

                return new DkmInstructionSymbol[1] { DkmCustomInstructionSymbol.Create(resolvedDocument.Module, Guids.luaRuntimeGuid, entityDataBytes, (ulong)((textSpan.StartLine << 16) + 0), additionalDataBytes) };
            }

            log.Error($"IDkmSymbolDocumentSpanQuery.FindSymbols failure (not found)");

            return resolvedDocument.FindSymbols(textSpan, text, out symbolLocation);
        }

        DkmCustomMessage GetLuaLocations(DkmProcess process, DkmNativeModuleInstance nativeModuleInstance)
        {
            DkmWorkerProcessConnection workerConnection = DkmWorkerProcessConnection.GetLocalSymbolsConnection();

            if (workerConnection != null)
                return DkmCustomMessage.Create(process.Connection, process, MessageToLocalWorker.guid, MessageToLocalWorker.fetchLuaSymbols, nativeModuleInstance.UniqueId.ToByteArray(), null, null, workerConnection).SendLower();

            return null;
        }

        void SendStatusMessage(DkmProcess process, int id, string content)
        {
            StatusTextMessage statusTextMessage = new StatusTextMessage
            {
                id = id,
                content = content
            };

            DkmCustomMessage msg = DkmCustomMessage.Create(process.Connection, process, Guid.Empty, MessageToVsService.setStatusText, statusTextMessage.Encode(), null);
            msg.SendToVsService(Guids.luaVsPackageComponentGuid, false);
        }

        void IDkmModuleInstanceLoadNotification.OnModuleInstanceLoad(DkmModuleInstance moduleInstance, DkmWorkList workList, DkmEventDescriptorS eventDescriptor)
        {
            log.Debug($"IDkmModuleInstanceLoadNotification.OnModuleInstanceLoad begin");

            var nativeModuleInstance = moduleInstance as DkmNativeModuleInstance;

            if (nativeModuleInstance != null)
            {
                var process = moduleInstance.Process;

#if DEBUG
                log.logPath = $"{Path.GetDirectoryName(process.Path)}\\lua_dkm_debug_log.txt";
#else
                if (releaseDebugLogs)
                {
                    log.logLevel = Log.LogLevel.Debug;

                    log.logPath = $"{Path.GetDirectoryName(process.Path)}\\lua_dkm_debug_log.txt";
                }
#endif

                var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

                var moduleName = nativeModuleInstance.FullName;

                if (moduleName != null && moduleName.EndsWith(".exe"))
                {
                    SendStatusMessage(process, 1, $"Lua: ---");
                    SendStatusMessage(process, 2, $"Attach: ---");

                    processData.versionNotificationSent = false;
                }

                if (moduleName != null && (moduleName.EndsWith(".exe") || Path.GetFileName(moduleName).IndexOf("lua", StringComparison.InvariantCultureIgnoreCase) != -1) &&
                    processData.moduleWithLoadedLua == null)
                {
                    // Request the RemoteComponent to create the runtime and a module
                    DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.createRuntime, null, null).SendLower();

                    if (process.LivePart == null)
                    {
                        log.Debug($"Process is not live, do not attach to Lua");

                        return;
                    }

                    if (!attachOnLaunch)
                    {
                        log.Warning("Lua attach on launch is disabled, skip search for Lua");

                        return;
                    }

                    log.Debug("Check if Lua library is loaded");

                    DkmCustomMessage luaLocations = null;

                    try
                    {
                        // Only available from VS 2019
                        luaLocations = GetLuaLocations(process, nativeModuleInstance);

                        UpdateEvaluationHelperWorkerConnection(process);
                    }
                    catch (Exception ex)
                    {
                        log.Debug("Local symbols connection is not available: " + ex.ToString());
                    }

                    if (luaLocations != null)
                    {
                        log.Debug("Found Lua library (from a worker component)");

                        var data = new LuaLocationsMessage();

                        data.ReadFrom(luaLocations.Parameter1 as byte[]);

                        processData.luaLocations = data;

                        processData.moduleWithLoadedLua = nativeModuleInstance;

                        processData.executionStartAddress = processData.luaLocations.luaExecuteAtStart;
                        processData.executionEndAddress = processData.luaLocations.luaExecuteAtEnd;
                    }
                    else
                    {
                        if (AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "lua_newstate", out string errorA).GetValueOrDefault(0) != 0 || AttachmentHelpers.TryGetFunctionAddress(nativeModuleInstance, "luaL_newstate", out string errorB).GetValueOrDefault(0) != 0)
                        {
                            log.Debug("Found Lua library (from an IDE component)");

                            processData.moduleWithLoadedLua = nativeModuleInstance;

                            processData.executionStartAddress = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(processData.moduleWithLoadedLua, "luaV_execute", out _).GetValueOrDefault(0);
                            processData.executionEndAddress = AttachmentHelpers.TryGetFunctionAddressAtDebugEnd(processData.moduleWithLoadedLua, "luaV_execute", out _).GetValueOrDefault(0);
                        }
                        else
                        {
                            if (errorA != null && errorA.Length != 0)
                                log.Error("Failed to find lua_newstate with: " + errorA);

                            if (errorB != null && errorB.Length != 0)
                                log.Error("Failed to find luaL_newstate with: " + errorB);

                            log.Warning("Failed to find Lua library");
                        }
                    }
                }

                if (nativeModuleInstance.FullName != null && nativeModuleInstance.FullName.EndsWith("kernel32.dll"))
                {
                    log.Debug("Found kernel32 library");

                    processData.loadLibraryAddress = DebugHelpers.FindFunctionAddress(process.GetNativeRuntimeInstance(), "LoadLibraryA");
                }

                if (nativeModuleInstance.FullName != null && (nativeModuleInstance.FullName.EndsWith("LuaDebugHelper_x86.dll") || nativeModuleInstance.FullName.EndsWith("LuaDebugHelper_x64.dll")))
                {
                    log.Debug("Found Lua debugger helper library");

                    var variableAddress = nativeModuleInstance.FindExportName("luaHelperIsInitialized", IgnoreDataExports: false);

                    if (variableAddress != null)
                    {
                        processData.helperWorkingDirectoryAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperWorkingDirectory");
                        processData.helperHookFunctionAddress_5_1 = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "LuaHelperHook_5_1");
                        processData.helperHookFunctionAddress_5_2 = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "LuaHelperHook_5_2");
                        processData.helperHookFunctionAddress_5_3 = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "LuaHelperHook_5_3");
                        processData.helperHookFunctionAddress_5_4 = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "LuaHelperHook_5_4");
                        processData.helperHookFunctionAddress_luajit = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "LuaHelperHook_luajit");

                        processData.helperBreakCountAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperBreakCount");
                        processData.helperBreakDataAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperBreakData");
                        processData.helperBreakHitIdAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperBreakHitId");
                        processData.helperBreakHitLuaStateAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperBreakHitLuaStateAddress");
                        processData.helperBreakSourcesAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperBreakSources");

                        processData.helperStepOverAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperStepOver");
                        processData.helperStepIntoAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperStepInto");
                        processData.helperStepOutAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperStepOut");
                        processData.helperSkipDepthAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperSkipDepth");
                        processData.helperStackDepthAtCall = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperStackDepthAtCall");
                        processData.helperAsyncBreakCodeAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperAsyncBreakCode");
                        processData.helperAsyncBreakDataAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperAsyncBreakData");
                        
                        // Hooks for compatibility mode
                        processData.helperHookFunctionAddress_5_234_compat = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "LuaHelperHook_5_234_compat");

                        processData.helperCompatLuaDebugEventOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatLuaDebugEventOffset");
                        processData.helperCompatLuaDebugCurrentLineOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatLuaDebugCurrentLineOffset");
                        processData.helperCompatLuaStateCallInfoOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatLuaStateCallInfoOffset");
                        processData.helperCompatCallInfoFunctionOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatCallInfoFunctionOffset");
                        processData.helperCompatTaggedValueTypeTagOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatTaggedValueTypeTagOffset");
                        processData.helperCompatTaggedValueValueOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatTaggedValueValueOffset");
                        processData.helperCompatLuaClosureProtoOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatLuaClosureProtoOffset");
                        processData.helperCompatLuaFunctionSourceOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatLuaFunctionSourceOffset");
                        processData.helperCompatStringContentOffset = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperCompatStringContentOffset");

                        processData.helperLuajitGetInfoAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperLuajitGetInfoAddress");
                        processData.helperLuajitGetStackAddress = AttachmentHelpers.FindVariableAddress(nativeModuleInstance, "luaHelperLuajitGetStackAddress");

                        // Breakpoints for calls into debugger
                        processData.breakpointLuaHelperBreakpointHit = AttachmentHelpers.CreateHelperFunctionBreakpoint(nativeModuleInstance, "OnLuaHelperBreakpointHit").GetValueOrDefault(Guid.Empty);
                        processData.breakpointLuaHelperStepComplete = AttachmentHelpers.CreateHelperFunctionBreakpoint(nativeModuleInstance, "OnLuaHelperStepComplete").GetValueOrDefault(Guid.Empty);
                        processData.breakpointLuaHelperStepInto = AttachmentHelpers.CreateHelperFunctionBreakpoint(nativeModuleInstance, "OnLuaHelperStepInto").GetValueOrDefault(Guid.Empty);
                        processData.breakpointLuaHelperStepOut = AttachmentHelpers.CreateHelperFunctionBreakpoint(nativeModuleInstance, "OnLuaHelperStepOut").GetValueOrDefault(Guid.Empty);
                        processData.breakpointLuaHelperAsyncBreak = AttachmentHelpers.CreateHelperFunctionBreakpoint(nativeModuleInstance, "OnLuaHelperAsyncBreak").GetValueOrDefault(Guid.Empty);

                        // TODO: check all data

                        processData.helperStartAddress = nativeModuleInstance.BaseAddress;
                        processData.helperEndAddress = processData.helperStartAddress + nativeModuleInstance.Size;

                        // Tell remote component about helper library locations
                        var data = new HelperLocationsMessage
                        {
                            helperBreakCountAddress = processData.helperBreakCountAddress,
                            helperBreakDataAddress = processData.helperBreakDataAddress,
                            helperBreakHitIdAddress = processData.helperBreakHitIdAddress,
                            helperBreakHitLuaStateAddress = processData.helperBreakHitLuaStateAddress,
                            helperBreakSourcesAddress = processData.helperBreakSourcesAddress,

                            helperStepOverAddress = processData.helperStepOverAddress,
                            helperStepIntoAddress = processData.helperStepIntoAddress,
                            helperStepOutAddress = processData.helperStepOutAddress,
                            helperSkipDepthAddress = processData.helperSkipDepthAddress,
                            helperStackDepthAtCallAddress = processData.helperStackDepthAtCall,
                            helperAsyncBreakCodeAddress = processData.helperAsyncBreakCodeAddress,

                            breakpointLuaHelperBreakpointHit = processData.breakpointLuaHelperBreakpointHit,
                            breakpointLuaHelperStepComplete = processData.breakpointLuaHelperStepComplete,
                            breakpointLuaHelperStepInto = processData.breakpointLuaHelperStepInto,
                            breakpointLuaHelperStepOut = processData.breakpointLuaHelperStepOut,
                            breakpointLuaHelperAsyncBreak = processData.breakpointLuaHelperAsyncBreak,

                            helperStartAddress = processData.helperStartAddress,
                            helperEndAddress = processData.helperEndAddress,

                            executionStartAddress = processData.executionStartAddress,
                            executionEndAddress = processData.executionEndAddress,
                        };

                        var message = DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.luaHelperDataLocations, data.Encode(), null);

                        message.SendLower();

                        // Handle Lua helper initialization sequence
                        var initialized = DebugHelpers.ReadIntVariable(process, variableAddress.CPUInstructionPart.InstructionPointer);

                        if (initialized.HasValue)
                        {
                            log.Debug($"Found helper library init flag at 0x{variableAddress.CPUInstructionPart.InstructionPointer:x}");

                            if (initialized.Value == 0)
                            {
                                log.Debug("Helper hasn't been initialized");

                                var breakpointId = AttachmentHelpers.CreateHelperFunctionBreakpoint(nativeModuleInstance, "OnLuaHelperInitialized");

                                if (breakpointId.HasValue)
                                {
                                    SendStatusMessage(process, 2, $"Attach: waiting for initialization");
                                    log.Debug("Waiting for helper library initialization");

                                    processData.breakpointLuaHelperInitialized = breakpointId.Value;

                                    processData.helperInitializationWaitActive = true;
                                }
                                else
                                {
                                    SendStatusMessage(process, 2, $"Attach: not initialized, failed to wait");
                                    log.Error("Failed to set breakpoint at 'OnLuaHelperInitialized'");

                                    processData.helperFailed = true;
                                }
                            }
                            else if (initialized.Value == 1)
                            {
                                SendStatusMessage(process, 2, $"Attach: initialized");
                                log.Debug("Helper has been initialized");

                                processData.helperInitialized = true;

                                if (processData.helperWorkingDirectoryAddress != 0)
                                {
                                    processData.workingDirectoryRequested = true;
                                    processData.workingDirectory = DebugHelpers.ReadStringVariable(process, processData.helperWorkingDirectoryAddress, 1024);

                                    if (processData.workingDirectory != null && processData.workingDirectory.Length != 0)
                                    {
                                        log.Debug($"Found process working directory {processData.workingDirectory}");

                                        LoadConfigurationFile(process, processData);
                                    }
                                    else
                                    {
                                        log.Error("Failed to get process working directory'");
                                    }
                                }
                            }
                        }
                        else
                        {
                            SendStatusMessage(process, 2, $"Attach: failed to read initialization state");
                            processData.helperFailed = true;
                        }
                    }
                    else
                    {
                        SendStatusMessage(process, 2, $"Attach: failed to find initializtion state");
                        log.Error("Failed to find 'luaHelperIsInitialized' in debug helper library");

                        processData.helperFailed = true;
                    }

                    if (processData.helperInitializationWaitUsed && !processData.helperInitializationWaitActive)
                    {
                        log.Debug("Lua thread is already suspended but the Helper initialization wait wasn't activated");

                        if (processData.helperInitializationSuspensionThread != null)
                        {
                            log.Debug("Resuming Lua thread");

                            processData.helperInitializationSuspensionThread.Resume(true);

                            processData.helperInitializationSuspensionThread = null;
                        }
                    }
                }

                if (processData.moduleWithLoadedLua != null && processData.loadLibraryAddress != 0)
                {
                    // Check if already injected
                    if (processData.helperInjectRequested)
                        return;

                    processData.helperInjectRequested = true;

                    // Track Lua state initialization (breakpoint at the start of the function)
                    if (processData.luaLocations != null)
                    {
                        processData.breakpointLuaInitialization = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "lua_newstate", "initialization mark", processData.luaLocations.luaNewStateAtStart).GetValueOrDefault(Guid.Empty);
                    }
                    else
                    {
                        processData.breakpointLuaInitialization = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "lua_newstate", "initialization mark", out _).GetValueOrDefault(Guid.Empty);

                        if (processData.breakpointLuaInitialization == Guid.Empty)
                            processData.breakpointLuaInitialization = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaL_newstate", "initialization mark (outer)", out _).GetValueOrDefault(Guid.Empty);
                    }

                    // Track Lua state creation (breakpoint at the end of the function)
                    if (processData.luaLocations != null)
                    {
                        processData.breakpointLuaThreadCreate = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "lua_newstate", "Lua thread creation", processData.luaLocations.luaNewStateAtEnd).GetValueOrDefault(Guid.Empty);
                    }
                    else
                    {
                        processData.breakpointLuaThreadCreate = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugEnd(process, processData.moduleWithLoadedLua, "lua_newstate", "Lua thread creation", out _).GetValueOrDefault(Guid.Empty);

                        if (processData.breakpointLuaThreadCreate == Guid.Empty)
                            processData.breakpointLuaThreadCreate = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugEnd(process, processData.moduleWithLoadedLua, "luaL_newstate", "Lua thread creation (outer)", out _).GetValueOrDefault(Guid.Empty);
                    }

                    // Track Lua state destruction (breakpoint at the start of the function)
                    if (processData.luaLocations != null)
                        processData.breakpointLuaThreadDestroy = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "lua_close", "Lua thread destruction", processData.luaLocations.luaClose).GetValueOrDefault(Guid.Empty);
                    else
                        processData.breakpointLuaThreadDestroy = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "lua_close", "Lua thread destruction", out _).GetValueOrDefault(Guid.Empty);

                    if (processData.luaLocations != null)
                        processData.breakpointLuaThreadDestroyInternal = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "close_state", "Lua thread destruction (internal)", processData.luaLocations.closeState).GetValueOrDefault(Guid.Empty);
                    else
                        processData.breakpointLuaThreadDestroyInternal = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "close_state", "Lua thread destruction (internal)", out _).GetValueOrDefault(Guid.Empty);

                    // Track Lua scripts loaded from files
                    if (processData.luaLocations != null)
                    {
                        if (processData.luaLocations.luaLoadFileEx != 0)
                        {
                            processData.breakpointLuaFileLoaded = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaL_loadfilex", "Lua script load from file", processData.luaLocations.luaLoadFileEx).GetValueOrDefault(Guid.Empty);
                        }
                        else
                        {
                            processData.breakpointLuaFileLoaded = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaL_loadfile", "Lua script load from file", processData.luaLocations.luaLoadFile).GetValueOrDefault(Guid.Empty);

                            processData.breakpointLuaFileLoadedSolCompat = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "kp_compat53L_loadfilex", "Lua script load from file", processData.luaLocations.solCompatLoadFileEx).GetValueOrDefault(Guid.Empty);
                        }
                    }
                    else
                    {
                        processData.breakpointLuaFileLoaded = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaL_loadfilex", "Lua script load from file", out _).GetValueOrDefault(Guid.Empty);

                        if (processData.breakpointLuaFileLoaded == Guid.Empty)
                        {
                            processData.breakpointLuaFileLoaded = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaL_loadfile", "Lua script load from file", out _).GetValueOrDefault(Guid.Empty);

                            processData.breakpointLuaFileLoadedSolCompat = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "kp_compat53L_loadfilex", "Lua script load from file", out _).GetValueOrDefault(Guid.Empty);
                        }
                    }

                    // Track Lua scripts loaded from buffers
                    if (processData.luaLocations != null)
                    {
                        if (processData.luaLocations.luaLoadBufferEx != 0)
                            processData.breakpointLuaBufferLoaded = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaL_loadbufferx", "Lua script load from buffer", processData.luaLocations.luaLoadBufferEx).GetValueOrDefault(Guid.Empty);
                        else
                            processData.breakpointLuaBufferLoaded = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaL_loadbuffer", "Lua script load from file", processData.luaLocations.luaLoadBufferAtStart).GetValueOrDefault(Guid.Empty);
                    }
                    else
                    {
                        processData.breakpointLuaBufferLoaded = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaL_loadbufferx", "Lua script load from buffer", out _).GetValueOrDefault(Guid.Empty);

                        if (processData.breakpointLuaBufferLoaded == Guid.Empty)
                            processData.breakpointLuaBufferLoaded = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaL_loadbuffer", "Lua script load from file", out _).GetValueOrDefault(Guid.Empty);
                    }

                    if (processData.luaLocations != null)
                        processData.breakpointLuaLoad = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "lua_load", "Lua script load", processData.luaLocations.luaLoad).GetValueOrDefault(Guid.Empty);
                    else
                        processData.breakpointLuaLoad = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "lua_load", "Lua script load", out _).GetValueOrDefault(Guid.Empty);

                    // Track runtime errors using two breakpoints, first will notify us that the following throw call is a runtime error instead of some other user error (we also capture break address to filter Lua frames in Call Stack filter)
                    if (processData.luaLocations != null)
                        processData.breakpointLuaBreakError = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaB_error", "Lua break error", processData.luaLocations.luaError).GetValueOrDefault(Guid.Empty);
                    else
                        processData.breakpointLuaBreakError = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaB_error", "Lua break error", out _).GetValueOrDefault(Guid.Empty);

                    if (processData.luaLocations != null)
                        processData.breakpointLuaRuntimeError = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaG_runerror", "Lua run error", processData.luaLocations.luaRunError).GetValueOrDefault(Guid.Empty);
                    else
                        processData.breakpointLuaRuntimeError = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaG_runerror", "Lua run error", out _).GetValueOrDefault(Guid.Empty);

                    if (processData.luaLocations != null)
                    {
                        processData.breakpointLuaThrowAddress = processData.luaLocations.luaThrow;
                        processData.breakpointLuaThrow = AttachmentHelpers.CreateTargetFunctionBreakpointObjectAtAddress(process, processData.moduleWithLoadedLua, "luaD_throw", "Lua script error", processData.luaLocations.luaThrow, false);
                    }
                    else
                    {
                        processData.breakpointLuaThrow = AttachmentHelpers.CreateTargetFunctionBreakpointObjectAtDebugStart(process, processData.moduleWithLoadedLua, "luaD_throw", "Lua script error", out processData.breakpointLuaThrowAddress, false);
                    }

                    // Check for luajit
                    ulong ljSetMode = processData.luaLocations != null ? processData.luaLocations.ljSetMode : AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "luaJIT_setmode");

                    // Load luajit functions
                    if (ljSetMode != 0)
                    {
                        if (processData.luaLocations != null)
                        {
                            processData.luaSetHookAddress = processData.luaLocations.luaSetHook;
                            processData.luaGetInfoAddress = processData.luaLocations.luaGetInfo;
                            processData.luaGetStackAddress = processData.luaLocations.luaGetStack;

                            processData.breakpointLuaThrowAddress = processData.luaLocations.ljErrThrow;
                            processData.breakpointLuaJitErrThrow = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "lj_err_throw", "LuaJIT error throw", processData.luaLocations.ljErrThrow).GetValueOrDefault(Guid.Empty);

                            processData.breakpointLuaThreadCreateExternalStart = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaL_newstate", "Lua thread creation (lib @ start)", processData.luaLocations.luaLibNewStateAtStart).GetValueOrDefault(Guid.Empty);
                            processData.breakpointLuaThreadCreateExternalEnd = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaL_newstate", "Lua thread creation (lib @ end)", processData.luaLocations.luaLibNewStateAtEnd).GetValueOrDefault(Guid.Empty);

                            processData.breakpointLuaBufferLoadedExternalStart = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaL_loadbuffer", "Lua buffer load (outer @ start)", processData.luaLocations.luaLoadBufferAtStart).GetValueOrDefault(Guid.Empty);
                            processData.breakpointLuaBufferLoadedExternalEnd = AttachmentHelpers.CreateTargetFunctionBreakpointAtAddress(process, processData.moduleWithLoadedLua, "luaL_loadbuffer", "Lua buffer load (outer @ end)", processData.luaLocations.luaLoadBufferAtEnd).GetValueOrDefault(Guid.Empty);
                        }
                        else
                        {
                            processData.luaSetHookAddress = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "lua_sethook");
                            processData.luaGetInfoAddress = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "lua_getinfo");
                            processData.luaGetStackAddress = AttachmentHelpers.FindFunctionAddress(nativeModuleInstance, "lua_getstack");

                            processData.breakpointLuaThrowAddress = AttachmentHelpers.TryGetFunctionAddressAtDebugStart(processData.moduleWithLoadedLua, "lj_err_throw", out string _).GetValueOrDefault(0);
                            processData.breakpointLuaJitErrThrow = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "lj_err_throw", "LuaJIT error throw", out _).GetValueOrDefault(Guid.Empty);

                            processData.breakpointLuaThreadCreateExternalStart = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaL_newstate", "Lua thread creation (lib @ start)", out _).GetValueOrDefault(Guid.Empty);
                            processData.breakpointLuaThreadCreateExternalEnd = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugEnd(process, processData.moduleWithLoadedLua, "luaL_newstate", "Lua thread creation (lib @ end)", out _).GetValueOrDefault(Guid.Empty);

                            processData.breakpointLuaBufferLoadedExternalStart = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugStart(process, processData.moduleWithLoadedLua, "luaL_loadbuffer", "Lua buffer load (outer @ start)", out _).GetValueOrDefault(Guid.Empty);
                            processData.breakpointLuaBufferLoadedExternalEnd = AttachmentHelpers.CreateTargetFunctionBreakpointAtDebugEnd(process, processData.moduleWithLoadedLua, "luaL_loadbuffer", "Lua buffer load (outer @ end)", out _).GetValueOrDefault(Guid.Empty);
                        }

                        LuaHelpers.luaVersion = LuaHelpers.luaVersionLuajit;
                    }

                    string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                    string dllPathName = Path.Combine(assemblyFolder, DebugHelpers.Is64Bit(process) ? "LuaDebugHelper_x64.dll" : "LuaDebugHelper_x86.dll");

                    if (!File.Exists(dllPathName))
                    {
                        SendStatusMessage(process, 2, $"Attach: helper dll hasn't been found");
                        log.Warning("Helper dll hasn't been found");
                        processData.helperInjectionFailed = true;
                        return;
                    }

                    var dllNameAddress = process.AllocateVirtualMemory(0ul, 4096, 0x3000, 0x04);

                    byte[] bytes = Encoding.ASCII.GetBytes(dllPathName);

                    process.WriteMemory(dllNameAddress, bytes);
                    process.WriteMemory(dllNameAddress + (ulong)bytes.Length, new byte[1] { 0 });

                    if (DebugHelpers.Is64Bit(process))
                    {
                        string exePathName = Path.Combine(assemblyFolder, "LuaDebugAttacher_x64.exe");

                        if (!File.Exists(exePathName))
                        {
                            SendStatusMessage(process, 2, $"Attach: helper exe hasn't been found");
                            log.Error("Helper exe hasn't been found");
                            processData.helperInjectionFailed = true;
                            return;
                        }

                        var processStartInfo = new ProcessStartInfo(exePathName, $"{process.LivePart.Id} {processData.loadLibraryAddress} {dllNameAddress} \"{dllPathName}\"")
                        {
                            CreateNoWindow = true,
                            RedirectStandardError = true,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            UseShellExecute = false
                        };

                        log.Debug($"Launching '{exePathName}' with '{processStartInfo.Arguments}'");

                        try
                        {
                            var attachProcess = Process.Start(processStartInfo);

                            attachProcess.WaitForExit();

                            if (attachProcess.ExitCode != 0)
                            {
                                SendStatusMessage(process, 2, $"Attach: failed to start thread (x64) code {attachProcess.ExitCode}");
                                log.Error($"Failed to start thread (x64) code {attachProcess.ExitCode}");

                                string errors = attachProcess.StandardError.ReadToEnd();

                                if (errors != null)
                                    log.Error(errors);

                                string output = attachProcess.StandardOutput.ReadToEnd();

                                if (output != null)
                                    log.Error(output);

                                processData.helperInjectionFailed = true;
                                return;
                            }
                        }
                        catch (Exception e)
                        {
                            SendStatusMessage(process, 2, $"Attach: failed to start process (x64)");
                            log.Error("Failed to start process (x64) with " + e.Message);
                            processData.helperInjectionFailed = true;
                            return;
                        }

                        SendStatusMessage(process, 2, $"Attach: injected (x64), waiting...");
                    }
                    else
                    {
                        // Configure dll permissions to inject into UWP sandboxed applications
                        int errorCode = Advapi32.AdjustAccessControlListForUwp(dllPathName);

                        if (errorCode != 0)
                            log.Warning($"Failed to adjust debug helper access control list (error code {errorCode}), if injection fails, thread may hang");

                        var processHandle = Kernel32.OpenProcess(0x001F0FFF, false, process.LivePart.Id);

                        if (processHandle == IntPtr.Zero)
                        {
                            SendStatusMessage(process, 2, $"Attach: failed to open target process");
                            log.Error("Failed to open target process");
                            processData.helperInjectionFailed = true;
                            return;
                        }

                        var threadHandle = Kernel32.CreateRemoteThread(processHandle, IntPtr.Zero, UIntPtr.Zero, (IntPtr)processData.loadLibraryAddress, (IntPtr)dllNameAddress, 0, IntPtr.Zero);

                        if (threadHandle == IntPtr.Zero)
                        {
                            SendStatusMessage(process, 2, $"Attach: failed to start thread (x86)");
                            log.Error("Failed to start thread (x86)");
                            processData.helperInjectionFailed = true;
                            return;
                        }

                        if (errorCode != 0)
                            SendStatusMessage(process, 2, $"Attach: injected (x86, thread may hang), waiting...");
                        else
                            SendStatusMessage(process, 2, $"Attach: injected (x86), waiting...");
                    }

                    processData.helperInjected = true;

                    log.Debug("Helper library has been injected");
                }
            }

            log.Debug($"IDkmModuleInstanceLoadNotification.OnModuleInstanceLoad finished");
        }

        void ClearSchema()
        {
            Schema.LuaStringData.available = false;
            Schema.LuaValueData.available = false;
            Schema.LuaLocalVariableData.available = false;
            Schema.LuaUpvalueDescriptionData.available = false;
            Schema.LuaUpvalueData.available = false;
            Schema.LuaFunctionData.available = false;
            Schema.LuaFunctionCallInfoData.available = false;
            Schema.LuaNodeData.available = false;
            Schema.LuaTableData.available = false;
            Schema.LuaClosureData.available = false;
            Schema.LuaExternalClosureData.available = false;
            Schema.LuaUserDataData.available = false;
            Schema.LuaStateData.available = false;
            Schema.LuaDebugData.available = false;

            Schema.LuajitStateData.available = false;
        }

        void LoadSchema(LuaLocalProcessData processData, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
        {
            if (processData.schemaLoaded)
                return;

            Schema.LuaStringData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaValueData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaLocalVariableData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaUpvalueDescriptionData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaUpvalueData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaFunctionData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaFunctionCallInfoData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaNodeData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaTableData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaClosureData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaExternalClosureData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaUserDataData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaStateData.LoadSchema(inspectionSession, thread, frame);
            Schema.LuaDebugData.LoadSchema(inspectionSession, thread, frame);

            processData.schemaLoaded = true;
        }

        void LoadSchemaLuajit(LuaLocalProcessData processData, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
        {
            if (processData.schemaLoadedLuajit)
                return;

            Schema.Luajit.LoadSchema(inspectionSession, thread, frame);
            Schema.LuajitStateData.LoadSchema(inspectionSession, thread, frame);

            processData.schemaLoadedLuajit = true;
        }

        void SendVersionNotification(DkmProcess process, LuaLocalProcessData processData)
        {
            if (processData.versionNotificationSent)
                return;

            if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
            {
                if (DebugHelpers.Is64Bit(process))
                {
                    if (Schema.Luajit.fullPointer)
                    {
                        SendStatusMessage(process, 1, $"Lua: created (LuaJIT x64 GC64)");
                    }
                    else
                    {
                        SendStatusMessage(process, 1, $"Lua: created (LuaJIT x64 GC32)");
                    }
                }
                else
                {
                    SendStatusMessage(process, 1, $"Lua: created (LuaJIT x86)");
                }
            }
            else
            {
                string arch = DebugHelpers.Is64Bit(process) ? "x64" : "x86";

                if (LuaHelpers.luaVersion != 0)
                {
                    SendStatusMessage(process, 1, $"Lua: created ({LuaHelpers.luaVersion / 100}.0.{LuaHelpers.luaVersion % 10} {arch})");
                }
                else
                {
                    SendStatusMessage(process, 1, $"Lua: created (unknown {arch})");
                }
            }

            processData.versionNotificationSent = true;
        }

        bool HasPendingBreakpoint(DkmProcess process, LuaLocalProcessData processData, string filePath)
        {
            if (!processData.pendingBreakpointDocumentsReady)
            {
                foreach (var pendingBreakpoint in process.GetPendingBreakpoints())
                {
                    if (pendingBreakpoint is DkmPendingFileLineBreakpoint pendingFileLineBreakpoint)
                    {
                        var sourcePosition = pendingFileLineBreakpoint.GetCurrentSourcePosition();

                        if (sourcePosition != null)
                            processData.pendingBreakpointDocuments.Add(sourcePosition.DocumentName);
                    }
                }

                processData.pendingBreakpointDocumentsReady = true;
            }

            return processData.pendingBreakpointDocuments.Contains(filePath);
        }

        void RegisterScriptBuffer(DkmProcess process, LuaLocalProcessData processData, ulong stateAddress, ulong scriptBufferAddress, long scriptSize, ulong scriptNameAddress)
        {
            byte[] rawScriptContent = DebugHelpers.ReadRawStringVariable(process, scriptBufferAddress, (int)scriptSize);

            if (rawScriptContent != null)
            {
                string scriptContent = Encoding.UTF8.GetString(rawScriptContent, 0, rawScriptContent.Length);

                string scriptName;

                if (scriptBufferAddress == scriptNameAddress)
                {
                    string badScriptName = scriptContent;

                    if (badScriptName.Length > 1023)
                        badScriptName = badScriptName.Substring(0, 1023);

                    lock (processData.symbolStore)
                    {
                        LuaStateSymbols stateSymbols = processData.symbolStore.FetchOrCreate(stateAddress);

                        scriptName = $"unnamed_{processData.unnamedScriptId++}";

                        stateSymbols.unnamedScriptMapping.Add(badScriptName, scriptName);
                    }
                }
                else
                {
                    scriptName = DebugHelpers.ReadStringVariable(process, scriptNameAddress, 1024);
                }

                if (scriptName != null)
                {
                    var sha1Hash = new SHA1Managed().ComputeHash(rawScriptContent);

                    lock (processData.symbolStore)
                    {
                        processData.symbolStore.FetchOrCreate(stateAddress).AddScriptSource(scriptName, scriptContent, sha1Hash);
                    }

                    log.Debug($"Adding script {scriptName} to symbol store of Lua state {stateAddress} (with content)");

                    string resolvedPath = TryFindSourcePath(process.Path, processData, scriptName, scriptContent, false, out string loadStatus);

                    if (resolvedPath != null)
                    {
                        if (HasPendingBreakpoint(process, processData, resolvedPath))
                        {
                            var message = DkmCustomMessage.Create(process.Connection, process, Guid.Empty, MessageToVsService.reloadBreakpoints, Encoding.UTF8.GetBytes(resolvedPath), null);

                            message.SendToVsService(Guids.luaVsPackageComponentGuid, true);
                        }

                        if (scriptBufferAddress != scriptNameAddress)
                        {
                            ScriptLoadMessage scriptLoadMessage = new ScriptLoadMessage
                            {
                                name = scriptName,
                                path = resolvedPath,
                                status = loadStatus,
                                content = scriptContent
                            };

                            DkmCustomMessage.Create(process.Connection, process, Guid.Empty, MessageToVsService.scriptLoad, scriptLoadMessage.Encode(), null).SendToVsService(Guids.luaVsPackageComponentGuid, false);
                        }
                    }
                }
                else
                {
                    log.Error("Failed to load script name from process");
                }
            }
            else
            {
                log.Error("Failed to load script content from process");
            }
        }

        void RegisterLuaStateCreation(DkmProcess process, LuaLocalProcessData processData, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, ulong? stateAddress)
        {
            if (useSchema)
            {
                if (!processData.schemaLoaded)
                    LoadSchema(processData, inspectionSession, thread, frame);
            }
            else
            {
                ClearSchema();
            }

            long? version = EvaluationHelpers.TryEvaluateNumberExpression($"(int)*((lua_State*){stateAddress})->l_G->version", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

            // Sadly, version field was only added in 5.2 and was removed in 5.4
            if (!version.HasValue)
            {
                // Warning function was added in 5.4
                if (EvaluationHelpers.TryEvaluateNumberExpression($"(int)((lua_State*){stateAddress})->l_G->ud_warn", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects).HasValue)
                    version = 504;
            }

            if (!version.HasValue)
            {
                // dummy_ffid field is used in luajit
                if (EvaluationHelpers.TryEvaluateNumberExpression($"(int)((lua_State*){stateAddress})->dummy_ffid", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects).HasValue)
                    version = LuaHelpers.luaVersionLuajit;
            }

            if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
            {
                LoadSchemaLuajit(processData, inspectionSession, thread, frame);

                version = LuaHelpers.luaVersionLuajit;
            }

            log.Debug("Completed evaluation");

            // Don't check version, Lua 5.1 doesn't have it
            if (stateAddress.HasValue)
            {
                log.Debug($"New Lua state 0x{stateAddress:x} version {version.GetValueOrDefault(501)}");

                LuaHelpers.luaVersion = (int)version.GetValueOrDefault(501);

                SendVersionNotification(process, processData);

                lock (processData.symbolStore)
                {
                    processData.symbolStore.FetchOrCreate(stateAddress.Value);
                }

                // Tell remote component about Lua version
                DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.luaVersionInfo, LuaHelpers.luaVersion, null).SendLower();

                if (!processData.helperInitialized)
                {
                    log.Warning("No helper to hook Lua state to");
                }
                else if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == 502 || LuaHelpers.luaVersion == 503 || LuaHelpers.luaVersion == 504)
                {
                    ulong? hookFunctionAddress = EvaluationHelpers.TryEvaluateAddressExpression($"&((lua_State*){stateAddress})->hook", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                    ulong? hookBaseCountAddress = EvaluationHelpers.TryEvaluateAddressExpression($"&((lua_State*){stateAddress})->basehookcount", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                    ulong? hookCountAddress = EvaluationHelpers.TryEvaluateAddressExpression($"&((lua_State*){stateAddress})->hookcount", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                    ulong? hookMaskAddress = EvaluationHelpers.TryEvaluateAddressExpression($"&((lua_State*){stateAddress})->hookmask", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    ulong? setTrapStateCallInfoOffset = null;
                    ulong? setTrapCallInfoPreviousOffset = null;
                    ulong? setTrapCallInfoCallStatusOffset = null;
                    ulong? setTrapCallInfoTrapOffset = null;
                    bool hasExtraValues = true;

                    if (LuaHelpers.luaVersion == 504)
                    {
                        setTrapStateCallInfoOffset = EvaluationHelpers.TryEvaluateAddressExpression($"&((lua_State*)0)->ci", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                        setTrapCallInfoPreviousOffset = EvaluationHelpers.TryEvaluateAddressExpression($"&((CallInfo*)0)->previous", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                        setTrapCallInfoCallStatusOffset = EvaluationHelpers.TryEvaluateAddressExpression($"&((CallInfo*)0)->callstatus", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                        setTrapCallInfoTrapOffset = EvaluationHelpers.TryEvaluateAddressExpression($"&((CallInfo*)0)->u.l.trap", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                        hasExtraValues = setTrapStateCallInfoOffset.HasValue && setTrapCallInfoPreviousOffset.HasValue && setTrapCallInfoCallStatusOffset.HasValue && setTrapCallInfoTrapOffset.HasValue;
                    }

                    if (hookFunctionAddress.HasValue && hookBaseCountAddress.HasValue && hookCountAddress.HasValue && hookMaskAddress.HasValue && hasExtraValues)
                    {
                        var message = new RegisterStateMessage
                        {
                            stateAddress = stateAddress.Value,

                            hookFunctionAddress = hookFunctionAddress.Value,
                            hookBaseCountAddress = hookBaseCountAddress.Value,
                            hookCountAddress = hookCountAddress.Value,
                            hookMaskAddress = hookMaskAddress.Value,

                            setTrapStateCallInfoOffset = setTrapStateCallInfoOffset.GetValueOrDefault(0),
                            setTrapCallInfoPreviousOffset = setTrapCallInfoPreviousOffset.GetValueOrDefault(0),
                            setTrapCallInfoCallStatusOffset = setTrapCallInfoCallStatusOffset.GetValueOrDefault(0),
                            setTrapCallInfoTrapOffset = setTrapCallInfoTrapOffset.GetValueOrDefault(0),
                        };

                        bool hasSchemaForHook = false;

                        if (processData.schemaLoaded)
                            hasSchemaForHook = Schema.LuaDebugData.available && Schema.LuaStateData.available && Schema.LuaFunctionCallInfoData.available && Schema.LuaValueData.available && Schema.LuaClosureData.available && Schema.LuaFunctionData.available;

                        if (hasSchemaForHook && LuaHelpers.luaVersion != 501)
                        {
                            message.helperHookFunctionAddress = processData.helperHookFunctionAddress_5_234_compat;

                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatLuaDebugEventOffset, (uint)Schema.LuaDebugData.eventType.GetValueOrDefault(0));
                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatLuaDebugCurrentLineOffset, (uint)Schema.LuaDebugData.currentLine.GetValueOrDefault(0));
                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatLuaStateCallInfoOffset, (uint)Schema.LuaStateData.callInfoAddress.GetValueOrDefault(0));
                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatCallInfoFunctionOffset, (uint)Schema.LuaFunctionCallInfoData.funcAddress.GetValueOrDefault(0));
                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatTaggedValueTypeTagOffset, (uint)Schema.LuaValueData.typeAddress.GetValueOrDefault(0));
                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatTaggedValueValueOffset, (uint)Schema.LuaValueData.valueAddress.GetValueOrDefault(0));
                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatLuaClosureProtoOffset, (uint)Schema.LuaClosureData.functionAddress.GetValueOrDefault(0));
                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatLuaFunctionSourceOffset, (uint)Schema.LuaFunctionData.sourceAddress.GetValueOrDefault(0));
                            DebugHelpers.TryWriteUintVariable(process, processData.helperCompatStringContentOffset, (uint)LuaHelpers.GetStringDataOffset(process));
                        }
                        else if (LuaHelpers.luaVersion == 501)
                        {
                            message.helperHookFunctionAddress = processData.helperHookFunctionAddress_5_1;
                        }
                        else if (LuaHelpers.luaVersion == 502)
                        {
                            message.helperHookFunctionAddress = processData.helperHookFunctionAddress_5_2;
                        }
                        else if (LuaHelpers.luaVersion == 503)
                        {
                            message.helperHookFunctionAddress = processData.helperHookFunctionAddress_5_3;
                        }
                        else if (LuaHelpers.luaVersion == 504)
                        {
                            message.helperHookFunctionAddress = processData.helperHookFunctionAddress_5_4;
                        }

                        DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.registerLuaState, message.Encode(), null).SendLower();

                        log.Debug("Hooked Lua state");
                    }
                    else
                    {
                        log.Warning("Failed to evaluate variables to hook");
                    }
                }
                else if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                {
                    DebugHelpers.TryWriteUlongVariable(process, processData.helperLuajitGetInfoAddress, processData.luaGetInfoAddress);
                    DebugHelpers.TryWriteUlongVariable(process, processData.helperLuajitGetStackAddress, processData.luaGetStackAddress);
                }
                else
                {
                    log.Warning("Hook does not support this Lua version");
                }
            }
            else
            {
                log.Error($"Failed to evaluate Lua state location");
            }
        }

        void TryRegisterLuaStateCreation(DkmProcess process, LuaLocalProcessData processData, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, ulong stateAddress)
        {
            if (processData.skipStateCreationRecovery)
                return;

            LoadSchemaLuajit(processData, inspectionSession, thread, frame);

            if (Schema.Luajit.luaStateSize != 0)
            {
                LuajitStateData state = new LuajitStateData();

                state.ReadFrom(process, stateAddress);

                if (state.globalStateAddress != 0)
                {
                    ulong mainThreadAddress = state.globalStateAddress - (ulong)Schema.Luajit.luaStateSize;

                    if (stateAddress != mainThreadAddress)
                        stateAddress = mainThreadAddress;
                }
            }

            bool knownState = false;

            lock (processData.symbolStore)
            {
                knownState = processData.symbolStore.knownStates.ContainsKey(stateAddress);
            }

            if (!knownState)
            {
                RegisterLuaStateCreation(process, processData, inspectionSession, thread, frame, stateAddress);
            }
        }

        DkmCustomMessage IDkmCustomMessageCallbackReceiver.SendHigher(DkmCustomMessage customMessage)
        {
            log.Debug($"IDkmCustomMessageCallbackReceiver.SendHigher begin");

            var process = customMessage.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

            if (customMessage.MessageCode == MessageToLocal.luaSupportBreakpointHit)
            {
                var data = new SupportBreakpointHitMessage();

                data.ReadFrom(customMessage.Parameter1 as byte[]);

                var thread = process.GetThreads().FirstOrDefault(el => el.UniqueId == data.threadId);

                if (data.breakpointId == processData.breakpointLuaInitialization || data.breakpointId == processData.breakpointLuaThreadCreateExternalStart)
                {
                    if (data.breakpointId == processData.breakpointLuaThreadCreateExternalStart)
                    {
                        log.Debug("Detected Lua initialization (lib @ start)");

                        processData.skipNextInternalCreate = true;
                    }
                    else
                    {
                        log.Debug("Detected Lua initialization");

                        if (processData.skipNextInternalCreate)
                        {
                            log.Debug("Skipping internal initialization");
                            return null;
                        }
                    }

                    if (processData.helperInjected && !processData.helperInitialized && !processData.helperFailed && !processData.helperInitializationWaitUsed)
                    {
                        log.Debug("Helper was injected but hasn't been initialized, suspending thread");

                        Debug.Assert(thread != null);

                        thread.Suspend(true);

                        processData.helperInitializationSuspensionThread = thread;
                        processData.helperInitializationWaitUsed = true;
                    }
                    else if (processData.helperInjectionFailed)
                    {
                        log.Warning("Helper injection has failed");
                    }
                    else if (!processData.helperInjected)
                    {
                        log.Warning("Helper hasn't been injected");
                    }
                    else if (processData.helperInitialized)
                    {
                        log.Debug("Helper already initialized, no need to suspend Lua");
                    }
                    else if (processData.helperFailed)
                    {
                        log.Warning("Helper initialization has failed");
                    }
                    else if (processData.helperInitializationWaitUsed)
                    {
                        log.Debug("Lua is already suspended for helper");
                    }
                }
                else if (data.breakpointId == processData.breakpointLuaThreadCreate || data.breakpointId == processData.breakpointLuaThreadCreateExternalEnd)
                {
                    if (data.breakpointId == processData.breakpointLuaThreadCreate)
                    {
                        log.Debug("Detected Lua thread start");

                        if (processData.skipNextInternalCreate)
                        {
                            log.Debug("Skipping internal create");

                            processData.skipNextInternalCreate = false;
                            return null;
                        }
                    }
                    else
                    {
                        log.Debug("Detected Lua thread start (lib @ end)");

                        processData.skipNextInternalCreate = false;
                    }

                    var inspectionSession = EvaluationHelpers.CreateInspectionSession(process, thread, data, out DkmStackWalkFrame frame);

                    ulong? stateAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (!stateAddress.HasValue)
                        stateAddress = EvaluationHelpers.TryEvaluateAddressExpression(DebugHelpers.Is64Bit(process) ? "@rax" : "@eax", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    processData.skipStateCreationRecovery = true;

                    RegisterLuaStateCreation(process, processData, inspectionSession, thread, frame, stateAddress);

                    inspectionSession.Close();
                }
                else if (data.breakpointId == processData.breakpointLuaThreadDestroy || data.breakpointId == processData.breakpointLuaThreadDestroyInternal)
                {
                    if (data.breakpointId == processData.breakpointLuaThreadDestroy)
                    {
                        log.Debug("Detected Lua thread destruction");

                        processData.skipNextInternalDestroy = true;
                    }
                    else
                    {
                        if (processData.skipNextInternalDestroy)
                        {
                            processData.skipNextInternalDestroy = false;

                            return null;
                        }

                        log.Debug("Detected raw Lua thread destruction");
                    }

                    var inspectionSession = EvaluationHelpers.CreateInspectionSession(process, thread, data, out DkmStackWalkFrame frame);

                    ulong? stateAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (stateAddress.HasValue)
                    {
                        log.Debug($"Removing Lua state 0x{stateAddress:x} from symbol store");

                        lock (processData.symbolStore)
                        {
                            processData.symbolStore.Remove(stateAddress.Value);
                        }

                        var message = new UnregisterStateMessage
                        {
                            stateAddress = stateAddress.Value,
                        };

                        DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.unregisterLuaState, message.Encode(), null).SendLower();
                    }

                    inspectionSession.Close();
                }
                else if (data.breakpointId == processData.breakpointLuaFileLoaded || data.breakpointId == processData.breakpointLuaFileLoadedSolCompat)
                {
                    log.Debug("Detected Lua script file load");

                    processData.skipNextRawLoad = true;

                    var inspectionSession = EvaluationHelpers.CreateInspectionSession(process, thread, data, out DkmStackWalkFrame frame);

                    ulong? stateAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (stateAddress.HasValue && LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                        TryRegisterLuaStateCreation(process, processData, inspectionSession, thread, frame, stateAddress.Value);

                    ulong? scriptNameAddress = EvaluationHelpers.TryEvaluateAddressExpression($"filename", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (stateAddress.HasValue && scriptNameAddress.HasValue)
                    {
                        string scriptContent = "";
                        string scriptName = DebugHelpers.ReadStringVariable(process, scriptNameAddress.Value, 1024);

                        if (scriptName != null)
                        {
                            scriptName = "@" + scriptName;

                            lock (processData.symbolStore)
                            {
                                processData.symbolStore.FetchOrCreate(stateAddress.Value).AddScriptSource(scriptName, scriptContent, null);
                            }

                            log.Debug($"Adding script {scriptName} to symbol store of Lua state {stateAddress.Value} (without content)");

                            string resolvedPath = TryFindSourcePath(process.Path, processData, scriptName, null, false, out string loadStatus);

                            if (resolvedPath != null)
                            {
                                if (HasPendingBreakpoint(process, processData, resolvedPath))
                                {
                                    var message = DkmCustomMessage.Create(process.Connection, process, Guid.Empty, MessageToVsService.reloadBreakpoints, Encoding.UTF8.GetBytes(resolvedPath), null);

                                    message.SendToVsService(Guids.luaVsPackageComponentGuid, true);
                                }

                                ScriptLoadMessage scriptLoadMessage = new ScriptLoadMessage
                                {
                                    name = scriptName,
                                    path = resolvedPath,
                                    status = loadStatus,
                                    content = ""
                                };

                                DkmCustomMessage.Create(process.Connection, process, Guid.Empty, MessageToVsService.scriptLoad, scriptLoadMessage.Encode(), null).SendToVsService(Guids.luaVsPackageComponentGuid, false);
                            }
                        }
                        else
                        {
                            log.Error("Failed to load script name from process");
                        }
                    }
                    else
                    {
                        log.Error("Failed to evaluate Lua buffer data");
                    }

                    inspectionSession.Close();
                }
                else if (data.breakpointId == processData.breakpointLuaBufferLoaded || data.breakpointId == processData.breakpointLuaBufferLoadedExternalStart)
                {
                    if (data.breakpointId == processData.breakpointLuaBufferLoadedExternalStart)
                    {
                        log.Debug("Detected Lua script buffer load (outer @ start)");

                        processData.skipNextInternalBufferLoaded = true;
                    }
                    else
                    {
                        log.Debug("Detected Lua script buffer load");

                        if (processData.skipNextInternalBufferLoaded)
                        {
                            log.Debug("Skipping internal script buffer load");

                            processData.skipNextInternalBufferLoaded = false;
                            return null;
                        }
                    }

                    processData.skipNextRawLoad = true;

                    var inspectionSession = EvaluationHelpers.CreateInspectionSession(process, thread, data, out DkmStackWalkFrame frame);

                    ulong? stateAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (stateAddress.HasValue && LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                        TryRegisterLuaStateCreation(process, processData, inspectionSession, thread, frame, stateAddress.Value);

                    ulong? scriptBufferAddress = EvaluationHelpers.TryEvaluateAddressExpression($"buff", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (!scriptBufferAddress.HasValue)
                        scriptBufferAddress = EvaluationHelpers.TryEvaluateAddressExpression($"buf", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    long? scriptSize = EvaluationHelpers.TryEvaluateNumberExpression($"size", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                    ulong? scriptNameAddress = EvaluationHelpers.TryEvaluateAddressExpression($"name", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (stateAddress.HasValue && scriptBufferAddress.HasValue && scriptSize.HasValue && scriptNameAddress.HasValue)
                    {
                        RegisterScriptBuffer(process, processData, stateAddress.Value, scriptBufferAddress.Value, scriptSize.Value, scriptNameAddress.Value);
                    }
                    else
                    {
                        log.Error("Failed to evaluate Lua buffer data");
                    }

                    inspectionSession.Close();
                }
                else if (data.breakpointId == processData.breakpointLuaBufferLoadedExternalEnd)
                {
                    log.Debug("Detected Lua script buffer load (outer @ end)");

                    processData.skipNextInternalBufferLoaded = false;
                }
                else if (data.breakpointId == processData.breakpointLuaLoad)
                {
                    if (processData.skipNextRawLoad)
                    {
                        processData.skipNextRawLoad = false;

                        return null;
                    }

                    log.Debug("Detected raw Lua script load");

                    var inspectionSession = EvaluationHelpers.CreateInspectionSession(process, thread, data, out DkmStackWalkFrame frame);

                    ulong? isStringReader = EvaluationHelpers.TryEvaluateAddressExpression($"reader == getS", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (isStringReader.GetValueOrDefault(0) != 0)
                    {
                        ulong? stateAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                        if (stateAddress.HasValue && LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                            TryRegisterLuaStateCreation(process, processData, inspectionSession, thread, frame, stateAddress.Value);

                        ulong? scriptBufferAddress = EvaluationHelpers.TryEvaluateAddressExpression($"*(char**)data", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                        long? scriptSize = EvaluationHelpers.TryEvaluateNumberExpression($"*(size_t*)((char*)data+sizeof(void*))", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);
                        ulong? scriptNameAddress = EvaluationHelpers.TryEvaluateAddressExpression($"chunkname", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                        if (stateAddress.HasValue && scriptBufferAddress.HasValue && scriptSize.HasValue && scriptNameAddress.HasValue)
                        {
                            RegisterScriptBuffer(process, processData, stateAddress.Value, scriptBufferAddress.Value, scriptSize.Value, scriptNameAddress.Value);
                        }
                        else
                        {
                            log.Error("Failed to evaluate Lua buffer data");
                        }
                    }
                    else
                    {
                        log.Warning("Not a string loader");
                    }

                    inspectionSession.Close();
                }
                else if (data.breakpointId == processData.breakpointLuaHelperInitialized)
                {
                    SendStatusMessage(process, 2, $"Attach: initialized");
                    log.Debug("Detected Helper initialization");

                    processData.helperInitializationWaitActive = false;
                    processData.helperInitialized = true;

                    if (processData.helperInitializationSuspensionThread != null)
                    {
                        log.Debug("Resuming Lua thread");

                        processData.helperInitializationSuspensionThread.Resume(true);

                        processData.helperInitializationSuspensionThread = null;
                    }

                    if (processData.helperWorkingDirectoryAddress != 0)
                    {
                        processData.workingDirectoryRequested = true;
                        processData.workingDirectory = DebugHelpers.ReadStringVariable(process, processData.helperWorkingDirectoryAddress, 1024);

                        if (processData.workingDirectory != null && processData.workingDirectory.Length != 0)
                        {
                            log.Debug($"Found process working directory {processData.workingDirectory}");

                            LoadConfigurationFile(process, processData);
                        }
                        else
                        {
                            log.Error("Failed to get process working directory'");
                        }
                    }
                }
                else if (data.breakpointId == processData.breakpointLuaRuntimeError || data.breakpointId == processData.breakpointLuaBreakError)
                {
                    log.Debug("Detected Lua runtime error");

                    if (!breakOnError)
                    {
                        log.Debug("Break on error is disabled, ignoring");

                        return null;
                    }

                    log.Debug("Enabling a trap at next luaD_throw");

                    if (processData.breakpointLuaThrow != null)
                        processData.breakpointLuaThrow.Enable();
                }
                else if (processData.breakpointLuaThrow != null && data.breakpointId == processData.breakpointLuaThrow.UniqueId)
                {
                    log.Debug("Detected Lua exception throw");

                    processData.breakpointLuaThrow.Disable();

                    var inspectionSession = EvaluationHelpers.CreateInspectionSession(process, thread, data, out DkmStackWalkFrame frame);

                    ulong? messageAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L->top - 1", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (messageAddress.HasValue)
                    {
                        var value = LuaHelpers.ReadValue(process, messageAddress.Value);

                        if (value != null)
                        {
                            var description = value.AsSimpleDisplayString(10);

                            return DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.throwException, Encoding.UTF8.GetBytes(description), null);
                        }
                        else
                        {
                            log.Error("Failed to get Lua error message");
                        }
                    }
                }
                else if (data.breakpointId == processData.breakpointLuaHelperAsyncBreak)
                {
                    log.Debug("Detected Lua debugger async break");

                    ulong code = DebugHelpers.ReadUintVariable(process, processData.helperAsyncBreakCodeAddress).GetValueOrDefault(0);

                    // Acknowledge signal
                    DebugHelpers.TryWriteUintVariable(process, processData.helperAsyncBreakCodeAddress, 0u);

                    var inspectionSession = EvaluationHelpers.CreateInspectionSession(process, thread, data, out DkmStackWalkFrame frame);

                    if (code == 1)
                    {
                        if (processData.luaSetHookAddress == 0)
                        {
                            log.Error("lua_sethook address is not available");
                            return null;
                        }

                        List<ulong> states = new List<ulong>();

                        lock (processData.symbolStore)
                        {
                            foreach (var state in processData.symbolStore.knownStates)
                                states.Add(state.Key);
                        }

                        // TODO: all states
                        if (states.Count > 0)
                        {
                            DebugHelpers.TryWriteUintVariable(process, processData.helperAsyncBreakCodeAddress, 2u);

                            DebugHelpers.TryWriteUlongVariable(process, processData.helperAsyncBreakDataAddress + 0 * 8, processData.luaSetHookAddress);
                            DebugHelpers.TryWriteUlongVariable(process, processData.helperAsyncBreakDataAddress + 1 * 8, states[0]);
                            DebugHelpers.TryWriteUlongVariable(process, processData.helperAsyncBreakDataAddress + 2 * 8, processData.helperHookFunctionAddress_luajit);
                        }
                    }
                    else if (code == 3)
                    {
                        List<ulong> states = new List<ulong>();

                        lock (processData.symbolStore)
                        {
                            foreach (var state in processData.symbolStore.knownStates)
                                states.Add(state.Key);
                        }

                        // TODO: all states
                        if (states.Count > 0)
                        {
                            DebugHelpers.TryWriteUintVariable(process, processData.helperAsyncBreakCodeAddress, 4u);

                            DebugHelpers.TryWriteUlongVariable(process, processData.helperAsyncBreakDataAddress + 0 * 8, processData.luaSetHookAddress);
                            DebugHelpers.TryWriteUlongVariable(process, processData.helperAsyncBreakDataAddress + 1 * 8, states[0]);
                            DebugHelpers.TryWriteUlongVariable(process, processData.helperAsyncBreakDataAddress + 2 * 8, 0);
                        }
                    }
                    else
                    {
                        log.Error("Unknown async break code");
                    }
                }
                else if (data.breakpointId == processData.breakpointLuaJitErrThrow)
                {
                    log.Debug("Detected LuaJIT exception throw");

                    var inspectionSession = EvaluationHelpers.CreateInspectionSession(process, thread, data, out DkmStackWalkFrame frame);

                    ulong? messageAddress = EvaluationHelpers.TryEvaluateAddressExpression($"L->top", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (messageAddress.HasValue)
                    {
                        var value = LuaHelpers.ReadValue(process, messageAddress.Value);

                        if (value != null)
                        {
                            var description = value.AsSimpleDisplayString(10);

                            return DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.throwException, Encoding.UTF8.GetBytes(description), null);
                        }
                        else
                        {
                            log.Error("Failed to get LuaJIT error message");
                        }
                    }
                }
                else
                {
                    log.Warning("Received unknown breakpoint hit");
                }
            }

            log.Debug($"IDkmCustomMessageCallbackReceiver.SendHigher finished");

            return null;
        }

        bool IDkmModuleUserCodeDeterminer.IsUserCode(DkmModuleInstance moduleInstance)
        {
            return true;
        }
    }
}
