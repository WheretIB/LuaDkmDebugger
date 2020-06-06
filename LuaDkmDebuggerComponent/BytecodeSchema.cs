using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Interop;
using System.Diagnostics;
using System.Runtime.Serialization.Formatters;

namespace LuaDkmDebuggerComponent
{
    namespace Schema
    {
        public static class Helper
        {
            public static bool looksLike_5_1 = false;

            public static long GetSize(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, string type, ref bool available)
            {
                long? result = EvaluationHelpers.TryEvaluateNumberExpression($"sizeof({type})", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects).GetValueOrDefault(0);

                if (!result.HasValue)
                {
                    available = false;

                    if (Log.instance != null)
                        Log.instance.Debug($"Failed to get sizeof '{type}'");

                    return 0;
                }

                return result.Value;
            }

            public static ulong? Read(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, string type, string[] memberOptions, ref bool available, ref int success, ref int failure)
            {
                Debug.Assert(memberOptions.Length > 0);

                long? result = null;

                foreach (var option in memberOptions)
                {
                    result = EvaluationHelpers.TryEvaluateNumberExpression($"(int)&(({type}*)0)->{option}", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                    if (result != null)
                        break;
                }

                if (!result.HasValue)
                {
                    available = false;
                    failure++;

                    if (Log.instance != null)
                        Log.instance.Debug($"Failed to get offsetof '{memberOptions[0]}' in '{type}'");

                    return null;
                }

                success++;
                return (ulong)result.Value;
            }

            public static ulong? Read(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, string type, string member, ref bool available, ref int success, ref int failure)
            {
                return Read(inspectionSession, thread, frame, type, new[] { member }, ref available, ref success, ref failure);
            }

            public static ulong? ReadOptional(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, string type, string member, ref int optional, out long size)
            {
                long? result = EvaluationHelpers.TryEvaluateNumberExpression($"(int)&(({type}*)0)->{member}", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                if (!result.HasValue)
                {
                    if (Log.instance != null)
                        Log.instance.Debug($"Failed to get offsetof '{member}' in '{type}' (optional)");

                    size = 0;
                    return null;
                }

                optional++;

                size = EvaluationHelpers.TryEvaluateNumberExpression($"sizeof((({type}*)0)->{member})", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects).GetValueOrDefault(0);

                return (ulong)result.Value;
            }

            public static ulong? ReadOptional(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, string type, string member, ref int optional)
            {
                long? result = EvaluationHelpers.TryEvaluateNumberExpression($"(int)&(({type}*)0)->{member}", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                if (!result.HasValue)
                {
                    if (Log.instance != null)
                        Log.instance.Debug($"Failed to get offsetof '{member}' in '{type}' (optional)");

                    return null;
                }

                optional++;
                return (ulong)result.Value;
            }
        }

        public static class LuaStringData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "TString", ref available);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaStringData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public static class LuaValueData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? valueAddress;
            public static ulong? typeAddress;
            public static ulong? doubleAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "TValue", ref available);

                valueAddress = Helper.Read(inspectionSession, thread, frame, "TValue", new[] { "u.i.v__", "value_", "value" }, ref available, ref success, ref failure);
                typeAddress = Helper.Read(inspectionSession, thread, frame, "TValue", new[] { "u.i.tt__", "tt_", "tt" }, ref available, ref success, ref failure);
                doubleAddress = Helper.ReadOptional(inspectionSession, thread, frame, "TValue", "u.d__", ref optional);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaValueData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public static class LuaLocalVariableData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? nameAddress;
            public static ulong? lifetimeStartInstruction;
            public static ulong? lifetimeEndInstruction;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "LocVar", ref available);

                nameAddress = Helper.Read(inspectionSession, thread, frame, "LocVar", "varname", ref available, ref success, ref failure);
                lifetimeStartInstruction = Helper.Read(inspectionSession, thread, frame, "LocVar", "startpc", ref available, ref success, ref failure);
                lifetimeEndInstruction = Helper.Read(inspectionSession, thread, frame, "LocVar", "endpc", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaLocalVariableData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaUpvalueDescriptionData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? nameAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "Upvaldesc", ref available);

                nameAddress = Helper.Read(inspectionSession, thread, frame, "Upvaldesc", "name", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaUpvalueDescriptionData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaUpvalueData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? valueAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "UpVal", ref available);

