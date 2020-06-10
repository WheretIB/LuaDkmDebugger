using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Evaluation;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace LuaDkmDebuggerComponent
{
    public enum LuaBaseType
    {
        Nil,
        Boolean,
        LightUserData,
        Number,
        String,
        Table,
        Function,
        UserData,
        Thread
    }

    public enum LuaExtendedType
    {
        Nil = LuaBaseType.Nil,

        Boolean = LuaBaseType.Boolean,
        BooleanTrue = LuaBaseType.Boolean + 16, // Lua 5.4

        LightUserData = LuaBaseType.LightUserData,

        ShortString = LuaBaseType.String,
        LongString = LuaBaseType.String + 16,

        Table = LuaBaseType.Table,

        LuaFunction = LuaBaseType.Function,
        ExternalFunction = LuaBaseType.Function + 16,
        ExternalClosure = LuaBaseType.Function + 32,

        UserData = LuaBaseType.UserData,
        Thread = LuaBaseType.Thread,
    }

    static class LuaHelpers
    {
        public static int luaVersion = 0;

        internal static LuaBaseType GetBaseType(int typeTag)
        {
            return (LuaBaseType)(typeTag & 0xf);
        }

        internal static LuaExtendedType GetExtendedType(int typeTag)
        {
            return (LuaExtendedType)(typeTag & 0x3f);
        }

        internal static ulong GetStringDataOffset(DkmProcess process)
        {
            if (Schema.LuaStringData.available)
            {
                if (Schema.LuaStringData.offsetToContent_5_4.HasValue)
                    return Schema.LuaStringData.offsetToContent_5_4.Value;

                return (ulong)Schema.LuaStringData.structSize;
            }

            // Same in Lua 5.1, 5.2, 5.3 and 5.4
            return (ulong)DebugHelpers.GetPointerSize(process) * 2 + 8;
        }

        internal static ulong GetValueSize(DkmProcess process)
        {
            if (Schema.LuaValueData.available)
                return (ulong)Schema.LuaValueData.structSize;

            if (LuaHelpers.luaVersion == 502)
                return DebugHelpers.Is64Bit(process) ? 16u : 8u;

            // Same in Lua 5.1, 5.3 and 5.4
            return 16u;
        }

        internal static ulong GetNodeSize(DkmProcess process)
        {
            if (Schema.LuaNodeData.available)
                return (ulong)Schema.LuaNodeData.structSize;

            if (LuaHelpers.luaVersion == 501)
                return DebugHelpers.Is64Bit(process) ? 40u : 32u;

            if (LuaHelpers.luaVersion == 502)
                return DebugHelpers.Is64Bit(process) ? 40u : 24u;

            if (LuaHelpers.luaVersion == 503)
                return 32u;

            return 24u;
        }

        internal static LuaExtendedType GetFloatNumberExtendedType()
        {
            if (LuaHelpers.luaVersion == 504)
                return (LuaExtendedType)(LuaBaseType.Number + 16);

            return (LuaExtendedType)LuaBaseType.Number;
        }

        internal static LuaExtendedType GetIntegerNumberExtendedType()
        {
            if (LuaHelpers.luaVersion == 504)
                return (LuaExtendedType)LuaBaseType.Number;

            return (LuaExtendedType)(LuaBaseType.Number + 16);
        }

        internal static LuaValueDataBase ReadValue(DkmProcess process, ulong address)
        {
            int? typeTag;
            ulong valueAddress = address;

            if (Schema.LuaValueData.available)
            {
                // Handle NAN trick
                if (Schema.LuaValueData.doubleAddress.HasValue)
                {
                    double? value = DebugHelpers.ReadDoubleVariable(process, address + Schema.LuaValueData.doubleAddress.GetValueOrDefault(0));

                    if (value == null)
                        return null;

                    if (double.IsNaN(value.Value))
                        typeTag = DebugHelpers.ReadIntVariable(process, address + Schema.LuaValueData.typeAddress.GetValueOrDefault(0));
                    else
                        typeTag = (int)GetFloatNumberExtendedType();
                }
                else
                {
                    typeTag = DebugHelpers.ReadIntVariable(process, address + Schema.LuaValueData.typeAddress.GetValueOrDefault(0));
                }

                valueAddress = address + Schema.LuaValueData.valueAddress.GetValueOrDefault(0);
            }
            else if (luaVersion == 502 && !DebugHelpers.Is64Bit(process))
            {
                // union { struct { Value v__; int tt__; } i; double d__; } u
                double? value = DebugHelpers.ReadDoubleVariable(process, address);

                if (value == null)
                    return null;

                if (double.IsNaN(value.Value))
                    typeTag = DebugHelpers.ReadIntVariable(process, address + (ulong)DebugHelpers.GetPointerSize(process));
                else
                    typeTag = (int)GetFloatNumberExtendedType();
            }
            else
            {
                // Same in Lua 5.1, 5.3 and 5.4
                // struct { Value value_; int tt_; }
                typeTag = DebugHelpers.ReadIntVariable(process, address + 8);
            }

            if (typeTag == null)
                return null;

            return ReadValueOfType(process, typeTag.Value, valueAddress);
        }

        internal static LuaValueDataBase ReadValueOfType(DkmProcess process, int typeTag, ulong address)
        {
            var extenedType = GetExtendedType(typeTag);

            if (extenedType == LuaExtendedType.Nil)
            {
                return new LuaValueDataNil()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.Boolean)
            {
                if (LuaHelpers.luaVersion == 504)
                {
                    return new LuaValueDataBool()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean | DkmEvaluationResultFlags.ReadOnly,
                        originalAddress = address,
                        value = false
                    };
                }

                var value = DebugHelpers.ReadIntVariable(process, address);

                if (value.HasValue)
                {
                    return new LuaValueDataBool()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = value.Value != 0 ? DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean | DkmEvaluationResultFlags.BooleanTrue : DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean,
                        originalAddress = address,
                        value = value.Value != 0
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.BooleanTrue)
            {
                if (LuaHelpers.luaVersion == 504)
                {
                    return new LuaValueDataBool()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean | DkmEvaluationResultFlags.BooleanTrue | DkmEvaluationResultFlags.ReadOnly,
                        originalAddress = address,
                        value = true
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.LightUserData)
            {
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    return new LuaValueDataLightUserData()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType,
                        originalAddress = address,
                        value = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == GetFloatNumberExtendedType())
            {
                var value = DebugHelpers.ReadDoubleVariable(process, address);

                if (value.HasValue)
                {
                    return new LuaValueDataNumber()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType,
                        originalAddress = address,
                        value = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == GetIntegerNumberExtendedType())
            {
                var value = DebugHelpers.ReadIntVariable(process, address);

                if (value.HasValue)
                {
                    return new LuaValueDataNumber()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType,
                        originalAddress = address,
                        value = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.ShortString)
            {
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    ulong luaStringOffset = LuaHelpers.GetStringDataOffset(process);

                    var target = DebugHelpers.ReadStringVariable(process, value.Value + luaStringOffset, 256);

                    if (target != null)
                    {
                        return new LuaValueDataString()
                        {
                            baseType = GetBaseType(typeTag),
                            extendedType = GetExtendedType(typeTag),
                            evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.RawString | DkmEvaluationResultFlags.ReadOnly,
                            originalAddress = address,
                            value = target,
                            targetAddress = value.Value + luaStringOffset
                        };
                    }
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.LongString)
            {
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    ulong luaStringOffset = LuaHelpers.GetStringDataOffset(process);

                    var target = DebugHelpers.ReadStringVariable(process, value.Value + luaStringOffset, 256);

                    if (target != null)
                    {
                        return new LuaValueDataString()
                        {
                            baseType = GetBaseType(typeTag),
                            extendedType = GetExtendedType(typeTag),
                            evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.RawString | DkmEvaluationResultFlags.ReadOnly,
                            originalAddress = address,
                            value = target,
                            targetAddress = value.Value + luaStringOffset
                        };
                    }
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.Table)
            {
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    LuaTableData target = new LuaTableData();

                    target.ReadFrom(process, value.Value);

                    return new LuaValueDataTable()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable,
                        originalAddress = address,
                        value = target,
                        targetAddress = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.LuaFunction)
            {
                // Read pointer to GCObject from address
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    LuaClosureData target = new LuaClosureData();

                    target.ReadFrom(process, value.Value);

                    return new LuaValueDataLuaFunction()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                        originalAddress = address,
                        value = target,
                        targetAddress = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.ExternalFunction)
            {
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    return new LuaValueDataExternalFunction()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                        originalAddress = address,
                        targetAddress = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.ExternalClosure)
            {
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    LuaExternalClosureData target = new LuaExternalClosureData();

                    target.ReadFrom(process, value.Value);

                    return new LuaValueDataExternalClosure()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                        originalAddress = address,
                        value = target,
                        targetAddress = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.UserData)
            {
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    LuaUserDataData target = new LuaUserDataData();

                    target.ReadFrom(process, value.Value);

                    return new LuaValueDataUserData()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.ReadOnly,
                        originalAddress = address,
                        value = target,
                        targetAddress = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            if (extenedType == LuaExtendedType.Thread)
            {
                var value = DebugHelpers.ReadPointerVariable(process, address);

                if (value.HasValue)
                {
                    return new LuaValueDataThread()
                    {
                        baseType = GetBaseType(typeTag),
                        extendedType = GetExtendedType(typeTag),
                        evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                        originalAddress = address,
                        targetAddress = value.Value
                    };
                }

                return new LuaValueDataError()
                {
                    baseType = GetBaseType(typeTag),
                    extendedType = GetExtendedType(typeTag),
                    evaluationFlags = DkmEvaluationResultFlags.None,
                    originalAddress = address
                };
            }

            return null;
        }
    }

    public class LuaLocalVariableData
    {
        public ulong nameAddress; // TString
        public string name;

        public int lifetimeStartInstruction;
        public int lifetimeEndInstruction;

        public static int StructSize(DkmProcess process)
        {
            if (Schema.LuaLocalVariableData.available)
                return (int)Schema.LuaLocalVariableData.structSize;

            return DebugHelpers.Is64Bit(process) ? 16 : 12;
        }

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaLocalVariableData.available)
            {
                nameAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaLocalVariableData.nameAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                lifetimeStartInstruction = DebugHelpers.ReadIntVariable(process, address + Schema.LuaLocalVariableData.lifetimeStartInstruction.GetValueOrDefault(0)).GetValueOrDefault(0);
                lifetimeEndInstruction = DebugHelpers.ReadIntVariable(process, address + Schema.LuaLocalVariableData.lifetimeEndInstruction.GetValueOrDefault(0)).GetValueOrDefault(0);
            }
            else
            {
                // Same in Lua 5.1, 5.2, 5.3 and 5.4
                nameAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += (ulong)DebugHelpers.GetPointerSize(process);

                lifetimeStartInstruction = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                lifetimeEndInstruction = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
            }

            if (nameAddress != 0)
            {
                try
                {
                    byte[] nameData = process.ReadMemoryString(nameAddress + LuaHelpers.GetStringDataOffset(process), DkmReadMemoryFlags.None, 1, 256);

                    if (nameData != null && nameData.Length != 0)
                        name = System.Text.Encoding.UTF8.GetString(nameData, 0, nameData.Length - 1);
                    else
                        name = "failed_to_read_name";
                }
                catch (DkmException)
                {
                    name = "failed_to_read_name";
                }
            }
            else
            {
                name = "nil";
            }
        }
    }

    public class LuaUpvalueDescriptionData
    {
        public ulong nameAddress; // TString
        public string name;

        // Not interested in other data

        public static int StructSize(DkmProcess process)
        {
            if (Schema.LuaUpvalueDescriptionData.available)
                return (int)Schema.LuaUpvalueDescriptionData.structSize;

            if (LuaHelpers.luaVersion == 501)
                return DebugHelpers.GetPointerSize(process);

            return DebugHelpers.GetPointerSize(process) * 2;
        }

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaUpvalueDescriptionData.available)
            {
                nameAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaUpvalueDescriptionData.nameAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
            }
            else if (LuaHelpers.luaVersion == 501)
            {
                nameAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
            }
            else
            {
                // Same in Lua 5.2, 5.3 and 5.4
                nameAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                // Not interested in other data
            }

            if (nameAddress != 0)
            {
                try
                {
                    byte[] nameData = process.ReadMemoryString(nameAddress + LuaHelpers.GetStringDataOffset(process), DkmReadMemoryFlags.None, 1, 256);

                    if (nameData != null && nameData.Length != 0)
                        name = System.Text.Encoding.UTF8.GetString(nameData, 0, nameData.Length - 1);
                    else
                        name = "failed_to_read_name";
                }
                catch (DkmException)
                {
                    name = "failed_to_read_name";
                }
            }
            else
            {
                name = "nil";
            }
        }
    }

    public class LuaUpvalueData
    {
        public ulong valueAddress;
        public LuaValueDataBase value;

        // Not interested in other data

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaUpvalueData.available)
            {
                valueAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaUpvalueData.valueAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
            }
            else if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == 502)
            {
                // Same in Lua 5.1 and 5.2

                // Skip CommonHeader
                DebugHelpers.SkipStructPointer(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);

                valueAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
            }
            else if (LuaHelpers.luaVersion == 503)
            {
                valueAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
            }
            else if (LuaHelpers.luaVersion == 504)
            {
                // Skip CommonHeader
                DebugHelpers.SkipStructPointer(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);

                DebugHelpers.SkipStructByte(process, ref address); // tbc

                valueAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
            }

            if (valueAddress != 0)
                value = LuaHelpers.ReadValue(process, valueAddress);
        }
    }

    public class LuaFunctionData
    {
        public ulong originalAddress;

        public byte argumentCount;
        public byte isVarargs;
        public byte maxStackSize;
        // 3 byte padding!
        public int upvalueSize;
        public int constantSize;
        public int codeSize;
        public int lineInfoSize;
        public int? absLineInfoSize_5_4;
        public int localFunctionSize;
        public int localVariableSize;
        public int definitionStartLine_opt;
        public int definitionEndLine_opt;
        public ulong constantDataAddress; // TValue[]
        public ulong codeDataAddress; // Opcode list (unsigned[])
        public ulong localFunctionDataAddress; // (Proto*[])
        public ulong lineInfoDataAddress; // For each opcode (int[])
        public ulong absLineInfoDataAddress_5_4; // AbsLineInfo[]
        public ulong localVariableDataAddress; // LocVar[]
        public ulong upvalueDataAddress; // Upvaldesc[]
        public ulong sourceAddress; // TString
        public ulong gclistAddress; // GCObject

        public List<LuaLocalVariableData> locals;
        public List<LuaLocalVariableData> activeLocals;

        public List<LuaFunctionData> localFunctions;
        public int[] lineInfo;
        public int[] absLineInfo;

        public string source;

        public List<LuaUpvalueDescriptionData> upvalues;

        public bool hasDefinitionLineInfo = false;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            originalAddress = address;

            ulong pointerSize = (ulong)DebugHelpers.GetPointerSize(process);

            if (Schema.LuaFunctionData.available)
            {
                constantDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.constantDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                codeDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.codeDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                localFunctionDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.localFunctionDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                lineInfoDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.lineInfoDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaFunctionData.absLineInfoDataAddress_5_4.HasValue)
                    absLineInfoDataAddress_5_4 = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.absLineInfoDataAddress_5_4.GetValueOrDefault(0)).GetValueOrDefault(0);

                localVariableDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.localVariableDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                upvalueDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.upvalueDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                sourceAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.sourceAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                upvalueSize = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.upvalueSize.GetValueOrDefault(0)).GetValueOrDefault(0);
                constantSize = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.constantSize.GetValueOrDefault(0)).GetValueOrDefault(0);
                codeSize = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.codeSize.GetValueOrDefault(0)).GetValueOrDefault(0);
                lineInfoSize = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.lineInfoSize.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaFunctionData.absLineInfoSize_5_4.HasValue)
                    absLineInfoSize_5_4 = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.absLineInfoSize_5_4.GetValueOrDefault(0));

                localFunctionSize = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.localFunctionSize.GetValueOrDefault(0)).GetValueOrDefault(0);
                localVariableSize = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.localVariableSize.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaFunctionData.definitionStartLine_opt.HasValue)
                    definitionStartLine_opt = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.definitionStartLine_opt.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaFunctionData.definitionEndLine_opt.HasValue)
                    definitionEndLine_opt = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionData.definitionEndLine_opt.GetValueOrDefault(0)).GetValueOrDefault(0);

                gclistAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionData.gclistAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                argumentCount = DebugHelpers.ReadByteVariable(process, address + Schema.LuaFunctionData.argumentCount.GetValueOrDefault(0)).GetValueOrDefault(0);
                isVarargs = DebugHelpers.ReadByteVariable(process, address + Schema.LuaFunctionData.isVarargs.GetValueOrDefault(0)).GetValueOrDefault(0);
                maxStackSize = DebugHelpers.ReadByteVariable(process, address + Schema.LuaFunctionData.maxStackSize.GetValueOrDefault(0)).GetValueOrDefault(0);

                hasDefinitionLineInfo = Schema.LuaFunctionData.definitionStartLine_opt.HasValue && Schema.LuaFunctionData.definitionEndLine_opt.HasValue;
            }
            else if (LuaHelpers.luaVersion == 501)
            {
                // Skip CommonHeader
                DebugHelpers.SkipStructPointer(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);

                constantDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                codeDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                localFunctionDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                lineInfoDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                localVariableDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                upvalueDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                sourceAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                upvalueSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                constantSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                codeSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                lineInfoSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                localFunctionSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                localVariableSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionStartLine_opt = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionEndLine_opt = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);

                gclistAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                DebugHelpers.SkipStructByte(process, ref address); // nups
                argumentCount = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                isVarargs = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                maxStackSize = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);

                hasDefinitionLineInfo = true;
            }
            else if (LuaHelpers.luaVersion == 502)
            {
                // Skip CommonHeader
                DebugHelpers.SkipStructPointer(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);

                constantDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                codeDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                localFunctionDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                lineInfoDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                localVariableDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                upvalueDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                DebugHelpers.SkipStructPointer(process, ref address); // last closure cache
                sourceAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                upvalueSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                constantSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                codeSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                lineInfoSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                localFunctionSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                localVariableSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionStartLine_opt = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionEndLine_opt = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);

                gclistAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                argumentCount = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                isVarargs = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                maxStackSize = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);

                hasDefinitionLineInfo = true;
            }
            else if (LuaHelpers.luaVersion == 503)
            {
                address += pointerSize; // Skip CommonHeader
                address += 2;

                argumentCount = DebugHelpers.ReadByteVariable(process, address).GetValueOrDefault(0);
                address += sizeof(byte);
                isVarargs = DebugHelpers.ReadByteVariable(process, address).GetValueOrDefault(0);
                address += sizeof(byte);
                maxStackSize = DebugHelpers.ReadByteVariable(process, address).GetValueOrDefault(0);
                address += sizeof(byte);
                address += 3; // Padding

                Debug.Assert((address & 0x3) == 0);

                upvalueSize = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                constantSize = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                codeSize = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                lineInfoSize = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                localFunctionSize = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                localVariableSize = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                definitionStartLine_opt = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                definitionEndLine_opt = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);

                constantDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
                codeDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
                localFunctionDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
                lineInfoDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
                localVariableDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
                upvalueDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;

                DebugHelpers.SkipStructPointer(process, ref address); // last closure cache

                sourceAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
                gclistAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;

                hasDefinitionLineInfo = true;
            }
            else
            {
                // Skip CommonHeader
                DebugHelpers.SkipStructPointer(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);
                DebugHelpers.SkipStructByte(process, ref address);

                argumentCount = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                isVarargs = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                maxStackSize = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);

                upvalueSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                constantSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                codeSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                lineInfoSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                localFunctionSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                localVariableSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                absLineInfoSize_5_4 = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionStartLine_opt = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionEndLine_opt = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);

                constantDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                codeDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                localFunctionDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                upvalueDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                lineInfoDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                absLineInfoDataAddress_5_4 = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                localVariableDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                sourceAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                gclistAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                hasDefinitionLineInfo = true;
            }

            // Sanity checks to 'guess' if we have wrong function data
            Debug.Assert(lineInfoSize >= 0 && lineInfoSize < 1000000);

            Debug.Assert(localFunctionSize >= 0 && localFunctionSize < 10000);
            Debug.Assert(localVariableSize >= 0 && localVariableSize < 10000);
            Debug.Assert(upvalueSize >= 0 && upvalueSize < 10000);
        }

        public void ReadLocals(DkmProcess process, int instructionPointer)
        {
            // Check if alraedy loaded
            if (locals != null)
                return;

            locals = new List<LuaLocalVariableData>();
            activeLocals = new List<LuaLocalVariableData>();

            for (int i = 0; i < localVariableSize; i++)
            {
                LuaLocalVariableData local = new LuaLocalVariableData();

                local.ReadFrom(process, localVariableDataAddress + (ulong)(i * LuaLocalVariableData.StructSize(process)));

                locals.Add(local);

                if (i < argumentCount || instructionPointer == -1)
                {
                    activeLocals.Add(local);
                }
                else
                {
                    if (instructionPointer >= local.lifetimeStartInstruction && instructionPointer < local.lifetimeEndInstruction)
                        activeLocals.Add(local);
                }
            }
        }

        public void UpdateLocals(DkmProcess process, int instructionPointer)
        {
            if (locals == null)
                ReadLocals(process, -1);

            activeLocals.Clear();

            for (int i = 0; i < localVariableSize; i++)
            {
                LuaLocalVariableData local = locals[i];

                if (i < argumentCount || instructionPointer == -1)
                {
                    activeLocals.Add(local);
                }
                else
                {
                    if (instructionPointer >= local.lifetimeStartInstruction && instructionPointer < local.lifetimeEndInstruction)
                        activeLocals.Add(local);
                }
            }
        }

        public void ReadLocalFunctions(DkmProcess process)
        {
            if (localFunctions != null)
                return;

            localFunctions = new List<LuaFunctionData>();

            for (int i = 0; i < localFunctionSize; i++)
            {
                LuaFunctionData data = new LuaFunctionData();

                var targetAddress = DebugHelpers.ReadPointerVariable(process, localFunctionDataAddress + (ulong)(i * DebugHelpers.GetPointerSize(process)));

                if (!targetAddress.HasValue)
                    continue;

                data.ReadFrom(process, targetAddress.Value);

                localFunctions.Add(data);
            }
        }

        public int[] ReadLineInfo(DkmProcess process)
        {
            if (lineInfo != null)
                return lineInfo;

            lineInfo = new int[lineInfoSize];

            if (absLineInfoSize_5_4.HasValue)
            {
                // TODO: block read
                for (int i = 0; i < lineInfoSize; i++)
                    lineInfo[i] = DebugHelpers.ReadByteVariable(process, lineInfoDataAddress + (ulong)i).GetValueOrDefault(0);
            }
            else
            {
                // TODO: block read
                for (int i = 0; i < lineInfoSize; i++)
                    lineInfo[i] = DebugHelpers.ReadIntVariable(process, lineInfoDataAddress + (ulong)i * 4u).GetValueOrDefault(0);
            }

            return lineInfo;
        }

        public int[] ReadAbsoluteLineInfo(DkmProcess process)
        {
            if (absLineInfo != null)
                return absLineInfo;

            if (!absLineInfoSize_5_4.HasValue)
                return null;

            absLineInfo = new int[absLineInfoSize_5_4.Value * 2];

            // TODO: block read
            for (int i = 0; i < absLineInfoSize_5_4.Value * 2; i++)
                absLineInfo[i] = DebugHelpers.ReadIntVariable(process, absLineInfoDataAddress_5_4 + (ulong)i * 4u).GetValueOrDefault(0);

            return absLineInfo;
        }

        public int ReadLineInfoFor(DkmProcess process, int instructionPointer)
        {
            Debug.Assert(instructionPointer < lineInfoSize);

            if (instructionPointer >= lineInfoSize)
                return 0;

            if (absLineInfoSize_5_4.HasValue)
            {
                // We need all data for this
                ReadLineInfo(process);
                ReadAbsoluteLineInfo(process);

                int baseInstructionPointer = 0;
                int baseLine = 0;

                if (absLineInfoSize_5_4.Value == 0 || instructionPointer < absLineInfo[0])
                {
                    baseInstructionPointer = -1;
                    baseLine = definitionStartLine_opt;
                }
                else if (instructionPointer >= absLineInfo[(absLineInfoSize_5_4.Value - 1) * 2 + 0])
                {
                    baseInstructionPointer = absLineInfo[(absLineInfoSize_5_4.Value - 1) * 2 + 0];
                    baseLine = absLineInfo[(absLineInfoSize_5_4.Value - 1) * 2 + 1];
                }
                else
                {
                    for (int i = 0; i < absLineInfoSize_5_4.Value; i++)
                    {
                        if (absLineInfo[i * 2 + 0] >= instructionPointer)
                        {
                            baseInstructionPointer = absLineInfo[i * 2 + 0];
                            baseLine = absLineInfo[i * 2 + 1];
                        }
                    }
                }

                while (baseInstructionPointer++ < instructionPointer)
                    baseLine += (sbyte)lineInfo[baseInstructionPointer];

                return baseLine;
            }

            return DebugHelpers.ReadIntVariable(process, lineInfoDataAddress + (ulong)instructionPointer * 4).GetValueOrDefault(0);
        }

        public string ReadSource(DkmProcess process)
        {
            if (source != null)
                return source;

            source = DebugHelpers.ReadStringVariable(process, sourceAddress + LuaHelpers.GetStringDataOffset(process), 1024);

            return source;
        }

        public void ReadUpvalues(DkmProcess process)
        {
            // Check if alraedy loaded
            if (upvalues != null)
                return;

            upvalues = new List<LuaUpvalueDescriptionData>();

            for (int i = 0; i < upvalueSize; i++)
            {
                LuaUpvalueDescriptionData upvalue = new LuaUpvalueDescriptionData();

                upvalue.ReadFrom(process, upvalueDataAddress + (ulong)(i * LuaUpvalueDescriptionData.StructSize(process)));

                upvalues.Add(upvalue);
            }
        }
    }

    public class LuaFunctionCallInfoData
    {
        public ulong funcAddress; // TValue*
        public ulong stackTopAddress; // TValue*
        public ulong previousAddress; // CallInfo*
        public ulong nextAddress; // CallInfo*

        public ulong stackBaseAddress; // TValue*
        public ulong savedInstructionPointerAddress; // unsigned*

        public int resultCount;
        public int tailCallCount_5_1; // number of tail calls lost under this entry
        public short callStatus;
        public ulong extra;

        public LuaValueDataBase func;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            ulong pointerSize = (ulong)DebugHelpers.GetPointerSize(process);

            if (Schema.LuaFunctionCallInfoData.available)
            {
                funcAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionCallInfoData.funcAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaFunctionCallInfoData.stackBaseAddress_5_123.HasValue)
                {
                    stackBaseAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionCallInfoData.stackBaseAddress_5_123.GetValueOrDefault(0)).GetValueOrDefault(0);
                }
                else
                {
                    stackBaseAddress = funcAddress + LuaHelpers.GetValueSize(process);
                }

                savedInstructionPointerAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionCallInfoData.savedInstructionPointerAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaFunctionCallInfoData.previousAddress_5_23.HasValue)
                    previousAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaFunctionCallInfoData.previousAddress_5_23.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaFunctionCallInfoData.callStatus_5_23.HasValue)
                {
                    if (Schema.LuaFunctionCallInfoData.callStatus_size == 1)
                        callStatus = DebugHelpers.ReadByteVariable(process, address + Schema.LuaFunctionCallInfoData.callStatus_5_23.GetValueOrDefault(0)).GetValueOrDefault(0);
                    else if (Schema.LuaFunctionCallInfoData.callStatus_size == 2)
                        callStatus = DebugHelpers.ReadShortVariable(process, address + Schema.LuaFunctionCallInfoData.callStatus_5_23.GetValueOrDefault(0)).GetValueOrDefault(0);
                }

                if (Schema.LuaFunctionCallInfoData.tailCallCount_5_1.HasValue)
                    tailCallCount_5_1 = DebugHelpers.ReadIntVariable(process, address + Schema.LuaFunctionCallInfoData.tailCallCount_5_1.GetValueOrDefault(0)).GetValueOrDefault(0);
            }
            else if (LuaHelpers.luaVersion == 501)
            {
                stackBaseAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                funcAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                stackTopAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                savedInstructionPointerAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                resultCount = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                tailCallCount_5_1 = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);

                // Not available
                previousAddress = 0;
                nextAddress = 0;
                callStatus = 0;
                extra = 0;
            }
            else if (LuaHelpers.luaVersion == 502)
            {
                funcAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                stackTopAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                previousAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                resultCount = DebugHelpers.ReadStructShort(process, ref address).GetValueOrDefault(0);
                callStatus = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                extra = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                stackBaseAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                savedInstructionPointerAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
            }
            else if (LuaHelpers.luaVersion == 503)
            {
                funcAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                stackTopAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                previousAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                stackBaseAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                savedInstructionPointerAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                DebugHelpers.SkipStructPointer(process, ref address); // ctx of a C function call info

                extra = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                resultCount = DebugHelpers.ReadStructShort(process, ref address).GetValueOrDefault(0);
                callStatus = DebugHelpers.ReadStructShort(process, ref address).GetValueOrDefault(0);
            }
            else
            {
                funcAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                stackTopAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                previousAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                savedInstructionPointerAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                DebugHelpers.SkipStructPointer(process, ref address); // old_errfunc of a C function call info
                DebugHelpers.SkipStructPointer(process, ref address); // ctx of a C function call info

                DebugHelpers.SkipStructInt(process, ref address); // u2

                resultCount = DebugHelpers.ReadStructShort(process, ref address).GetValueOrDefault(0);
                callStatus = DebugHelpers.ReadStructShort(process, ref address).GetValueOrDefault(0);

                stackBaseAddress = funcAddress + LuaHelpers.GetValueSize(process);
            }
        }

        public void ReadFunction(DkmProcess process)
        {
            if (func != null)
                return;

            func = LuaHelpers.ReadValue(process, funcAddress);
        }

        public bool CheckCallStatusLua()
        {
            if (LuaHelpers.luaVersion == 504)
                return (callStatus & (int)CallStatus_5_4.C) == 0;

            if (LuaHelpers.luaVersion == 502)
                return (callStatus & (int)CallStatus_5_2.Lua) != 0;

            return (callStatus & (int)CallStatus_5_3.Lua) != 0;
        }

        public bool CheckCallStatusFinalizer()
        {
            if (LuaHelpers.luaVersion == 504)
                return (callStatus & (int)CallStatus_5_4.Finalizer) != 0;

            if (LuaHelpers.luaVersion == 503)
                return (callStatus & (int)CallStatus_5_3.Finalizer) != 0;

            return false;
        }

        public bool CheckCallStatusTailCall()
        {
            if (LuaHelpers.luaVersion == 504)
                return (callStatus & (int)CallStatus_5_4.TailCall) != 0;

            if (LuaHelpers.luaVersion == 502)
                return (callStatus & (int)CallStatus_5_2.Tail) != 0;

            return (callStatus & (int)CallStatus_5_3.TailCall) != 0;
        }
    }

    public class LuaNodeData
    {
        public ulong valueDataAddress;
        protected LuaValueDataBase value;
        public LuaValueDataBase key;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaNodeData.available)
            {
                valueDataAddress = address + Schema.LuaNodeData.valueDataAddress.GetValueOrDefault(0);

                value = LuaHelpers.ReadValue(process, address + Schema.LuaNodeData.valueDataAddress.GetValueOrDefault(0));

                if (Schema.LuaNodeData.keyDataAddress_5_123.HasValue)
                {
                    key = LuaHelpers.ReadValue(process, address + Schema.LuaNodeData.keyDataAddress_5_123.GetValueOrDefault(0));
                }
                else
                {
                    var typeTag = DebugHelpers.ReadIntVariable(process, address + Schema.LuaNodeData.keyDataTypeAddress_5_4.GetValueOrDefault(0));
                    var valueAddress = address + Schema.LuaNodeData.keyDataValueAddress_5_4.GetValueOrDefault(0);

                    key = LuaHelpers.ReadValueOfType(process, typeTag.GetValueOrDefault(0), valueAddress);
                }
            }
            else if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == 502 || LuaHelpers.luaVersion == 503)
            {
                valueDataAddress = address;

                // Same in Lua 5.1, 5.2 and 5.3
                value = LuaHelpers.ReadValue(process, address);
                key = LuaHelpers.ReadValue(process, address + LuaHelpers.GetValueSize(process));
            }
            else
            {
                valueDataAddress = address;

                value = LuaHelpers.ReadValue(process, address);

                DebugHelpers.SkipStructUlong(process, ref address); // value_
                DebugHelpers.SkipStructByte(process, ref address); // tt_

                var typeTag = DebugHelpers.ReadStructByte(process, ref address);
                DebugHelpers.SkipStructInt(process, ref address);

                key = LuaHelpers.ReadValueOfType(process, typeTag.GetValueOrDefault(0), address);
            }
        }

        public void ReadFromKeyOnly(DkmProcess process, ulong address)
        {
            if (Schema.LuaNodeData.available)
            {
                valueDataAddress = address + Schema.LuaNodeData.valueDataAddress.GetValueOrDefault(0);

                if (Schema.LuaNodeData.keyDataAddress_5_123.HasValue)
                {
                    key = LuaHelpers.ReadValue(process, address + Schema.LuaNodeData.keyDataAddress_5_123.GetValueOrDefault(0));
                }
                else
                {
                    var typeTag = DebugHelpers.ReadIntVariable(process, address + Schema.LuaNodeData.keyDataTypeAddress_5_4.GetValueOrDefault(0));
                    var valueAddress = address + Schema.LuaNodeData.keyDataValueAddress_5_4.GetValueOrDefault(0);

                    key = LuaHelpers.ReadValueOfType(process, typeTag.GetValueOrDefault(0), valueAddress);
                }
            }
            else if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == 502 || LuaHelpers.luaVersion == 503)
            {
                valueDataAddress = address;

                // Same in Lua 5.1, 5.2 and 5.3
                key = LuaHelpers.ReadValue(process, address + LuaHelpers.GetValueSize(process));
            }
            else
            {
                valueDataAddress = address;

                DebugHelpers.SkipStructUlong(process, ref address); // value_
                DebugHelpers.SkipStructByte(process, ref address); // tt_

                var typeTag = DebugHelpers.ReadStructByte(process, ref address);
                DebugHelpers.SkipStructInt(process, ref address);

                key = LuaHelpers.ReadValueOfType(process, typeTag.GetValueOrDefault(0), address);
            }
        }

        public LuaValueDataBase LoadValue(DkmProcess process)
        {
            if (value != null)
                return value;

            if (valueDataAddress == 0)
                return null;

            // Same in Lua 5.1, 5.2, 5.3 and 5.4
            value = LuaHelpers.ReadValue(process, valueDataAddress);

            return value;
        }
    }

    public class LuaTableData
    {
        public byte flags;
        public byte nodeArraySizeLog2;
        public int arraySize;
        public ulong arrayDataAddress; // TValue[]
        public ulong nodeDataAddress; // Node
        public ulong lastFreeNodeDataAddress; // Node
        public ulong metaTableDataAddress; // Table
        public ulong gclistAddress; // GCObject

        public List<LuaValueDataBase> arrayElements;
        public List<LuaNodeData> nodeElements;
        public List<LuaNodeData> nodeKeys;
        public LuaTableData metaTable;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaTableData.available)
            {
                flags = DebugHelpers.ReadByteVariable(process, address + Schema.LuaTableData.flags.GetValueOrDefault(0)).GetValueOrDefault(0);
                nodeArraySizeLog2 = DebugHelpers.ReadByteVariable(process, address + Schema.LuaTableData.nodeArraySizeLog2.GetValueOrDefault(0)).GetValueOrDefault(0);

                metaTableDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaTableData.metaTableDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                arrayDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaTableData.arrayDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                nodeDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaTableData.nodeDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                lastFreeNodeDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaTableData.lastFreeNodeDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                gclistAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaTableData.gclistAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                arraySize = DebugHelpers.ReadIntVariable(process, address + Schema.LuaTableData.arraySize.GetValueOrDefault(0)).GetValueOrDefault(0);
            }
            else if (LuaHelpers.luaVersion == 501)
            {
                // Skip CommonHeader
                DebugHelpers.SkipStructPointer(process, ref address); // next
                DebugHelpers.SkipStructByte(process, ref address); // typeTag
                DebugHelpers.SkipStructByte(process, ref address); // marked

                flags = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                nodeArraySizeLog2 = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);

                metaTableDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                arrayDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                nodeDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                lastFreeNodeDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                gclistAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                arraySize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
            }
            else
            {
                // Same in Lua 5.2, 5.3 and 5.4
                DebugHelpers.SkipStructPointer(process, ref address); // next
                DebugHelpers.SkipStructByte(process, ref address); // typeTag
                DebugHelpers.SkipStructByte(process, ref address); // marked

                flags = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                nodeArraySizeLog2 = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);

                arraySize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);

                arrayDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                nodeDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                lastFreeNodeDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                metaTableDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                gclistAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
            }
        }

        public void LoadValues(DkmProcess process)
        {
            // Check if already loaded
            if (arrayElements != null)
                return;

            // Create even if it's empty
            arrayElements = new List<LuaValueDataBase>();

            if (arrayDataAddress != 0)
            {
                for (int i = 0; i < arraySize; i++)
                {
                    ulong address = arrayDataAddress + (ulong)i * LuaHelpers.GetValueSize(process);

                    arrayElements.Add(LuaHelpers.ReadValue(process, address));
                }
            }

            nodeElements = new List<LuaNodeData>();

            if (nodeDataAddress != 0)
            {
                for (int i = 0; i < (1 << nodeArraySizeLog2); i++)
                {
                    ulong address = nodeDataAddress + (ulong)i * LuaHelpers.GetNodeSize(process);

                    LuaNodeData node = new LuaNodeData();

                    node.ReadFrom(process, address);

                    if (node.key as LuaValueDataNil == null)
                        nodeElements.Add(node);
                }
            }
        }

        public void LoadKeys(DkmProcess process)
        {
            if (nodeKeys != null)
                return;

            // Full data is already loaded, use it
            if (nodeElements != null)
            {
                nodeKeys = nodeElements;
                return;
            }

            nodeKeys = new List<LuaNodeData>();

            if (nodeDataAddress != 0)
            {
                for (int i = 0; i < (1 << nodeArraySizeLog2); i++)
                {
                    ulong address = nodeDataAddress + (ulong)i * LuaHelpers.GetNodeSize(process);

                    LuaNodeData node = new LuaNodeData();

                    node.ReadFromKeyOnly(process, address);

                    if (node.key as LuaValueDataNil == null)
                        nodeKeys.Add(node);
                }
            }
        }

        public void LoadMetaTable(DkmProcess process)
        {
            // Check if already loaded
            if (metaTable != null)
                return;

            if (metaTableDataAddress == 0)
                return;

            metaTable = new LuaTableData();

            metaTable.ReadFrom(process, metaTableDataAddress);
            metaTable.LoadValues(process);
        }

        public LuaValueDataBase FetchMember(DkmProcess process, string name)
        {
            foreach (var element in nodeElements)
            {
                var keyAsString = element.key as LuaValueDataString;

                if (keyAsString == null)
                    continue;

                if (keyAsString.value == name)
                    return element.LoadValue(process);
            }

            return null;
        }
    }

    public class LuaClosureData
    {
        // CommonHeader
        public ulong nextAddress;
        public byte typeTag;
        public byte marked;

        // ClosureHeader
        public byte isC_5_1;
        public byte upvalueSize_opt;
        public ulong gcListAddress;

        // LClosure
        public ulong envTableDataAddress_5_1;
        public ulong functionAddress;

        public ulong firstUpvaluePointerAddress;

        public LuaTableData envTable_5_1;
        public LuaFunctionData function;
        public LuaUpvalueData[] upvalues;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaClosureData.available)
            {
                if (Schema.LuaClosureData.upvalueSize_opt.HasValue)
                    upvalueSize_opt = DebugHelpers.ReadByteVariable(process, address + Schema.LuaClosureData.upvalueSize_opt.GetValueOrDefault(0)).GetValueOrDefault(0);

                functionAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaClosureData.functionAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaClosureData.isC_5_1.HasValue)
                    isC_5_1 = DebugHelpers.ReadByteVariable(process, address + Schema.LuaClosureData.isC_5_1.GetValueOrDefault(0)).GetValueOrDefault(0);

                if (Schema.LuaClosureData.envTableDataAddress_5_1.HasValue)
                    envTableDataAddress_5_1 = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaClosureData.envTableDataAddress_5_1.GetValueOrDefault(0)).GetValueOrDefault(0);

                firstUpvaluePointerAddress = address + Schema.LuaClosureData.firstUpvaluePointerAddress.GetValueOrDefault(0);
            }
            else
            {
                // Same in Lua 5.2, 5.3 and 5.4, additional fields in 5.1
                nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                typeTag = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();
                marked = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 501)
                    isC_5_1 = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

                upvalueSize_opt = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();
                gcListAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 501)
                    envTableDataAddress_5_1 = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();

                functionAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();

                firstUpvaluePointerAddress = address;
            }
        }

        public LuaFunctionData ReadFunction(DkmProcess process)
        {
            if (function != null)
                return function;

            if (functionAddress == 0)
                return null;

            function = new LuaFunctionData();

            function.ReadFrom(process, functionAddress);

            return function;
        }

        public LuaUpvalueData ReadUpvalue(DkmProcess process, int index, int expectedCount)
        {
            int count = upvalueSize_opt;

            if (count == 0)
                count = expectedCount;

            if (upvalues == null)
                upvalues = new LuaUpvalueData[count];

            if (index >= upvalues.Length)
                return null;

            if (upvalues[index] != null)
                return upvalues[index];

            ulong upvalueAddress = DebugHelpers.ReadPointerVariable(process, firstUpvaluePointerAddress + (ulong)(index * DebugHelpers.GetPointerSize(process))).GetValueOrDefault(0);

            if (upvalueAddress == 0)
                return null;

            LuaUpvalueData result = new LuaUpvalueData();

            result.ReadFrom(process, upvalueAddress);

            upvalues[index] = result;

            return result;
        }

        public LuaTableData ReadEnvTable_5_1(DkmProcess process)
        {
            if (envTable_5_1 != null)
                return envTable_5_1;

            if (envTableDataAddress_5_1 == 0)
                return null;

            envTable_5_1 = new LuaTableData();

            envTable_5_1.ReadFrom(process, envTableDataAddress_5_1);

            return envTable_5_1;
        }
    }

    public class LuaExternalClosureData
    {
        // CommonHeader
        public ulong nextAddress;
        public byte typeTag;
        public byte marked;

        // ClosureHeader
        public byte isC_5_1;
        public byte upvalueSize;
        public ulong gcListAddress;

        // CClosure
        public ulong envTableDataAddress_5_1;
        public ulong functionAddress;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaExternalClosureData.available)
            {
                functionAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaExternalClosureData.functionAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
            }
            else
            {
                // Same in Lua 5.2, 5.3 and 5.4, additional fields in 5.1
                nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                typeTag = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();
                marked = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 501)
                    isC_5_1 = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

                upvalueSize = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();
                gcListAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 501)
                    envTableDataAddress_5_1 = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();

                functionAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
            }
        }
    }

    public class LuaUserDataData
    {
        // CommonHeader
        public ulong nextAddress;
        public byte typeTag;
        public byte marked;

        public byte userValueTypeTag_5_3;
        public ulong metaTableDataAddress;

        public LuaTableData metaTable;
        public ulong pointerAtValueStart;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaUserDataData.available)
            {
                metaTableDataAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaUserDataData.metaTableDataAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                pointerAtValueStart = DebugHelpers.ReadPointerVariable(process, address + (ulong)Schema.LuaUserDataData.structSize).GetValueOrDefault();
            }
            else if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == 502 || LuaHelpers.luaVersion == 503)
            {
                nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                typeTag = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();
                marked = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 503)
                    userValueTypeTag_5_3 = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

                metaTableDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 503)
                {
                    DebugHelpers.SkipStructPointer(process, ref address); // len
                    address += 8; // Value user_ (not to be confused with TValue and GetValueSize)

                    pointerAtValueStart = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                }
                else if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == 502)
                {
                    DebugHelpers.SkipStructPointer(process, ref address); // env
                    DebugHelpers.SkipStructPointer(process, ref address); // len

                    address = (address + 7ul) & ~7ul; // Align to 8

                    pointerAtValueStart = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                }
            }
            else
            {
                nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                typeTag = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();
                marked = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

                short userDataValues = DebugHelpers.ReadStructShort(process, ref address).GetValueOrDefault();
                DebugHelpers.SkipStructPointer(process, ref address); // len

                metaTableDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();

                DebugHelpers.SkipStructPointer(process, ref address); // gclist

                address += (ulong)userDataValues * 16u; // Skip user data values

                address = (address + 7ul) & ~7ul; // Align to 8

                // Read pointer from user memory area
                pointerAtValueStart = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
            }
        }

        public LuaTableData LoadMetaTable(DkmProcess process)
        {
            // Check if already loaded
            if (metaTable != null)
                return metaTable;

            if (metaTableDataAddress == 0)
                return null;

            metaTable = new LuaTableData();

            metaTable.ReadFrom(process, metaTableDataAddress);
            metaTable.LoadValues(process);

            return metaTable;
        }

        public string GetNativeType(DkmProcess process)
        {
            LoadMetaTable(process);

            var nativeTypeContext = metaTable.FetchMember(process, "__type");

            if (nativeTypeContext is LuaValueDataTable nativeTypeContextTable)
            {
                nativeTypeContextTable.value.LoadValues(process);

                if (nativeTypeContextTable.value.FetchMember(process, "name") is LuaValueDataString nativeTypeContextName)
                {
                    if (pointerAtValueStart != 0)
                        return nativeTypeContextName.value;
                }
            }

            return null;
        }
    }

    public class LuaDebugData
    {
        public int eventType;

        public ulong nameAddress;
        public ulong nameWhatAddress;
        public ulong whatAddress;
        public ulong sourceAddress;

        public int currentLine;
        public int upvalueSize;
        public int definitionStartLine;
        public int definitionEndLine;

        public string name;
        public string nameWhat;
        public string what;
        public string source;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (Schema.LuaDebugData.available)
            {
                eventType = DebugHelpers.ReadIntVariable(process, address + Schema.LuaDebugData.eventType.GetValueOrDefault(0)).GetValueOrDefault(0);

                nameAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaDebugData.nameAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                nameWhatAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaDebugData.nameWhatAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                whatAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaDebugData.whatAddress.GetValueOrDefault(0)).GetValueOrDefault(0);
                sourceAddress = DebugHelpers.ReadPointerVariable(process, address + Schema.LuaDebugData.sourceAddress.GetValueOrDefault(0)).GetValueOrDefault(0);

                currentLine = DebugHelpers.ReadIntVariable(process, address + Schema.LuaDebugData.currentLine.GetValueOrDefault(0)).GetValueOrDefault(0);
                upvalueSize = DebugHelpers.ReadIntVariable(process, address + Schema.LuaDebugData.upvalueSize.GetValueOrDefault(0)).GetValueOrDefault(0);
                definitionStartLine = DebugHelpers.ReadIntVariable(process, address + Schema.LuaDebugData.definitionStartLine.GetValueOrDefault(0)).GetValueOrDefault(0);
                definitionEndLine = DebugHelpers.ReadIntVariable(process, address + Schema.LuaDebugData.definitionEndLine.GetValueOrDefault(0)).GetValueOrDefault(0);
            }
            else
            {
                eventType = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault();

                nameAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                nameWhatAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                whatAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
                sourceAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 504)
                    DebugHelpers.SkipStructPointer(process, ref address);

                currentLine = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 501)
                    upvalueSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault();

                definitionStartLine = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault();
                definitionEndLine = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault();

                if (LuaHelpers.luaVersion == 502 || LuaHelpers.luaVersion == 503 || LuaHelpers.luaVersion == 504)
                    upvalueSize = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();
            }

            if (nameAddress != 0)
                name = DebugHelpers.ReadStringVariable(process, nameAddress, 1024);
            else
                name = "";

            if (nameWhatAddress != 0)
                nameWhat = DebugHelpers.ReadStringVariable(process, nameWhatAddress, 1024);
            else
                nameWhat = "";

            if (whatAddress != 0)
                what = DebugHelpers.ReadStringVariable(process, whatAddress, 1024);
            else
                what = "";

            if (sourceAddress != 0)
                source = DebugHelpers.ReadStringVariable(process, sourceAddress, 1024);
            else
                source = "";
        }
    }

    public class LuaAddressEntityData
    {
        // Main level - source:line
        public string source;
        public int line;

        // Extended level 'inside' source and line - function and instruction number
        // When this information is missing we can assume that we have the 'first' insrtuciton on source:line
        public ulong functionAddress; // Address of the Proto struct
        public int functionInstructionPointer;

        public ReadOnlyCollection<byte> Encode()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(source);
                    writer.Write(line);

                    writer.Write(functionAddress);
                    writer.Write(functionInstructionPointer);

                    writer.Flush();

                    return new ReadOnlyCollection<byte>(stream.ToArray());
                }
            }
        }

        public void ReadFrom(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(stream))
                {
                    source = reader.ReadString();
                    line = reader.ReadInt32();

                    functionAddress = reader.ReadUInt64();
                    functionInstructionPointer = reader.ReadInt32();
                }
            }
        }
    }

    public class LuaFrameData
    {
        public int marker = 1;

        public ulong state; // Address of the Lua state, called 'L' in Lua library

        public ulong registryAddress; // Address of the Lua global registry, accessible as '&L->l_G->l_registry' in Lua library
        public int version;

        public ulong callInfo; // Address of the CallInfo struct, called 'ci' in Lua library

        public ulong functionAddress; // Address of the Proto struct, accessible as '((LClosure*)ci->func->value_.gc)->p' in Lua library
        public string functionName;

        public int instructionLine;
        public int instructionPointer; // Current instruction within the Lua Closure, evaluated as 'ci->u.l.savedpc - p->code' in Lua library

        public string source;

        public ReadOnlyCollection<byte> Encode()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(marker);

                    writer.Write(state);

                    writer.Write(registryAddress);
                    writer.Write(version);

                    writer.Write(callInfo);

                    writer.Write(functionAddress);
                    writer.Write(functionName);

                    writer.Write(instructionLine);
                    writer.Write(instructionPointer);

                    writer.Write(source);

                    writer.Flush();

                    return new ReadOnlyCollection<byte>(stream.ToArray());
                }
            }
        }

        public bool ReadFrom(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(stream))
                {
                    marker = reader.ReadInt32();

                    if (marker != 1)
                        return false;

                    state = reader.ReadUInt64();

                    registryAddress = reader.ReadUInt64();
                    version = reader.ReadInt32();

                    callInfo = reader.ReadUInt64();

                    functionAddress = reader.ReadUInt64();
                    functionName = reader.ReadString();

                    instructionLine = reader.ReadInt32();
                    instructionPointer = reader.ReadInt32();

                    source = reader.ReadString();
                }
            }

            return true;
        }
    }

    public class LuaBreakpointAdditionalData
    {
        public int marker = 2;

        public string source;
        public int line;

        public ReadOnlyCollection<byte> Encode()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(marker);

                    writer.Write(source);
                    writer.Write(line);

                    writer.Flush();

                    return new ReadOnlyCollection<byte>(stream.ToArray());
                }
            }
        }

        public bool ReadFrom(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(stream))
                {
                    marker = reader.ReadInt32();

                    if (marker != 2)
                        return false;

                    source = reader.ReadString();
                    line = reader.ReadInt32();
                }
            }

            return true;
        }
    }
}
