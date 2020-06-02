using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.Evaluation;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace LuaDkmDebuggerComponent
{
    internal class EvaluationHelpers
    {
        internal static string ExecuteExpression(string expression, DkmInspectionSession inspectionSession, DkmThread thread, DkmStackWalkFrame input, DkmEvaluationFlags flags, bool allowZero, out ulong address)
        {
            if (Log.instance != null)
                Log.instance.Verbose($"ExecuteExpression begin evaluation of '{expression}'");

            var compilerId = new DkmCompilerId(DkmVendorId.Microsoft, DkmLanguageId.Cpp);
            var language = DkmLanguage.Create("C++", compilerId);
            var languageExpression = DkmLanguageExpression.Create(language, DkmEvaluationFlags.None, expression, null);

            var inspectionContext = DkmInspectionContext.Create(inspectionSession, input.RuntimeInstance, thread, 200, flags, DkmFuncEvalFlags.None, 10, language, null);

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
                    return $"table [{value.value.arrayElements.Count} element(s) and {value.value.nodeElements.Count} key(s)]";

                if (value.value.arrayElements.Count != 0)
                    return $"table [{value.value.arrayElements.Count} element(s)]";

                if (value.value.nodeElements.Count != 0)
                    return $"table [{value.value.nodeElements.Count} key(s)]";

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

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress:x}";
            }

            if (valueBase as LuaValueDataExternalFunction != null)
            {
                var value = valueBase as LuaValueDataExternalFunction;

                type = "c_function";

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress:x}";
            }

            if (valueBase as LuaValueDataExternalClosure != null)
            {
                var value = valueBase as LuaValueDataExternalClosure;

                type = "c_closure";

                flags |= DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
                return $"0x{value.targetAddress:x}";
            }

            if (valueBase as LuaValueDataUserData != null)
            {
                var value = valueBase as LuaValueDataUserData;

                type = "user_data";

                flags |= DkmEvaluationResultFlags.ReadOnly;
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

        internal static DkmEvaluationResult GetTableChildAtIndex(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string fullName, LuaValueDataTable value, int index)
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
