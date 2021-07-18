using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace LuaDkmDebuggerComponent
{
    public class ExpressionEvaluation
    {
        public ExpressionEvaluation(DkmProcess process, DkmStackWalkFrame stackFrame, DkmInspectionSession inspectionSession, LuaFunctionData functionData, ulong frameBaseAddress, LuaClosureData luaClosure)
        {
            this.process = process;
            this.stackFrame = stackFrame;
            this.inspectionSession = inspectionSession;
            this.functionData = functionData;
            this.frameBaseAddress = frameBaseAddress;
            this.luaClosure = luaClosure;
        }

        DkmProcess process;
        DkmStackWalkFrame stackFrame;
        DkmInspectionSession inspectionSession;
        LuaFunctionData functionData;
        ulong frameBaseAddress;
        LuaClosureData luaClosure;

        private string expression;
        private int pos = 0;
        private bool allowSideEffects = false;

        public void SkipSpace()
        {
            while (pos < expression.Length && expression[pos] <= ' ')
                pos++;
        }

        public bool PeekToken(string token)
        {
            SkipSpace();

            if (string.Compare(expression, pos, token, 0, token.Length) == 0)
                return true;

            return false;
        }

        public bool PeekNamedToken(string token)
        {
            SkipSpace();

            if (string.Compare(expression, pos, token, 0, token.Length) == 0)
            {
                if (pos + token.Length < expression.Length && char.IsLetterOrDigit(expression[pos + token.Length]))
                    return false;

                return true;
            }

            return false;
        }

        public bool TryTakeToken(string token)
        {
            if (PeekToken(token))
            {
                pos += token.Length;
                return true;
            }

            return false;
        }

        public bool TryTakeNamedToken(string token)
        {
            if (PeekNamedToken(token))
            {
                pos += token.Length;
                return true;
            }

            return false;
        }

        public string TryTakeOneOfTokens(string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (TryTakeToken(tokens[i]))
                    return tokens[i];
            }

            return null;
        }

        public LuaValueDataBase Report(string error)
        {
            return new LuaValueDataError(error);
        }

        public LuaValueDataBase Report(LuaValueDataBase value, string error)
        {
            var errorValue = value as LuaValueDataError;

            if (errorValue != null)
            {
                return new LuaValueDataError(errorValue.value + ", " + error);
            }

            return Report(error);
        }

        public bool CoerceToBool(LuaValueDataBase value)
        {
            LuaValueDataNil asNilValue = value as LuaValueDataNil;
            LuaValueDataBool asBoolValue = value as LuaValueDataBool;

            // 'nil' and 'false' are false, everything else is 'true'
            return asNilValue != null ? false : (asBoolValue != null ? asBoolValue.value : true);
        }

        public LuaValueDataNumber CoerceToNumber(LuaValueDataBase value)
        {
            var valueAsNumber = value as LuaValueDataNumber;

            if (valueAsNumber != null)
                return valueAsNumber;

            var valueAsString = value as LuaValueDataString;

            if (valueAsString != null)
            {
                if (!double.TryParse(valueAsString.value, out double result))
                    return null;

                if ((double)(int)result == result)
                    return new LuaValueDataNumber(result);

                return new LuaValueDataNumber(result);
            }

            return null;
        }

        public LuaValueDataBase LookupMetaTableIndex(LuaValueDataTable tableValue, LuaTableData table, LuaValueDataBase index)
        {
            foreach (var element in table.GetMetaTableKeys(process))
            {
                var keyAsString = element.LoadKey(process) as LuaValueDataString;

                if (keyAsString == null)
                    continue;

                if (keyAsString.value == "__index")
                {
                    var indexMetaTableValue = element.LoadValue(process);

                    if (indexMetaTableValue is LuaValueDataTable indexMetaTableValueTable)
                    {
                        return LookupTableElement(indexMetaTableValueTable, index);
                    }
                    else if (indexMetaTableValue is LuaValueDataLuaFunction indexMetaTableValueLuaFunction)
                    {
                        if (tableValue != null)
                            return EvaluateCall(new LuaValueDataBase[] { indexMetaTableValueLuaFunction, tableValue, index });
                        else
                            return Report("Cannot evaluate __index Lua function");
                    }
                    else if (indexMetaTableValue is LuaValueDataExternalClosure indexMetaTableValueExternalClosure)
                    {
                        if (tableValue != null)
                            return EvaluateCall(new LuaValueDataBase[] { indexMetaTableValueExternalClosure, tableValue, index });
                        else
                            return Report("Cannot evaluate __index C closure");
                    }
                }
            }

            return null;
        }

        public LuaValueDataBase LookupTableMember(LuaValueDataTable tableValue, LuaTableData table, string name)
        {
            if (process == null)
                return Report("Can't load table - process memory is not available");

            foreach (var element in table.GetNodeKeys(process))
            {
                var keyAsString = element.LoadKey(process, table.batchNodeElementData) as LuaValueDataString;

                if (keyAsString == null)
                    continue;

                if (keyAsString.value == name)
                    return element.LoadValue(process, table.batchNodeElementData);
            }

            if (table.HasMetaTable())
            {
                LuaValueDataBase result = LookupMetaTableIndex(tableValue, table, new LuaValueDataString(name));

                if (result != null)
                    return result;
            }

            return Report($"Failed to find key '{name}' in table");
        }

        public LuaValueDataBase LookupTableElement(LuaValueDataTable table, LuaValueDataBase index)
        {
            var indexAsNumber = index as LuaValueDataNumber;

            if (indexAsNumber != null && indexAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType())
            {
                int result = (int)indexAsNumber.value;

                if (LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                {
                    if (result >= 0 && result < table.value.GetArrayElementCount(process))
                    {
                        var arrayElements = table.value.GetArrayElements(process);

                        return arrayElements[result];
                    }
                }
                else
                {
                    if (result > 0 && result - 1 < table.value.GetArrayElementCount(process))
                    {
                        var arrayElements = table.value.GetArrayElements(process);

                        return arrayElements[result - 1];
                    }
                }
            }

            foreach (var element in table.value.GetNodeKeys(process))
            {
                var elementKey = element.LoadKey(process, table.value.batchNodeElementData);

                if (elementKey.GetType() != index.GetType())
                    continue;

                if (elementKey.LuaCompare(index))
                {
                    return element.LoadValue(process, table.value.batchNodeElementData);
                }
            }

            if (table.value.HasMetaTable())
            {
                LuaValueDataBase result = LookupMetaTableIndex(table, table.value, index);

                if (result != null)
                    return result;
            }

            return Report($"Failed to find key '{index.AsSimpleDisplayString(10)}' in table");
        }

        public LuaValueDataBase LookupTableValueMember(LuaValueDataBase value, string name)
        {
            var table = value as LuaValueDataTable;

            if (table == null)
                return Report("Value is not a table");

            return LookupTableMember(table, table.value, name);
        }

        public LuaValueDataBase LookupVariable(string name)
        {
            if (process == null || functionData == null)
                return Report($"Can't lookup variable - process memory is not available");

            for (int i = 0; i < functionData.activeLocals.Count; i++)
            {
                var local = functionData.activeLocals[i];

                if (local.name == name)
                {
                    ulong address = frameBaseAddress + (ulong)i * LuaHelpers.GetValueSize(process);

                    var result = LuaHelpers.ReadValue(process, address);

                    if (result == null)
                        return Report($"Failed to read variable '{name}'");

                    return result;
                }
            }

            if (luaClosure != null)
            {
                int envIndex = -1;

                for (int i = 0; i < functionData.upvalues.Count; i++)
                {
                    var upvalue = functionData.upvalues[i];

                    if (upvalue.name == name)
                    {
                        LuaUpvalueData upvalueData = luaClosure.ReadUpvalue(process, i, functionData.upvalueSize);

                        if (upvalueData == null || upvalueData.value == null)
                            return Report($"Failed to read variable '{name}'");

                        return upvalueData.value;
                    }

                    if (upvalue.name == "_ENV")
                        envIndex = i;
                }

                if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit)
                {
                    var envTable = luaClosure.ReadEnvTable_5_1(process);

                    if (envTable == null)
                        return Report($"Failed to read environment value");

                    var value = LookupTableMember(null, envTable, name);

                    if (value as LuaValueDataError == null)
                        return value;
                }
                else
                {
                    // Check _ENV.name
                    if (envIndex != -1)
                    {
                        LuaUpvalueData upvalueData = luaClosure.ReadUpvalue(process, envIndex, functionData.upvalueSize);

                        if (upvalueData == null || upvalueData.value == null)
                            return Report($"Failed to read environment value");

                        var value = LookupTableValueMember(upvalueData.value, name);

                        if (value as LuaValueDataError == null)
                            return value;
                    }
                }
            }

            return Report($"Failed to find variable '{name}'");
        }

        public LuaValueDataBase EvaluateCall(LuaValueDataBase[] args)
        {
            if (!allowSideEffects)
            {
                var error = Report("Expression might have side-effects");

                error.evaluationFlags |= DkmEvaluationResultFlags.UnflushedSideEffects;

                return error;
            }

            if (process == null)
                return Report($"Can't evaluate function - process memory is not available");

            var processData = DebugHelpers.GetOrCreateDataItem<LuaLocalProcessData>(process);

            var parentFrameData = stackFrame.Data.GetDataItem<LuaStackWalkFrameParentData>();

            if (parentFrameData == null)
                return Report($"Can't evaluate function - evaluation frame not available");

            ulong stateAddress = parentFrameData.stateAddress;

            if (stateAddress == 0)
                return Report($"Can't evaluate function - context is not available");

            DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.pauseBreakpoints, null, null).SendLower();

            ulong topAddress = EvaluationHelpers.TryEvaluateAddressExpression($"&((lua_State*){stateAddress})->top", inspectionSession, stackFrame.Thread, parentFrameData.originalFrame, DkmEvaluationFlags.None).GetValueOrDefault(0);

            if (topAddress == 0)
                return Report($"Can't evaluate function - can't get frame top");

            ulong top = DebugHelpers.ReadPointerVariable(process, topAddress).GetValueOrDefault(0);
            ulong originalTop = top;

            if (top == 0)
                return Report($"Can't evaluate function - can't read frame top");

            for (int i = 0; i < args.Length; i++)
            {
                LuaValueDataBase arg = args[i];

                LuaHelpers.GetValueAddressParts(process, top + (ulong)i * LuaHelpers.GetValueSize(process), out ulong tagAddress, out ulong valueAddress);

                if (!LuaHelpers.TryWriteValue(process, stackFrame, inspectionSession, tagAddress, valueAddress, arg, out string errorText))
                {
                    DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.resumeBreakpoints, null, null).SendLower();

                    return Report($"Can't evaluate function - {errorText}");
                }
            }

            DebugHelpers.TryWritePointerVariable(process, topAddress, top + (ulong)args.Length * LuaHelpers.GetValueSize(process));

            long? status = null;

            if (processData.luaPcallAddress != 0)
            {
                status = EvaluationHelpers.TryEvaluateNumberExpression($"((int(*)(void*,int,int,int)){processData.luaPcallAddress})({stateAddress}, {args.Length - 1}, 1, 0)", inspectionSession, stackFrame.Thread, parentFrameData.originalFrame, DkmEvaluationFlags.None);
            }
            else if (processData.luaPcallkAddress != 0)
            {
                status = EvaluationHelpers.TryEvaluateNumberExpression($"((int(*)(void*,int,int,int,int,void*)){processData.luaPcallkAddress})({stateAddress}, {args.Length - 1}, 1, 0, 0, 0)", inspectionSession, stackFrame.Thread, parentFrameData.originalFrame, DkmEvaluationFlags.None);
            }

            DkmCustomMessage.Create(process.Connection, process, MessageToRemote.guid, MessageToRemote.resumeBreakpoints, null, null).SendLower();

            if (!status.HasValue)
            {
                DebugHelpers.TryWritePointerVariable(process, topAddress, originalTop);
                return Report($"Can't evaluate function - failed");
            }

            var result = LuaHelpers.ReadValue(process, originalTop);

            DebugHelpers.TryWritePointerVariable(process, topAddress, originalTop);

            if (result == null)
                return Report($"Can't evaluate function - failed to read result");

            result.evaluationFlags |= DkmEvaluationResultFlags.SideEffect;

            return result;
        }

        public string TryParseIdentifier()
        {
            SkipSpace();

            if (pos == expression.Length)
                return null;

            if (!char.IsLetter(expression[pos]) && expression[pos] != '_')
                return null;

            int curr = pos;

            while (curr < expression.Length && (char.IsLetterOrDigit(expression[curr]) || expression[curr] == '_'))
                curr++;

            string name = expression.Substring(pos, curr - pos);

            pos = curr;

            return name;
        }

        public double? TryParseNumber(out bool canBeAnInteger)
        {
            SkipSpace();

            canBeAnInteger = true;

            if (pos < expression.Length && char.IsDigit(expression[pos]))
            {
                // Try to find number length
                int curr = pos;

                curr++;

                // Hexadecimal number
                if (curr < expression.Length && (expression[curr] == 'x' || expression[curr] == 'X'))
                {
                    curr++;

                    while (curr < expression.Length && (char.IsDigit(expression[curr]) || ((int)char.ToLower(expression[curr]) - (int)'a' < 6)))
                        curr++;

                    if (!int.TryParse(expression.Substring(pos + 2, curr - (pos + 2)), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out int intResult))
                        return null;

                    pos = curr;

                    return intResult;
                }

                while (curr < expression.Length && char.IsDigit(expression[curr]))
                    curr++;

                if (curr < expression.Length && expression[curr] == '.')
                {
                    canBeAnInteger = false;

                    curr++;

                    while (curr < expression.Length && char.IsDigit(expression[curr]))
                        curr++;
                }

                if (curr < expression.Length && (expression[curr] == 'e' || expression[curr] == 'E'))
                {
                    canBeAnInteger = false;

                    curr++;

                    while (curr < expression.Length && char.IsDigit(expression[curr]))
                        curr++;
                }

                if (!double.TryParse(expression.Substring(pos, curr - pos), out double result))
                    return null;

                pos = curr;

                return result;
            }

            return null;
        }

        public LuaValueDataBase EvaluatePostExpressions(LuaValueDataBase value)
        {
            if (TryTakeToken(".") || TryTakeToken(":"))
            {
                string name = TryParseIdentifier();

                if (name == null)
                    return Report("Failed to find member name");

                if (value is LuaValueDataTable table)
                {
                    value = LookupTableMember(table, table.value, name);
                }
                else if (value is LuaValueDataUserData userData)
                {
                    LuaTableData metaTable = userData.value.LoadMetaTable(process);

                    if (metaTable != null)
                    {
                        var indexMetaTableValue = LookupTableMember(null, metaTable, "__index");

                        if (indexMetaTableValue is LuaValueDataTable indexMetaTableValueTable)
                        {
                            value = LookupTableMember(indexMetaTableValueTable, indexMetaTableValueTable.value, name);
                        }
                        else if (indexMetaTableValue is LuaValueDataLuaFunction indexMetaTableValueLuaFunction)
                        {
                            value = EvaluateCall(new LuaValueDataBase[]{ indexMetaTableValueLuaFunction, userData, new LuaValueDataString(name) });
                        }
                        else if (indexMetaTableValue is LuaValueDataExternalClosure indexMetaTableValueExternalClosure)
                        {
                            value = EvaluateCall(new LuaValueDataBase[]{ indexMetaTableValueExternalClosure, userData, new LuaValueDataString(name) });
                        }
                    }
                    else
                    {
                        return Report("Cannot find userdata metatable");
                    }
                }

                if (value as LuaValueDataError != null)
                    return value;

                return EvaluatePostExpressions(value);
            }

            if (TryTakeToken("["))
            {
                var table = value as LuaValueDataTable;

                if (table == null)
                    return Report("Value is not a table");

                var index = EvaluateOr();

                if (index as LuaValueDataError != null)
                    return index;

                if (!TryTakeToken("]"))
                    return Report("Failed to find ']' after '['");

                if (process == null)
                    return Report("Can't load table - process memory is not available");

                value = LookupTableElement(table, index);

                if (value as LuaValueDataError != null)
                    return value;

                return EvaluatePostExpressions(value);
            }

            return value;
        }

        // group variable
        public LuaValueDataBase EvaluateComplexTerminal()
        {
            if (TryTakeToken("("))
            {
                LuaValueDataBase value = EvaluateOr();

                if (value as LuaValueDataError != null)
                    return value;

                if (!TryTakeToken(")"))
                    return Report("Failed to find ')' after '('");

                return EvaluatePostExpressions(value);
            }

            string name = TryParseIdentifier();

            if (name == null)
                return Report("Failed to find variable name");

            var result = LookupVariable(name);

            if (result as LuaValueDataError != null)
                return result;

            return EvaluatePostExpressions(result);
        }

        // nil false true number 'string' "string"
        public LuaValueDataBase EvaluateTerminal()
        {
            if (TryTakeNamedToken("nil"))
            {
                return new LuaValueDataNil();
            }

            if (TryTakeNamedToken("false"))
            {
                return new LuaValueDataBool(false);
            }

            if (TryTakeNamedToken("true"))
            {
                return new LuaValueDataBool(true);
            }

            SkipSpace();

            if (pos < expression.Length && char.IsDigit(expression[pos]))
            {
                double? result = TryParseNumber(out bool canBeAnInteger);

                if (result == null)
                    return Report("Failed to parse a number");

                if (canBeAnInteger && (double)(int)result.Value == result.Value)
                    return new LuaValueDataNumber((int)result.Value);

                return new LuaValueDataNumber(result.Value);
            }

            if (TryTakeToken("\'"))
            {
                int curr = pos;

                while (curr < expression.Length && expression[curr] != '\'')
                    curr++;

                if (curr == expression.Length)
                    return Report("Failed to find end of a string after \'");

                string result = expression.Substring(pos, curr - pos);

                curr++;

                pos = curr;

                return new LuaValueDataString(result);
            }

            if (TryTakeToken("\""))
            {
                int curr = pos;

                while (curr < expression.Length && expression[curr] != '\"')
                    curr++;

                if (curr == expression.Length)
                    return Report("Failed to find end of a string after \"");

                string result = expression.Substring(pos, curr - pos);

                curr++;

                pos = curr;

                return new LuaValueDataString(result);
            }

            return EvaluateComplexTerminal();
        }

        // not - #
        public LuaValueDataBase EvaluateUnary()
        {
            if (TryTakeNamedToken("not"))
            {
                LuaValueDataBase lhs = EvaluateUnary();

                if (lhs as LuaValueDataError != null)
                    return lhs;

                return new LuaValueDataBool(!CoerceToBool(lhs));
            }

            if (TryTakeToken("-"))
            {
                LuaValueDataBase lhs = EvaluateUnary();

                if (lhs as LuaValueDataError != null)
                    return lhs;

                var lhsAsNumber = CoerceToNumber(lhs);

                if (lhsAsNumber == null)
                    return Report("value of the unary '-' operator must be a number");

                if (lhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType())
                    return new LuaValueDataNumber(-(int)lhsAsNumber.value);

                return new LuaValueDataNumber(-lhsAsNumber.value);
            }

            if (TryTakeToken("#"))
            {
                LuaValueDataBase lhs = EvaluateUnary();

                if (lhs as LuaValueDataError != null)
                    return lhs;

                if (process == null)
                    return Report("Can't load value - process memory is not available");

                if (lhs is LuaValueDataTable table)
                {
                    var arrayElements = table.value.GetArrayElements(process);

                    if (arrayElements == null || arrayElements.Count == 0)
                        return new LuaValueDataNumber(0);

                    int start = LuaHelpers.luaVersion == LuaHelpers.luaVersionLuajit ? 1 : 0;

                    for (int i = start; i < arrayElements.Count; i++)
                    {
                        if (arrayElements[i] == null || arrayElements[i].baseType == LuaBaseType.Nil)
                            return new LuaValueDataNumber(i - start);
                    }

                    return new LuaValueDataNumber(arrayElements.Count - start);
                }

                if (lhs is LuaValueDataString str)
                    return new LuaValueDataNumber(str.value.Length);

                return Report("Value is not a table or a string");
            }

            return EvaluateTerminal();
        }

        // * /
        public LuaValueDataBase EvaluateMultiplicative()
        {
            LuaValueDataBase lhs = EvaluateUnary();

            if (lhs as LuaValueDataError != null)
                return lhs;

            string token = TryTakeOneOfTokens(new[] { "*", "/" });

            if (token != null)
            {
                var lhsAsNumber = CoerceToNumber(lhs);

                if (lhsAsNumber == null)
                    return Report("lhs of a numeric binary operator must be a number");

                LuaValueDataBase rhs = EvaluateUnary();

                if (rhs as LuaValueDataError != null)
                    return rhs;

                var rhsAsNumber = CoerceToNumber(rhs);

                if (rhsAsNumber == null)
                    return Report("rhs of a numeric binary operator must be a number");

                if (token == "*")
                {
                    if (lhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType() && rhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType())
                        return new LuaValueDataNumber((int)lhsAsNumber.value * (int)rhsAsNumber.value);

                    return new LuaValueDataNumber(lhsAsNumber.value * rhsAsNumber.value);
                }

                if (token == "/")
                {
                    // Always floating-point
                    return new LuaValueDataNumber(lhsAsNumber.value / rhsAsNumber.value);
                }
            }

            return lhs;
        }

        // + -
        public LuaValueDataBase EvaluateAdditive()
        {
            LuaValueDataBase lhs = EvaluateMultiplicative();

            if (lhs as LuaValueDataError != null)
                return lhs;

            string token = TryTakeOneOfTokens(new[] { "+", "-" });

            if (token != null)
            {
                var lhsAsNumber = CoerceToNumber(lhs);

                if (lhsAsNumber == null)
                    return Report("lhs of a numeric binary operator must be a number");

                LuaValueDataBase rhs = EvaluateMultiplicative();

                if (rhs as LuaValueDataError != null)
                    return rhs;

                var rhsAsNumber = CoerceToNumber(rhs);

                if (rhsAsNumber == null)
                    return Report("rhs of a '+' binary operator must be a number");

                if (token == "+")
                {
                    if (lhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType() && rhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType())
                        return new LuaValueDataNumber((int)lhsAsNumber.value + (int)rhsAsNumber.value);

                    return new LuaValueDataNumber(lhsAsNumber.value + rhsAsNumber.value);
                }

                if (token == "-")
                {
                    if (lhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType() && rhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType())
                        return new LuaValueDataNumber((int)lhsAsNumber.value - (int)rhsAsNumber.value);

                    return new LuaValueDataNumber(lhsAsNumber.value - rhsAsNumber.value);
                }
            }

            return lhs;
        }

        // ..
        public LuaValueDataBase EvaluateConcatenation()
        {
            LuaValueDataBase lhs = EvaluateAdditive();

            if (lhs as LuaValueDataError != null)
                return lhs;

            if (TryTakeToken(".."))
            {
                LuaValueDataBase rhs = EvaluateAdditive();

                if (rhs as LuaValueDataError != null)
                    return rhs;

                var lhsAsNumber = lhs as LuaValueDataNumber;
                var lhsAsString = lhs as LuaValueDataString;

                if (lhsAsNumber == null && lhsAsString == null)
                    return Report("lhs of a concatenation operator must be a number or a string");

                var rhsAsNumber = rhs as LuaValueDataNumber;
                var rhsAsString = rhs as LuaValueDataString;

                if (rhsAsNumber == null && rhsAsString == null)
                    return Report("rhs of a concatenation operator must be a number or a string");

                string lhsString = lhsAsNumber != null ? (lhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType() ? $"{(int)lhsAsNumber.value}" : $"{lhsAsNumber.value}") : lhsAsString.value;
                string rhsString = rhsAsNumber != null ? (rhsAsNumber.extendedType == LuaHelpers.GetIntegerNumberExtendedType() ? $"{(int)rhsAsNumber.value}" : $"{rhsAsNumber.value}") : rhsAsString.value;

                return new LuaValueDataString(lhsString + rhsString);
            }

            return lhs;
        }

        // < > <= >= == ~=
        public LuaValueDataBase EvaluateComparisons()
        {
            LuaValueDataBase lhs = EvaluateConcatenation();

            if (lhs as LuaValueDataError != null)
                return lhs;

            string token = TryTakeOneOfTokens(new[] { "<=", ">=", "==", "~=", "<", ">" });

            if (token != null)
            {
                LuaValueDataBase rhs = EvaluateConcatenation();

                if (rhs as LuaValueDataError != null)
                    return rhs;

                if (token == "==")
                {
                    if (lhs.GetType() != rhs.GetType())
                        return new LuaValueDataBool(false);

                    return new LuaValueDataBool(lhs.LuaCompare(rhs));
                }

                if (token == "~=")
                {
                    if (lhs.GetType() != rhs.GetType())
                        return new LuaValueDataBool(true);

                    return new LuaValueDataBool(!lhs.LuaCompare(rhs));
                }

                // Other relational operators can only be applied to numbers and strings
                var lhsAsNumber = lhs as LuaValueDataNumber;
                var lhsAsString = lhs as LuaValueDataString;

                if (lhsAsNumber == null && lhsAsString == null)
                    return Report("lhs of a comparison operator must be a number or a string");

                var rhsAsNumber = rhs as LuaValueDataNumber;
                var rhsAsString = rhs as LuaValueDataString;

                if (rhsAsNumber == null && rhsAsString == null)
                    return Report("rhs of a comparison operator must be a number or a string");

                if (lhsAsNumber != null)
                {
                    if (rhsAsNumber == null)
                        return Report("lhs of a comparison operator is number but rhs is a string");

                    if (token == "<=")
                        return new LuaValueDataBool(lhsAsNumber.value <= rhsAsNumber.value);

                    if (token == ">=")
                        return new LuaValueDataBool(lhsAsNumber.value >= rhsAsNumber.value);

                    if (token == "<")
                        return new LuaValueDataBool(lhsAsNumber.value < rhsAsNumber.value);

                    if (token == ">")
                        return new LuaValueDataBool(lhsAsNumber.value > rhsAsNumber.value);
                }
                else
                {
                    if (rhsAsString == null)
                        return Report("lhs of a comparison operator is string but rhs is a number");

                    if (token == "<=")
                        return new LuaValueDataBool(lhsAsString.value.CompareTo(rhsAsString.value) <= 0);

                    if (token == ">=")
                        return new LuaValueDataBool(lhsAsString.value.CompareTo(rhsAsString.value) >= 0);

                    if (token == "<")
                        return new LuaValueDataBool(lhsAsString.value.CompareTo(rhsAsString.value) < 0);

                    if (token == ">")
                        return new LuaValueDataBool(lhsAsString.value.CompareTo(rhsAsString.value) > 0);
                }
            }

            return lhs;
        }

        // and
        public LuaValueDataBase EvaluateAnd()
        {
            LuaValueDataBase lhs = EvaluateComparisons();

            if (lhs as LuaValueDataError != null)
                return lhs;

            if (TryTakeNamedToken("and"))
            {
                LuaValueDataBase rhs = EvaluateComparisons();

                if (rhs as LuaValueDataError != null)
                    return rhs;

                return new LuaValueDataBool(CoerceToBool(lhs) && CoerceToBool(rhs));
            }

            return lhs;
        }

        // or
        public LuaValueDataBase EvaluateOr()
        {
            LuaValueDataBase lhs = EvaluateAnd();

            if (lhs as LuaValueDataError != null)
                return lhs;

            if (TryTakeNamedToken("or"))
            {
                LuaValueDataBase rhs = EvaluateAnd();

                if (rhs as LuaValueDataError != null)
                    return rhs;

                return new LuaValueDataBool(CoerceToBool(lhs) || CoerceToBool(rhs));
            }

            return lhs;
        }

        public LuaValueDataBase EvaluateAssignment()
        {
            LuaValueDataBase lhs = EvaluateOr();

            if (lhs as LuaValueDataError != null)
                return lhs;

            if (TryTakeToken("="))
            {
                LuaValueDataBase rhs = EvaluateOr();

                if (rhs as LuaValueDataError != null)
                    return rhs;

                if (!allowSideEffects)
                {
                    var error = Report("Expression has side-effects");

                    error.evaluationFlags |= DkmEvaluationResultFlags.UnflushedSideEffects;

                    return error;
                }

                // lhs must be an l-value
                if (lhs.tagAddress == 0 || lhs.originalAddress == 0)
                    return Report("lhs value cannot be modified");

                // Try to update the value
                if (!LuaHelpers.TryWriteValue(process, stackFrame, inspectionSession, lhs.tagAddress, lhs.originalAddress, rhs, out string errorText))
                    return Report(errorText);

                rhs.evaluationFlags |= DkmEvaluationResultFlags.SideEffect;
                return rhs;
            }

            return lhs;
        }

        public LuaValueDataBase Evaluate(string expression, bool allowSideEffects)
        {
            this.expression = expression;
            this.pos = 0;
            this.allowSideEffects = allowSideEffects;

            LuaValueDataBase value = EvaluateAssignment();

            if (value as LuaValueDataError != null)
                return value;

            SkipSpace();

            if (pos < expression.Length)
                return Report(value, $"Failed to fully parse at '{expression.Substring(pos)}'");

            return value;
        }

        public LuaValueDataBase Evaluate(string expression)
        {
            return Evaluate(expression, false);
        }
    }
}
