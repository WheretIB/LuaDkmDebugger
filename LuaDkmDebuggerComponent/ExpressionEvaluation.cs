using Microsoft.VisualStudio.Debugger;
using System.Diagnostics;

namespace LuaDkmDebuggerComponent
{
    public class ExpressionEvaluation
    {
        public ExpressionEvaluation(DkmProcess process, LuaFunctionData functionData, ulong frameBaseAddress)
        {
            this.process = process;
            this.functionData = functionData;
            this.frameBaseAddress = frameBaseAddress;
        }

        DkmProcess process;
        LuaFunctionData functionData;
        ulong frameBaseAddress;

        private string expression;
        private int pos = 0;

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
                if (char.IsLetterOrDigit(expression[pos + token.Length]))
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

        public LuaValueDataBase LookupVariable(string name)
        {
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

            return Report($"Failed to find variable '{name}'");
        }

        public string TryParseIdentifier()
        {
            SkipSpace();

            if (pos == expression.Length)
                return null;

            if (!char.IsLetter(expression[pos]))
                return null;

            int curr = pos;

            while (curr < expression.Length && char.IsLetterOrDigit(expression[curr]))
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
            if (TryTakeToken("."))
            {
                string name = TryParseIdentifier();

                if (name == null)
                    return Report("Failed to find member name");

                var table = value as LuaValueDataTable;

                if (table == null)
                    return Report("Value is not a table");

                table.value.LoadValues(process);

                foreach (var element in table.value.nodeElements)
                {
                    var keyAsString = element.key as LuaValueDataString;

                    if (keyAsString == null)
                        continue;

                    if (keyAsString.value == name)
                        return EvaluatePostExpressions(element.value);
                }

                return Report($"Failed to find key '{name}' in table");
            }

            if (TryTakeToken("["))
            {
                var table = value as LuaValueDataTable;

                if (table == null)
                    return Report("Value is not a table");

                var index = EvaluateOr();

                if (index as LuaValueDataError != null)
                    return index;

                table.value.LoadValues(process);

                // Check array index
                var indexAsNumber = index as LuaValueDataNumber;

                if (indexAsNumber != null && indexAsNumber.extendedType == LuaExtendedType.IntegerNumber)
                {
                    int result = (int)indexAsNumber.value;

                    if (result > 0 && result - 1 < table.value.arrayElements.Count)
                    {
                        if (!TryTakeToken("]"))
                            return Report("Failed to find ']' after '['");

                        return table.value.arrayElements[result - 1];
                    }
                }

                foreach (var element in table.value.nodeElements)
                {
                    if (element.key.GetType() != index.GetType())
                        continue;

                    if (element.key.LuaCompare(index))
                    {
                        if (!TryTakeToken("]"))
                            return Report("Failed to find ']' after '['");

                        return EvaluatePostExpressions(element.value);
                    }
                }

                return Report($"Failed to find key '{index.AsSimpleDisplayString(10)}' in table");
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

        // not -
        public LuaValueDataBase EvaluateUnary()
        {
            if (TryTakeNamedToken("not"))
            {
                LuaValueDataBase lhs = EvaluateUnary();

                if (lhs as LuaValueDataError != null)
                    return lhs;

                return new LuaValueDataBool(CoerceToBool(lhs));
            }

            if (TryTakeToken("-"))
            {
                LuaValueDataBase lhs = EvaluateUnary();

                if (lhs as LuaValueDataError != null)
                    return lhs;

                var lhsAsNumber = CoerceToNumber(lhs);

                if (lhsAsNumber == null)
                    return Report("value of the unary '-' operator must be a number");

                if (lhsAsNumber.extendedType == LuaExtendedType.IntegerNumber)
                    return new LuaValueDataNumber(-(int)lhsAsNumber.value);

                return new LuaValueDataNumber(-lhsAsNumber.value);
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
                    if (lhsAsNumber.extendedType == LuaExtendedType.IntegerNumber && rhsAsNumber.extendedType == LuaExtendedType.IntegerNumber)
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
                    if (lhsAsNumber.extendedType == LuaExtendedType.IntegerNumber && rhsAsNumber.extendedType == LuaExtendedType.IntegerNumber)
                        return new LuaValueDataNumber((int)lhsAsNumber.value + (int)rhsAsNumber.value);

                    return new LuaValueDataNumber(lhsAsNumber.value + rhsAsNumber.value);
                }

                if (token == "-")
                {
                    if (lhsAsNumber.extendedType == LuaExtendedType.IntegerNumber && rhsAsNumber.extendedType == LuaExtendedType.IntegerNumber)
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

                string lhsString = lhsAsNumber != null ? (lhsAsNumber.extendedType == LuaExtendedType.IntegerNumber ? $"{(int)lhsAsNumber.value}" : $"{lhsAsNumber.value}") : lhsAsString.value;
                string rhsString = rhsAsNumber != null ? (rhsAsNumber.extendedType == LuaExtendedType.IntegerNumber ? $"{(int)rhsAsNumber.value}" : $"{rhsAsNumber.value}") : lhsAsString.value;

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
                        return new LuaValueDataBool(lhsAsNumber.value <= rhsAsNumber.value);

                    if (token == ">=")
                        return new LuaValueDataBool(lhsAsNumber.value >= rhsAsNumber.value);

                    if (token == "<")
                        return new LuaValueDataBool(lhsAsNumber.value < rhsAsNumber.value);

                    if (token == ">")
                        return new LuaValueDataBool(lhsAsNumber.value > rhsAsNumber.value);
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

        public LuaValueDataBase Evaluate(string expression)
        {
            this.expression = expression;
            this.pos = 0;

            LuaValueDataBase value = EvaluateOr();

            if (value as LuaValueDataError != null)
                return value;

            SkipSpace();

            if (pos < expression.Length)
                return Report(value, $"Failed to fully parse at '{expression.Substring(pos)}'");

            return value;
        }
    }
}