                valueAddress = Helper.Read(inspectionSession, thread, frame, "UpVal", "v", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaUpvalueData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public static class LuaFunctionData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? argumentCount;
            public static ulong? isVarargs;
            public static ulong? maxStackSize;
            public static ulong? upvalueSize;
            public static ulong? constantSize;
            public static ulong? codeSize;
            public static ulong? lineInfoSize;
            public static ulong? localFunctionSize;
            public static ulong? localVariableSize;
            public static ulong? definitionStartLine;
            public static ulong? definitionEndLine;
            public static ulong? constantDataAddress;
            public static ulong? codeDataAddress;
            public static ulong? localFunctionDataAddress;
            public static ulong? lineInfoDataAddress;
            public static ulong? localVariableDataAddress;
            public static ulong? upvalueDataAddress;
            public static ulong? lastClosureCache;
            public static ulong? sourceAddress;
            public static ulong? gclistAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "Proto", ref available);

                argumentCount = Helper.Read(inspectionSession, thread, frame, "Proto", "numparams", ref available, ref success, ref failure);
                isVarargs = Helper.Read(inspectionSession, thread, frame, "Proto", "is_vararg", ref available, ref success, ref failure);
                maxStackSize = Helper.Read(inspectionSession, thread, frame, "Proto", "maxstacksize", ref available, ref success, ref failure);
                upvalueSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizeupvalues", ref available, ref success, ref failure);
                constantSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizek", ref available, ref success, ref failure);
                codeSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizecode", ref available, ref success, ref failure);
                lineInfoSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizelineinfo", ref available, ref success, ref failure);
                localFunctionSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizep", ref available, ref success, ref failure);
                localVariableSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizelocvars", ref available, ref success, ref failure);
                definitionStartLine = Helper.Read(inspectionSession, thread, frame, "Proto", "linedefined", ref available, ref success, ref failure);
                definitionEndLine = Helper.Read(inspectionSession, thread, frame, "Proto", "lastlinedefined", ref available, ref success, ref failure);
                constantDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "k", ref available, ref success, ref failure);
                codeDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "code", ref available, ref success, ref failure);
                localFunctionDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "p", ref available, ref success, ref failure);
                lineInfoDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "lineinfo", ref available, ref success, ref failure);
                localVariableDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "locvars", ref available, ref success, ref failure);
                upvalueDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "upvalues", ref available, ref success, ref failure);
                lastClosureCache = Helper.Read(inspectionSession, thread, frame, "Proto", "cache", ref available, ref success, ref failure);
                sourceAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "source", ref available, ref success, ref failure);
                gclistAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "gclist", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaFunctionData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaFunctionCallInfoData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? funcAddress;
            public static ulong? previousAddress_5_23;
            public static ulong? stackBaseAddress;
            public static ulong? savedInstructionPointerAddress;
            public static ulong? tailCallCount_5_1;
            public static long callStatus_size = 0;
            public static ulong? callStatus_5_23;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "CallInfo", ref available);

                funcAddress = Helper.Read(inspectionSession, thread, frame, "CallInfo", "func", ref available, ref success, ref failure);
                previousAddress_5_23 = Helper.ReadOptional(inspectionSession, thread, frame, "CallInfo", "previous", ref optional);
                stackBaseAddress = Helper.Read(inspectionSession, thread, frame, "CallInfo", new[] { "u.l.base", "base" }, ref available, ref success, ref failure);
                savedInstructionPointerAddress = Helper.Read(inspectionSession, thread, frame, "CallInfo", new[] { "u.l.savedpc", "savedpc" }, ref available, ref success, ref failure);
                tailCallCount_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "CallInfo", "tailcalls", ref optional);
                callStatus_5_23 = Helper.ReadOptional(inspectionSession, thread, frame, "CallInfo", "callstatus", ref optional, out callStatus_size);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaFunctionCallInfoData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaNodeData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? valueDataAddress;
            public static ulong? keyDataAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "Node", ref available);

                valueDataAddress = Helper.Read(inspectionSession, thread, frame, "Node", "i_val", ref available, ref success, ref failure);
                keyDataAddress = Helper.Read(inspectionSession, thread, frame, "Node", "i_key", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaNodeData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaTableData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? flags;
            public static ulong? nodeArraySizeLog2;
            public static ulong? arraySize;
            public static ulong? arrayDataAddress;
            public static ulong? nodeDataAddress;
            public static ulong? lastFreeNodeDataAddress;
            public static ulong? metaTableDataAddress;
            public static ulong? gclistAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "Table", ref available);

                flags = Helper.Read(inspectionSession, thread, frame, "Table", "flags", ref available, ref success, ref failure);
                nodeArraySizeLog2 = Helper.Read(inspectionSession, thread, frame, "Table", "lsizenode", ref available, ref success, ref failure);
                arraySize = Helper.Read(inspectionSession, thread, frame, "Table", "sizearray", ref available, ref success, ref failure);
                arrayDataAddress = Helper.Read(inspectionSession, thread, frame, "Table", "array", ref available, ref success, ref failure);
                nodeDataAddress = Helper.Read(inspectionSession, thread, frame, "Table", "node", ref available, ref success, ref failure);
                lastFreeNodeDataAddress = Helper.Read(inspectionSession, thread, frame, "Table", "lastfree", ref available, ref success, ref failure);
                metaTableDataAddress = Helper.Read(inspectionSession, thread, frame, "Table", "metatable", ref available, ref success, ref failure);
                gclistAddress = Helper.Read(inspectionSession, thread, frame, "Table", "gclist", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaTableData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaClosureData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? isC_5_1;
            public static ulong? upvalueSize;
            public static ulong? envTableDataAddress_5_1;
            public static ulong? functionAddress;
            public static ulong? firstUpvaluePointerAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "LClosure", ref available);

                isC_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "LClosure", "isC", ref optional);
                upvalueSize = Helper.Read(inspectionSession, thread, frame, "LClosure", "nupvalues", ref available, ref success, ref failure);
                envTableDataAddress_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "LClosure", "env", ref optional);
                functionAddress = Helper.Read(inspectionSession, thread, frame, "LClosure", "p", ref available, ref success, ref failure);
                firstUpvaluePointerAddress = Helper.Read(inspectionSession, thread, frame, "LClosure", "upvals", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaClosureData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaExternalClosureData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? functionAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "CClosure", ref available);

                functionAddress = Helper.Read(inspectionSession, thread, frame, "CClosure", "f", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaExternalClosureData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaUserDataData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? metaTableDataAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "Udata", ref available);

                metaTableDataAddress = Helper.Read(inspectionSession, thread, frame, "Udata", new[] { "uv.metatable", "metatable" }, ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaUserDataData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaStateData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? globalStateAddress;
            public static ulong? callInfoAddress;
            public static ulong? savedProgramCounterAddress_5_1;
            public static ulong? baseCallInfoAddress_5_1;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "lua_State", ref available);

                globalStateAddress = Helper.Read(inspectionSession, thread, frame, "lua_State", "l_G", ref available, ref success, ref failure);
                callInfoAddress = Helper.Read(inspectionSession, thread, frame, "lua_State", "ci", ref available, ref success, ref failure);
                savedProgramCounterAddress_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "lua_State", "savedpc", ref optional);
                baseCallInfoAddress_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "lua_State", "base_ci", ref optional);

                if (savedProgramCounterAddress_5_1.HasValue && baseCallInfoAddress_5_1.HasValue)
                    Helper.looksLike_5_1 = true;

                if (Log.instance != null)
                    Log.instance.Debug($"LuaStateData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class LuaDebugData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? eventType;
            public static ulong? nameAddress;
            public static ulong? nameWhatAddress;
            public static ulong? whatAddress;
            public static ulong? sourceAddress;
            public static ulong? currentLine;
            public static ulong? upvalueSize;
            public static ulong? definitionStartLine;
            public static ulong? definitionEndLine;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "lua_Debug", ref available);

                eventType = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "event", ref available, ref success, ref failure);
                nameAddress = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "name", ref available, ref success, ref failure);
                nameWhatAddress = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "namewhat", ref available, ref success, ref failure);
                whatAddress = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "what", ref available, ref success, ref failure);
                sourceAddress = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "source", ref available, ref success, ref failure);
                currentLine = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "currentline", ref available, ref success, ref failure);
                upvalueSize = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "nups", ref available, ref success, ref failure);
                definitionStartLine = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "linedefined", ref available, ref success, ref failure);
                definitionEndLine = Helper.Read(inspectionSession, thread, frame, "lua_Debug", "lastlinedefined", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaDebugData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }
    }
}
