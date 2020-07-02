using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.DefaultPort;
using Microsoft.VisualStudio.Debugger.Evaluation;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace LuaDkmDebuggerComponent
{
    internal class EvaluationHelpers
    {
        internal static DkmEvaluationResult ExecuteRawExpression(string expression, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame input, DkmRuntimeInstance runtimeInstance, DkmEvaluationFlags flags)
        {
            var compilerId = new DkmCompilerId(DkmVendorId.Microsoft, DkmLanguageId.Cpp);
            var language = DkmLanguage.Create("C++", compilerId);
            var languageExpression = DkmLanguageExpression.Create(language, DkmEvaluationFlags.None, expression, null);

            DkmInspectionContext inspectionContext;

            LuaWorkerConnectionWrapper workerConnectionWrapper = inspectionSession.Process.GetDataItem<LuaWorkerConnectionWrapper>();

            if (workerConnectionWrapper != null)
                inspectionContext = workerConnectionWrapper.CreateInspectionSession(inspectionSession, runtimeInstance, thread, flags, language);
            else
                inspectionContext = DkmInspectionContext.Create(inspectionSession, runtimeInstance, thread, 200, flags, DkmFuncEvalFlags.None, 10, language, null);

            var workList = DkmWorkList.Create(null);

            try
            {
                DkmEvaluationResult result = null;

                inspectionContext.EvaluateExpression(workList, languageExpression, input, res =>
                {
                    if (res.ErrorCode == 0)
                        result = res.ResultObject;
                });

                workList.Execute();

                return result;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        internal static string ExecuteExpression(string expression, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame input, DkmEvaluationFlags flags, bool allowZero, out ulong address)
        {
            if (Log.instance != null)
                Log.instance.Verbose($"ExecuteExpression begin evaluation of '{expression}'");

            var compilerId = new DkmCompilerId(DkmVendorId.Microsoft, DkmLanguageId.Cpp);
            var language = DkmLanguage.Create("C++", compilerId);
            var languageExpression = DkmLanguageExpression.Create(language, DkmEvaluationFlags.None, expression, null);

            DkmInspectionContext inspectionContext;

            LuaWorkerConnectionWrapper workerConnectionWrapper = inspectionSession.Process.GetDataItem<LuaWorkerConnectionWrapper>();

            if (workerConnectionWrapper != null)
                inspectionContext = workerConnectionWrapper.CreateInspectionSession(inspectionSession, input.RuntimeInstance, thread, flags, language);
            else
                inspectionContext = DkmInspectionContext.Create(inspectionSession, input.RuntimeInstance, thread, 200, flags, DkmFuncEvalFlags.None, 10, language, null);

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

                if (Log.instance != null)
                    Log.instance.Verbose($"ExecuteExpression completed");

                address = resultAddress;
                return resultText;
            }
            catch (OperationCanceledException)
            {
                address = 0;
                return null;
            }
        }

        internal static ulong? TryEvaluateAddressExpression(string expression, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame input, DkmEvaluationFlags flags)
        {
            if (ExecuteExpression(expression, inspectionSession, thread, input, flags, true, out ulong address) != null)
                return address;

            return null;
        }

        internal static long? TryEvaluateNumberExpression(string expression, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame input, DkmEvaluationFlags flags)
        {
            string result = ExecuteExpression(expression, inspectionSession, thread, input, flags, true, out _);

            if (result == null)
                return null;

            if (long.TryParse(result, out long value))
                return value;

            return null;
        }

        internal static string TryEvaluateStringExpression(string expression, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame input, DkmEvaluationFlags flags)
        {
            return ExecuteExpression(expression + ",sb", inspectionSession, thread, input, flags, false, out _);
        }

        internal static string EvaluateValueAtLuaValue(DkmProcess process, LuaValueDataBase valueBase, uint radix, out string editableValue, ref DkmEvaluationResultFlags flags, out DkmDataAddress dataAddress, out string type)
        {
            editableValue = null;
            dataAddress = null;
            type = "unknown";

            if (valueBase == null)
                return null;

            if (valueBase as LuaValueDataNil != null)
            {
                type = "nil";

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
                return "nil";
            }

            if (valueBase as LuaValueDataBool != null)
            {
                var value = valueBase as LuaValueDataBool;

                type = "bool";

                if (value.value)
                {
                    flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean | DkmEvaluationResultFlags.BooleanTrue;
                    editableValue = $"{value.value}";
                    return "true";
                }
                else
                {
                    flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean;
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

                if (value.extendedType == LuaHelpers.GetIntegerNumberExtendedType())
                {
                    type = "int";

                    flags |= DkmEvaluationResultFlags.IsBuiltInType;
                    editableValue = $"{value.value}";

                    if (radix == 16)
                        return $"0x{(int)value.value:x8}";

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

                flags |= DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable;

                var arrayElementCount = value.value.GetArrayElementCount(process);
                var nodeElementCount = value.value.GetNodeElementCount(process);

                if (arrayElementCount != 0 && nodeElementCount != 0)
                    return $"0x{value.targetAddress:x} table [{arrayElementCount} element(s) and {nodeElementCount} key(s)]";

                if (arrayElementCount != 0)
                    return $"0x{value.targetAddress:x} table [{arrayElementCount} element(s)]";

                if (nodeElementCount != 0)
                    return $"0x{value.targetAddress:x} table [{nodeElementCount} key(s)]";

                if (!value.value.HasMetaTable())
                {
                    flags &= ~DkmEvaluationResultFlags.Expandable;

                    return $"0x{value.targetAddress:x} table [empty]";
                }

                return $"0x{value.targetAddress:x} table";
            }

            if (valueBase as LuaValueDataLuaFunction != null)
            {
                var value = valueBase as LuaValueDataLuaFunction;

                type = "lua_function";

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable;
                return $"0x{value.targetAddress:x}";
            }

            if (valueBase as LuaValueDataExternalFunction != null)
            {
                var value = valueBase as LuaValueDataExternalFunction;

                type = "c_function";

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable;
                return $"0x{value.targetAddress:x}";
            }

            if (valueBase as LuaValueDataExternalClosure != null)
            {
                var value = valueBase as LuaValueDataExternalClosure;

                type = "c_closure";

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable;
                return $"0x{value.targetAddress:x}";
            }

            if (valueBase as LuaValueDataUserData != null)
            {
                var value = valueBase as LuaValueDataUserData;

                type = "user_data";

                flags |= DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable;
                return $"0x{value.targetAddress:x}";
            }

            if (valueBase as LuaValueDataThread != null)
            {
                var value = valueBase as LuaValueDataThread;

                type = "thread";

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress:x}";
            }

            return null;
        }

        internal static string EvaluateValueAtAddress(DkmProcess process, ulong address, uint radix, out string editableValue, ref DkmEvaluationResultFlags flags, out DkmDataAddress dataAddress, out string type, out LuaValueDataBase luaValueData)
        {
            editableValue = null;
            dataAddress = null;
            type = "unknown";

            luaValueData = LuaHelpers.ReadValue(process, address);

            if (luaValueData == null)
                return null;

            return EvaluateValueAtLuaValue(process, luaValueData, radix, out editableValue, ref flags, out dataAddress, out type);
        }

        internal static DkmEvaluationResult EvaluateDataAtAddress(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string name, string fullName, ulong address, DkmEvaluationResultFlags flags, DkmEvaluationResultAccessType access, DkmEvaluationResultStorageType storage)
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

        internal static DkmEvaluationResult EvaluateDataAtLuaValue(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string name, string fullName, LuaValueDataBase luaValue, DkmEvaluationResultFlags flags, DkmEvaluationResultAccessType access, DkmEvaluationResultStorageType storage)
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

        internal static DkmEvaluationResult EvaluateCppExpression(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string name, string expression)
        {
            DkmEvaluationResult result = ExecuteRawExpression(expression, inspectionContext.InspectionSession, inspectionContext.Thread, stackFrame, inspectionContext.Thread.Process.GetNativeRuntimeInstance(), DkmEvaluationFlags.TreatAsExpression);

            if (result is DkmSuccessEvaluationResult success)
            {
                var renamedResult = DkmSuccessEvaluationResult.Create(success.InspectionContext, success.StackFrame, name, success.FullName, success.Flags, success.Value, success.EditableValue, success.Type, success.Category, success.Access, success.StorageType, success.TypeModifierFlags, success.Address, success.CustomUIVisualizers, success.ExternalModules, success.RefreshButtonText, null);

                result.Close();

                return renamedResult;
            }
            else if (result is DkmFailedEvaluationResult faliure)
            {
                var renamedResult = DkmFailedEvaluationResult.Create(faliure.InspectionContext, faliure.StackFrame, name, faliure.FullName, faliure.ErrorMessage, faliure.Flags, faliure.Type, faliure.Category, null);

                result.Close();

                return renamedResult;
            }

            return DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, name, expression, "Aborted", DkmEvaluationResultFlags.Invalid, "(void*)", null);
        }

        internal static DkmEvaluationResult EvaluateCppValueAtAddress(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string name, string type, ulong address, bool noAddress)
        {
            string expression = $"({type}){address}";

            if (noAddress)
                expression += ",na";

            return EvaluateCppExpression(inspectionContext, stackFrame, name, expression);
        }

        internal static DkmEvaluationResult GetTableChildAtIndex(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string fullName, LuaTableData value, int index)
        {
            if (value == null)
                return DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, $"[{index + 1}]", $"{fullName}[{index + 1}]", "Table data is missing", DkmEvaluationResultFlags.Invalid, null);

            var process = stackFrame.Process;

            var arrayElementCount = value.GetArrayElementCount(process);

            if (index < arrayElementCount)
            {
                var arrayElements = value.GetArrayElements(process);

                var element = arrayElements[index];

                return EvaluateDataAtLuaValue(inspectionContext, stackFrame, $"[{index + 1}]", $"{fullName}[{index + 1}]", element, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
            }

            index = index - arrayElementCount;

            var nodeElementCount = value.GetNodeElementCount(process);

            if (index < nodeElementCount)
            {
                var lazyNodeElements = value.GetNodeLazyElements(process);

                var node = lazyNodeElements[index];

                var nodeKey = node.LoadKey(process, value.batchNodeElementData);

                DkmEvaluationResultFlags flags = DkmEvaluationResultFlags.None;
                string name = EvaluateValueAtLuaValue(process, nodeKey, 10, out _, ref flags, out _, out _);

                var keyString = nodeKey as LuaValueDataString;

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
                    return EvaluateDataAtLuaValue(inspectionContext, stackFrame, name, $"{fullName}.{name}", node.LoadValue(process, value.batchNodeElementData), DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);

                return EvaluateDataAtLuaValue(inspectionContext, stackFrame, $"\"{name}\"", $"{fullName}[\"{name}\"]", node.LoadValue(process, value.batchNodeElementData), DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
            }

            index = index - nodeElementCount;

            if (index == 0 && value.HasMetaTable())
            {
                var metaTableValue = new LuaValueDataTable
                {
                    baseType = LuaBaseType.Table,
                    extendedType = LuaExtendedType.Table,
                    evaluationFlags = DkmEvaluationResultFlags.ReadOnly,
                    originalAddress = 0, // Not available as TValue
                    value = value.GetMetaTable(process),
                    targetAddress = value.metaTableDataAddress
                };

                return EvaluateDataAtLuaValue(inspectionContext, stackFrame, "!metatable", $"{fullName}.!metatable", metaTableValue, DkmEvaluationResultFlags.None, DkmEvaluationResultAccessType.None, DkmEvaluationResultStorageType.None);
            }

            Debug.Assert(false, "Invalid child index");

            return null;
        }

        internal static DkmEvaluationResult GetLuaFunctionChildAtIndex(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string fullName, LuaClosureData value, int index)
        {
            var process = stackFrame.Process;

            var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

            if (index == 0)
            {
                if (value == null)
                    return DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, "[function]", $"{fullName}.!function", "null", DkmEvaluationResultFlags.Invalid, null);

                var functionData = value.ReadFunction(process);

                if (functionData == null)
                    return DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, "[function]", $"{fullName}.!function", "[internal error: failed to read Proto]", DkmEvaluationResultFlags.Invalid, null);

                string source = functionData.ReadSource(process);
                int line = functionData.definitionStartLine_opt;

                DkmEvaluationResultCategory category = DkmEvaluationResultCategory.Method;
                DkmEvaluationResultTypeModifierFlags typeModifiers = DkmEvaluationResultTypeModifierFlags.None;
                DkmEvaluationResultAccessType access = DkmEvaluationResultAccessType.Public;
                DkmEvaluationResultStorageType storage = DkmEvaluationResultStorageType.Global;

                LuaAddressEntityData entityData = new LuaAddressEntityData
                {
                    source = source,
                    line = line,

                    functionAddress = 0,
                    functionInstructionPointer = 0,
                };

                var entityDataBytes = entityData.Encode();

                DkmInstructionAddress instructionAddress = DkmCustomInstructionAddress.Create(processData.runtimeInstance, processData.moduleInstance, entityDataBytes, (ulong)((line << 16) + 0), null, null);

                DkmDataAddress dataAddress = DkmDataAddress.Create(processData.runtimeInstance, value.functionAddress, instructionAddress);

                return DkmSuccessEvaluationResult.Create(inspectionContext, stackFrame, "[function]", $"{fullName}.!function", DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Address, $"{source}:{line}", null, "Proto*", category, access, storage, typeModifiers, dataAddress, null, null, null);
            }

            Debug.Assert(false, "Invalid child index");

            return null;
        }

        internal static DkmInspectionSession CreateInspectionSession(DkmProcess process, DkmThread thread, SupportBreakpointHitMessage data, out DkmStackWalkFrame frame)
        {
            const int CV_ALLREG_VFRAME = 0x00007536;
            var vFrameRegister = DkmUnwoundRegister.Create(CV_ALLREG_VFRAME, new ReadOnlyCollection<byte>(BitConverter.GetBytes(data.vframe)));
            var registers = thread.GetCurrentRegisters(new[] { vFrameRegister });
            var instructionAddress = process.CreateNativeInstructionAddress(registers.GetInstructionPointer());
            frame = DkmStackWalkFrame.Create(thread, instructionAddress, data.frameBase, 0, DkmStackWalkFrameFlags.None, null, registers, null);

            return DkmInspectionSession.Create(process, null);
        }
    }
}
