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

            public static ulong? ReadOptional(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, string type, string member, string message, ref int optional, out long size)
            {
                long? result = EvaluationHelpers.TryEvaluateNumberExpression($"(int)&(({type}*)0)->{member}", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                if (!result.HasValue)
                {
                    if (Log.instance != null)
                        Log.instance.Debug($"Failed to get offsetof '{member}' in '{type}' (optional, {message})");

                    size = 0;
                    return null;
                }

                optional++;

                size = EvaluationHelpers.TryEvaluateNumberExpression($"sizeof((({type}*)0)->{member})", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects).GetValueOrDefault(0);

                return (ulong)result.Value;
            }

            public static ulong? ReadOptional(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame, string type, string member, string message, ref int optional)
            {
                long? result = EvaluationHelpers.TryEvaluateNumberExpression($"(int)&(({type}*)0)->{member}", inspectionSession, thread, frame, DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects);

                if (!result.HasValue)
                {
                    if (Log.instance != null)
                        Log.instance.Debug($"Failed to get offsetof '{member}' in '{type}' (optional, {message})");

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

            public static ulong? offsetToContent_5_4;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "TString", ref available);

                offsetToContent_5_4 = Helper.ReadOptional(inspectionSession, thread, frame, "TString", "contents", "used in 5.4", ref optional);

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
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "TValue", ref available);

                valueAddress = Helper.Read(inspectionSession, thread, frame, "TValue", new[] { "u.i.v__", "value_", "value" }, ref available, ref success, ref failure);
                typeAddress = Helper.Read(inspectionSession, thread, frame, "TValue", new[] { "u.i.tt__", "tt_", "tt" }, ref available, ref success, ref failure);
                doubleAddress = Helper.ReadOptional(inspectionSession, thread, frame, "TValue", "u.d__", "used in NAN trick", ref optional);

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
                success = 0;
                failure = 0;
                optional = 0;

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
                success = 0;
                failure = 0;
                optional = 0;

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
                success = 0;
                failure = 0;
                optional = 0;

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
            public static ulong? maxStackSize_opt;
            public static ulong? upvalueSize;
            public static ulong? constantSize;
            public static ulong? codeSize;
            public static ulong? lineInfoSize;
            public static ulong? absLineInfoSize_5_4;
            public static ulong? localFunctionSize;
            public static ulong? localVariableSize;
            public static ulong? definitionStartLine_opt;
            public static ulong? definitionEndLine_opt;
            public static ulong? constantDataAddress;
            public static ulong? codeDataAddress;
            public static ulong? localFunctionDataAddress;
            public static ulong? lineInfoDataAddress;
            public static ulong? absLineInfoDataAddress_5_4;
            public static ulong? localVariableDataAddress;
            public static ulong? upvalueDataAddress;
            public static ulong? sourceAddress;
            public static ulong? gclistAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "Proto", ref available);

                argumentCount = Helper.Read(inspectionSession, thread, frame, "Proto", "numparams", ref available, ref success, ref failure);
                isVarargs = Helper.Read(inspectionSession, thread, frame, "Proto", "is_vararg", ref available, ref success, ref failure);
                maxStackSize_opt = Helper.ReadOptional(inspectionSession, thread, frame, "Proto", "maxstacksize", "not used", ref optional);
                upvalueSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizeupvalues", ref available, ref success, ref failure);
                constantSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizek", ref available, ref success, ref failure);
                codeSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizecode", ref available, ref success, ref failure);
                lineInfoSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizelineinfo", ref available, ref success, ref failure);
                absLineInfoSize_5_4 = Helper.ReadOptional(inspectionSession, thread, frame, "Proto", "sizeabslineinfo", "used in 5.4", ref optional);
                localFunctionSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizep", ref available, ref success, ref failure);
                localVariableSize = Helper.Read(inspectionSession, thread, frame, "Proto", "sizelocvars", ref available, ref success, ref failure);
                definitionStartLine_opt = Helper.ReadOptional(inspectionSession, thread, frame, "Proto", "linedefined", "used to detect main function", ref optional);
                definitionEndLine_opt = Helper.ReadOptional(inspectionSession, thread, frame, "Proto", "lastlinedefined", "not used", ref optional);
                constantDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "k", ref available, ref success, ref failure);
                codeDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "code", ref available, ref success, ref failure);
                localFunctionDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "p", ref available, ref success, ref failure);
                lineInfoDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "lineinfo", ref available, ref success, ref failure);
                absLineInfoDataAddress_5_4 = Helper.ReadOptional(inspectionSession, thread, frame, "Proto", "abslineinfo", "used in 5.4", ref optional);
                localVariableDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "locvars", ref available, ref success, ref failure);
                upvalueDataAddress = Helper.Read(inspectionSession, thread, frame, "Proto", "upvalues", ref available, ref success, ref failure);
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
            public static ulong? stackBaseAddress_5_123;
            public static ulong? savedInstructionPointerAddress;
            public static ulong? tailCallCount_5_1;
            public static long callStatus_size = 0;
            public static ulong? callStatus_5_23;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "CallInfo", ref available);

                funcAddress = Helper.Read(inspectionSession, thread, frame, "CallInfo", "func", ref available, ref success, ref failure);
                previousAddress_5_23 = Helper.ReadOptional(inspectionSession, thread, frame, "CallInfo", "previous", "used in 5.2/5.3", ref optional);

                // Try to guess if we have Lua 5.4 using new field
                if (!Helper.ReadOptional(inspectionSession, thread, frame, "CallInfo", "u.l.trap", "used to detect 5.4", ref optional).HasValue)
                    stackBaseAddress_5_123 = Helper.Read(inspectionSession, thread, frame, "CallInfo", new[] { "u.l.base", "base" }, ref available, ref success, ref failure);
                else
                    stackBaseAddress_5_123 = null;

                savedInstructionPointerAddress = Helper.Read(inspectionSession, thread, frame, "CallInfo", new[] { "u.l.savedpc", "savedpc" }, ref available, ref success, ref failure);
                tailCallCount_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "CallInfo", "tailcalls", "used in 5.1", ref optional);
                callStatus_5_23 = Helper.ReadOptional(inspectionSession, thread, frame, "CallInfo", "callstatus", "used in 5.2/5.3", ref optional, out callStatus_size);

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

            // Two ways of storing key value
            public static ulong? keyDataAddress_5_123;

            public static ulong? keyDataTypeAddress_5_4;
            public static ulong? keyDataValueAddress_5_4;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "Node", ref available);

                valueDataAddress = Helper.Read(inspectionSession, thread, frame, "Node", "i_val", ref available, ref success, ref failure);

                keyDataTypeAddress_5_4 = Helper.ReadOptional(inspectionSession, thread, frame, "Node", "u.key_tt", "used in Lua 5.4", ref optional);
                keyDataValueAddress_5_4 = Helper.ReadOptional(inspectionSession, thread, frame, "Node", "u.key_val", "used in Lua 5.4", ref optional);

                if (!keyDataTypeAddress_5_4.HasValue || !keyDataValueAddress_5_4.HasValue)
                    keyDataAddress_5_123 = Helper.Read(inspectionSession, thread, frame, "Node", "i_key", ref available, ref success, ref failure);
                else
                    keyDataAddress_5_123 = null;

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
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "Table", ref available);

                flags = Helper.Read(inspectionSession, thread, frame, "Table", "flags", ref available, ref success, ref failure);
                nodeArraySizeLog2 = Helper.Read(inspectionSession, thread, frame, "Table", "lsizenode", ref available, ref success, ref failure);
                arraySize = Helper.Read(inspectionSession, thread, frame, "Table", new[] { "sizearray", "alimit" }, ref available, ref success, ref failure);
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
            public static ulong? upvalueSize_opt;
            public static ulong? envTableDataAddress_5_1;
            public static ulong? functionAddress;
            public static ulong? firstUpvaluePointerAddress;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "LClosure", ref available);

                isC_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "LClosure", "isC", "used in 5.1", ref optional);
                upvalueSize_opt = Helper.ReadOptional(inspectionSession, thread, frame, "LClosure", "nupvalues", "used if avaiable", ref optional);
                envTableDataAddress_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "LClosure", "env", "used in 5.1", ref optional);
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
                success = 0;
                failure = 0;
                optional = 0;

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
                success = 0;
                failure = 0;
                optional = 0;

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

            public static ulong? globalStateAddress_opt;
            public static ulong? callInfoAddress;
            public static ulong? savedProgramCounterAddress_5_1_opt;
            public static ulong? baseCallInfoAddress_5_1;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "lua_State", ref available);

                globalStateAddress_opt = Helper.ReadOptional(inspectionSession, thread, frame, "lua_State", "l_G", "used in Locals Window", ref optional);
                callInfoAddress = Helper.Read(inspectionSession, thread, frame, "lua_State", "ci", ref available, ref success, ref failure);
                savedProgramCounterAddress_5_1_opt = Helper.ReadOptional(inspectionSession, thread, frame, "lua_State", "savedpc", "used in 5.1 (*)", ref optional);
                baseCallInfoAddress_5_1 = Helper.ReadOptional(inspectionSession, thread, frame, "lua_State", "base_ci", "used in 5.1", ref optional);

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

            public static ulong? ici;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;
                success = 0;
                failure = 0;
                optional = 0;

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
                ici = Helper.ReadOptional(inspectionSession, thread, frame, "lua_Debug", "i_ci", "used in luajit", ref optional);

                if (Log.instance != null)
                    Log.instance.Debug($"LuaDebugData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }

        public class Luajit
        {
            public static long valueSize = 0;
            public static long stringSize = 0;
            public static long userdataSize = 0;
            public static long protoSize = 0;
            public static long upvalueSize = 0;
            public static long nodeSize = 0;
            public static long tableSize = 0;
            public static long mrefSize = 0;
            public static long gcrefSize = 0;
            public static long luaStateSize = 0;

            public static ulong? upvalueDataOffset;

            public static bool fullPointer = false;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                bool dummy = false;
                valueSize = Helper.GetSize(inspectionSession, thread, frame, "TValue", ref dummy);
                stringSize = Helper.GetSize(inspectionSession, thread, frame, "GCstr", ref dummy);
                userdataSize = Helper.GetSize(inspectionSession, thread, frame, "GCudata", ref dummy);
                protoSize = Helper.GetSize(inspectionSession, thread, frame, "GCproto", ref dummy);
                upvalueSize = Helper.GetSize(inspectionSession, thread, frame, "GCupval", ref dummy);
                nodeSize = Helper.GetSize(inspectionSession, thread, frame, "Node", ref dummy);
                tableSize = Helper.GetSize(inspectionSession, thread, frame, "GCtab", ref dummy);
                mrefSize = Helper.GetSize(inspectionSession, thread, frame, "MRef", ref dummy);
                gcrefSize = Helper.GetSize(inspectionSession, thread, frame, "GCRef", ref dummy);
                luaStateSize = Helper.GetSize(inspectionSession, thread, frame, "lua_State", ref dummy);

                int optional = 0;
                upvalueDataOffset = Helper.ReadOptional(inspectionSession, thread, frame, "GCupval", "v", "used in LuaJIT", ref optional);

                fullPointer = mrefSize == 8 && gcrefSize == 8;
            }
        }

        public class LuajitStateData
        {
            public static bool available = false;
            public static int success = 0;
            public static int failure = 0;
            public static int optional = 0;

            public static long structSize = 0;

            public static ulong? status;
            public static ulong? glref;
            public static ulong? base_;
            public static ulong? stack;
            public static ulong? env;
            public static ulong? cframe;

            public static void LoadSchema(DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame frame)
            {
                available = true;
                success = 0;
                failure = 0;
                optional = 0;

                structSize = Helper.GetSize(inspectionSession, thread, frame, "lua_State", ref available);

                status = Helper.Read(inspectionSession, thread, frame, "lua_State", "status", ref available, ref success, ref failure);
                glref = Helper.Read(inspectionSession, thread, frame, "lua_State", "glref", ref available, ref success, ref failure);
                base_ = Helper.Read(inspectionSession, thread, frame, "lua_State", "base", ref available, ref success, ref failure);
                stack = Helper.Read(inspectionSession, thread, frame, "lua_State", "stack", ref available, ref success, ref failure);
                env = Helper.Read(inspectionSession, thread, frame, "lua_State", "env", ref available, ref success, ref failure);
                cframe = Helper.Read(inspectionSession, thread, frame, "lua_State", "cframe", ref available, ref success, ref failure);

                if (Log.instance != null)
                    Log.instance.Debug($"LuajitStateData schema {(available ? "available" : "not available")} with {success} successes and {failure} failures and {optional} optional");
            }
        }
    }
}
