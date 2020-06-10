using Microsoft.VisualStudio.Debugger.Evaluation;
using System.Diagnostics;

namespace LuaDkmDebuggerComponent
{
    public abstract class LuaValueDataBase
    {
        public LuaBaseType baseType;
        public LuaExtendedType extendedType;
        public DkmEvaluationResultFlags evaluationFlags;
        public ulong originalAddress;

        public abstract bool LuaCompare(LuaValueDataBase rhs);
        public abstract string GetLuaType();
        public abstract string AsSimpleDisplayString(uint radix);
    }

    [DebuggerDisplay("error ({extendedType})")]
    public class LuaValueDataError : LuaValueDataBase
    {
        public LuaValueDataError()
        {
        }

        public LuaValueDataError(string value)
        {
            baseType = LuaBaseType.Nil;
            extendedType = LuaExtendedType.Nil;
            evaluationFlags = DkmEvaluationResultFlags.ReadOnly;
            originalAddress = 0;
            this.value = value;
        }

        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataError;

            if (rhsAsMe == null)
                return false;

            return value == rhsAsMe.value;
        }

        public override string GetLuaType()
        {
            return "error";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return value;
        }

        public string value;
    }

    [DebuggerDisplay("nil ({extendedType})")]
    public class LuaValueDataNil : LuaValueDataBase
    {
        public LuaValueDataNil()
        {
            baseType = LuaBaseType.Boolean;
            extendedType = LuaExtendedType.Boolean;
            evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
            originalAddress = 0;
        }

        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataNil;

            if (rhsAsMe == null)
                return false;

            return true;
        }

        public override string GetLuaType()
        {
            return "nil";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return "nil";
        }
    }

    [DebuggerDisplay("value = {value} ({extendedType})")]
    public class LuaValueDataBool : LuaValueDataBase
    {
        public LuaValueDataBool()
        {
        }

        public LuaValueDataBool(bool value)
        {
            baseType = LuaBaseType.Boolean;
            extendedType = LuaExtendedType.Boolean;
            evaluationFlags = (value ? DkmEvaluationResultFlags.BooleanTrue : DkmEvaluationResultFlags.None) | DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean | DkmEvaluationResultFlags.ReadOnly;
            originalAddress = 0;
            this.value = value;
        }

        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataBool;

            if (rhsAsMe == null)
                return false;

            return value == rhsAsMe.value;
        }

        public override string GetLuaType()
        {
            return "bool";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return value ? "true" : "false";
        }

        public bool value;
    }

    [DebuggerDisplay("value = 0x{value,x} ({extendedType})")]
    public class LuaValueDataLightUserData : LuaValueDataBase
    {
        public LuaValueDataLightUserData()
        {
        }

        public LuaValueDataLightUserData(ulong value)
        {
            baseType = LuaBaseType.LightUserData;
            extendedType = LuaExtendedType.LightUserData;
            evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
            originalAddress = 0;
            this.value = value;
        }

        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataLightUserData;

            if (rhsAsMe == null)
                return false;

            return value == rhsAsMe.value;
        }

        public override string GetLuaType()
        {
            return "light_user_data";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return $"0x{value:x}";
        }

        public ulong value;
    }

    [DebuggerDisplay("value = {value} ({extendedType})")]
    public class LuaValueDataNumber : LuaValueDataBase
    {
        public LuaValueDataNumber()
        {
        }

        public LuaValueDataNumber(int value)
        {
            baseType = LuaBaseType.Number;
            extendedType = LuaHelpers.GetIntegerNumberExtendedType();
            evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
            originalAddress = 0;
            this.value = value;
        }

        public LuaValueDataNumber(double value)
        {
            baseType = LuaBaseType.Number;
            extendedType = LuaHelpers.GetFloatNumberExtendedType();
            evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
            originalAddress = 0;
            this.value = value;
        }

        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataNumber;

            if (rhsAsMe == null)
                return false;

            return value == rhsAsMe.value;
        }

        public override string GetLuaType()
        {
            return extendedType == LuaHelpers.GetIntegerNumberExtendedType() ? "int" : "double";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            if (extendedType == LuaHelpers.GetIntegerNumberExtendedType())
            {
                if (radix == 16)
                    return $"0x{(int)value:x8}";

                return $"{(int)value}";
            }

            return $"{value}";
        }

        public double value;
    }

    [DebuggerDisplay("value = {value} ({extendedType})")]
    public class LuaValueDataString : LuaValueDataBase
    {
        public LuaValueDataString()
        {
        }

        public LuaValueDataString(string value)
        {
            baseType = LuaBaseType.String;
            extendedType = LuaExtendedType.ShortString;
            evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly;
            originalAddress = 0;
            this.value = value;
        }

        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataString;

            if (rhsAsMe == null)
                return false;

            return value == rhsAsMe.value;
        }

        public override string GetLuaType()
        {
            return extendedType == LuaExtendedType.ShortString ? "short_string" : "long_string";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return $"\"{value}\"";
        }

        public string value;
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    public class LuaValueDataTable : LuaValueDataBase
    {
        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataTable;

            if (rhsAsMe == null)
                return false;

            return value == rhsAsMe.value;
        }

        public override string GetLuaType()
        {
            return "table";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return "{}";
        }

        public LuaTableData value;
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    public class LuaValueDataLuaFunction : LuaValueDataBase
    {
        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataLuaFunction;

            if (rhsAsMe == null)
                return false;

            return targetAddress == rhsAsMe.targetAddress;
        }

        public override string GetLuaType()
        {
            return "lua_function";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return $"0x{targetAddress:x}";
        }

        public LuaClosureData value;
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    public class LuaValueDataExternalFunction : LuaValueDataBase
    {
        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataExternalFunction;

            if (rhsAsMe == null)
                return false;

            return targetAddress == rhsAsMe.targetAddress;
        }

        public override string GetLuaType()
        {
            return "c_function";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return $"0x{targetAddress:x}";
        }

        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    public class LuaValueDataExternalClosure : LuaValueDataBase
    {
        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataExternalClosure;

            if (rhsAsMe == null)
                return false;

            return targetAddress == rhsAsMe.targetAddress;
        }

        public override string GetLuaType()
        {
            return "c_closure";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return $"0x{targetAddress:x}";
        }

        public LuaExternalClosureData value;
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    public class LuaValueDataUserData : LuaValueDataBase
    {
        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataUserData;

            if (rhsAsMe == null)
                return false;

            return targetAddress == rhsAsMe.targetAddress;
        }

        public override string GetLuaType()
        {
            return "user_data";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return $"0x{targetAddress:x}";
        }

        public LuaUserDataData value;
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    public class LuaValueDataThread : LuaValueDataBase
    {
        public override bool LuaCompare(LuaValueDataBase rhs)
        {
            var rhsAsMe = rhs as LuaValueDataThread;

            if (rhsAsMe == null)
                return false;

            return targetAddress == rhsAsMe.targetAddress;
        }

        public override string GetLuaType()
        {
            return "thread";
        }

        public override string AsSimpleDisplayString(uint radix)
        {
            return $"0x{targetAddress:x}";
        }

        public ulong targetAddress;
    }
}
