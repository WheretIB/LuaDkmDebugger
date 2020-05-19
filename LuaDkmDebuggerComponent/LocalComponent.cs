using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.Evaluation;
using System.Collections.Generic;

namespace LuaDkmDebuggerComponent
{
    public class LuaStackFilter : IDkmCallStackFilter
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
                const int luaBaseTypeMask = 0xf;
                const int luaExtendedTypeMask = 0x3f;

                const int luaTypeFunction = 6;

                const int luaTypeLuaFunction = luaTypeFunction + (0 << 4);
                const int luaTypeExternalFunction = luaTypeFunction + (1 << 4);
                const int luaTypeExternalClosure = luaTypeFunction + (2 << 4);

                int luaStringOffset = DebugHelpers.GetPointerSize(input.Thread.Process) * 2 + 8;

                List<DkmStackWalkFrame> luaFrames = new List<DkmStackWalkFrame>();

                var luaFrameFlags = input.Flags;

                luaFrameFlags = luaFrameFlags & ~(DkmStackWalkFrameFlags.NonuserCode | DkmStackWalkFrameFlags.UserStatusNotDetermined);
                luaFrameFlags = luaFrameFlags | DkmStackWalkFrameFlags.InlineOptimized;

                ulong? currCallInfo = TryEvaluateAddressExpression($"L->ci", stackContext, input);

                while (currCallInfo.HasValue && currCallInfo.Value != 0)
                {
                    long? frameType = TryEvaluateNumberExpression($"((CallInfo*){currCallInfo.Value})->func->tt_", stackContext, input);

                    if (frameType.HasValue && (frameType.Value & luaBaseTypeMask) == luaTypeFunction)
                    {
                        if ((frameType.Value & luaExtendedTypeMask) == luaTypeLuaFunction)
                        {
                            ulong? currProto = TryEvaluateAddressExpression($"((LClosure*)((CallInfo*){currCallInfo.Value})->func->value_.gc)->p", stackContext, input);

                            // Should be there, but can timeout or made an internal error
                            if (!currProto.HasValue)
                                break;

                            string sourceName = TryEvaluateStringExpression($"(char*)((Proto*){currProto.Value})->source + {luaStringOffset}", stackContext, input);
                            long? lineDefined = TryEvaluateNumberExpression($"((Proto*){currProto.Value})->linedefined", stackContext, input);

                            if (sourceName != null && lineDefined.HasValue)
                            {
                                luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"{sourceName} unknown() Line {lineDefined}", input.Registers, input.Annotations));
                            }
                        }
                        else if ((frameType.Value & luaExtendedTypeMask) == luaTypeExternalFunction)
                        {
                            // TODO: iirc, Lua has a single call stack with external function entries, when stack filter is performed, we should break up these parts to avoid duplication
                            luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"C function", input.Registers, input.Annotations));
                        }
                        else if ((frameType.Value & luaExtendedTypeMask) == luaTypeExternalClosure)
                        {
                            // TODO: iirc, Lua has a single call stack with external function entries, when stack filter is performed, we should break up these parts to avoid duplication
                            luaFrames.Add(DkmStackWalkFrame.Create(stackContext.Thread, input.InstructionAddress, input.FrameBase, input.FrameSize, luaFrameFlags, $"C closure", input.Registers, input.Annotations));
                        }

                        currCallInfo = TryEvaluateAddressExpression($"((CallInfo*){currCallInfo.Value})->previous", stackContext, input);
                    }
                    else
                    {
                        currCallInfo = null;
                    }
                }

                luaFrames.Add(input);

                return luaFrames.ToArray();
            }

            return new DkmStackWalkFrame[1] { input };
        }
    }
}
