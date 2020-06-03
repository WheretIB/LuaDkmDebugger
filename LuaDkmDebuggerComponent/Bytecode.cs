using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Evaluation;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Markup;

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
        LightUserData = LuaBaseType.LightUserData,

        FloatNumber = LuaBaseType.Number,
        IntegerNumber = LuaBaseType.Number + 16,

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
            return (ulong)DebugHelpers.GetPointerSize(process) * 2 + 8;
        }

        internal static ulong GetValueSize(DkmProcess process)
        {
            if (LuaHelpers.luaVersion == 501)
                return 16u;

            if (LuaHelpers.luaVersion == 502)
                return DebugHelpers.Is64Bit(process) ? 16u : 8u;

            return 16u;
        }

        internal static ulong GetNodeSize(DkmProcess process)
        {
            if (LuaHelpers.luaVersion == 501)
                return DebugHelpers.Is64Bit(process) ? 40u : 32u;

            if (LuaHelpers.luaVersion == 502)
                return DebugHelpers.Is64Bit(process) ? 40u : 24u;

            return 32u;
        }

        internal static LuaValueDataBase ReadValue(DkmProcess process, ulong address)
        {
            int? typeTag;

            if (luaVersion == 502 && !DebugHelpers.Is64Bit(process))
            {
                // union { struct { Value v__; int tt__; } i; double d__; } u
                double? value = DebugHelpers.ReadDoubleVariable(process, address);

                if (value == null)
                    return null;

                if (double.IsNaN(value.Value))
                    typeTag = DebugHelpers.ReadIntVariable(process, address + (ulong)DebugHelpers.GetPointerSize(process));
                else
                    typeTag = (int)LuaExtendedType.FloatNumber;
            }
            else
            {
                // Same in Lua 5.1 and 5.3
                // struct { Value value_; int tt_; }
                typeTag = DebugHelpers.ReadIntVariable(process, address + 8);
            }

            if (typeTag == null)
                return null;

            switch (GetExtendedType(typeTag.Value))
            {
                case LuaExtendedType.Nil:
                    {
                        return new LuaValueDataNil()
                        {
                            baseType = GetBaseType(typeTag.Value),
                            extendedType = GetExtendedType(typeTag.Value),
                            evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                            originalAddress = address
                        };
                    }
                case LuaExtendedType.Boolean:
                    {
                        var value = DebugHelpers.ReadIntVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataBool()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = value.Value != 0 ? DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean | DkmEvaluationResultFlags.BooleanTrue : DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.Boolean,
                                originalAddress = address,
                                value = value.Value != 0
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.LightUserData:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataLightUserData()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType,
                                originalAddress = address,
                                value = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.FloatNumber:
                    {
                        var value = DebugHelpers.ReadDoubleVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataNumber()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType,
                                originalAddress = address,
                                value = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.IntegerNumber:
                    {
                        var value = DebugHelpers.ReadIntVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataNumber()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType,
                                originalAddress = address,
                                value = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.ShortString:
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
                                    baseType = GetBaseType(typeTag.Value),
                                    extendedType = GetExtendedType(typeTag.Value),
                                    evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.RawString | DkmEvaluationResultFlags.ReadOnly,
                                    originalAddress = address,
                                    value = target,
                                    targetAddress = value.Value + luaStringOffset
                                };
                            }
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.LongString:
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
                                    baseType = GetBaseType(typeTag.Value),
                                    extendedType = GetExtendedType(typeTag.Value),
                                    evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.RawString | DkmEvaluationResultFlags.ReadOnly,
                                    originalAddress = address,
                                    value = target,
                                    targetAddress = value.Value + luaStringOffset
                                };
                            }
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.Table:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            LuaTableData target = new LuaTableData();

                            target.ReadFrom(process, value.Value);

                            return new LuaValueDataTable()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.ReadOnly | DkmEvaluationResultFlags.Expandable,
                                originalAddress = address,
                                value = target,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.LuaFunction:
                    {
                        // Read pointer to GCObject from address
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            LuaClosureData target = new LuaClosureData();

                            target.ReadFrom(process, value.Value);

                            return new LuaValueDataLuaFunction()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                                originalAddress = address,
                                value = target,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.ExternalFunction:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataExternalFunction()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                                originalAddress = address,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.ExternalClosure:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            LuaExternalClosureData target = new LuaExternalClosureData();

                            target.ReadFrom(process, value.Value);

                            return new LuaValueDataExternalClosure()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                                originalAddress = address,
                                value = target,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.UserData:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            LuaUserDataData target = new LuaUserDataData();

                            target.ReadFrom(process, value.Value);

                            return new LuaValueDataUserData()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.ReadOnly,
                                originalAddress = address,
                                value = target,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        evaluationFlags = DkmEvaluationResultFlags.None,
                        originalAddress = address
                    };
                case LuaExtendedType.Thread:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataThread()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                evaluationFlags = DkmEvaluationResultFlags.IsBuiltInType | DkmEvaluationResultFlags.ReadOnly,
                                originalAddress = address,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
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
            return DebugHelpers.Is64Bit(process) ? 16 : 12;
        }

        public void ReadFrom(DkmProcess process, ulong address)
        {
            // Same in Lua 5.1, 5.2 and 5.3
            nameAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += (ulong)DebugHelpers.GetPointerSize(process);

            if (nameAddress != 0)
            {
                byte[] nameData = process.ReadMemoryString(nameAddress + LuaHelpers.GetStringDataOffset(process), DkmReadMemoryFlags.None, 1, 256);

                if (nameData != null && nameData.Length != 0)
                    name = System.Text.Encoding.UTF8.GetString(nameData, 0, nameData.Length - 1);
                else
                    name = "failed_to_read_name";
            }
            else
            {
                name = "nil";
            }

            lifetimeStartInstruction = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
            address += sizeof(int);
            lifetimeEndInstruction = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
            address += sizeof(int);
        }
    }

    public class LuaUpvalueDescriptionData
    {
        public ulong nameAddress; // TString
        public string name;

        // Not available in Lua 5.1
        public byte isInStack;
        public byte index;

        public static int StructSize(DkmProcess process)
        {
            if (LuaHelpers.luaVersion == 501)
                return DebugHelpers.GetPointerSize(process);

            return DebugHelpers.GetPointerSize(process) * 2;
        }

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (LuaHelpers.luaVersion == 501)
            {
                nameAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
            }
            else
            {
                // Same in Lua 5.2 and 5.3
                nameAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                isInStack = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                index = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
            }

            if (nameAddress != 0)
            {
                byte[] nameData = process.ReadMemoryString(nameAddress + LuaHelpers.GetStringDataOffset(process), DkmReadMemoryFlags.None, 1, 256);

                if (nameData != null && nameData.Length != 0)
                    name = System.Text.Encoding.UTF8.GetString(nameData, 0, nameData.Length - 1);
                else
                    name = "failed_to_read_name";
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
            if (LuaHelpers.luaVersion == 501 || LuaHelpers.luaVersion == 502)
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
        public int localFunctionSize;
        public int localVariableSize;
        public int definitionStartLine;
        public int definitionEndLine;
        public ulong constantDataAddress; // TValue[]
        public ulong codeDataAddress; // Opcode list (unsigned[])
        public ulong localFunctionDataAddress; // (Proto*[])
        public ulong lineInfoDataAddress; // For each opcode (int[])
        public ulong localVariableDataAddress; // LocVar[]
        public ulong upvalueDataAddress; // Upvaldesc[]
        public ulong lastClosureCache; // LClosure
        public ulong sourceAddress; // TString
        public ulong gclistAddress; // GCObject

        public List<LuaLocalVariableData> locals;
        public List<LuaLocalVariableData> activeLocals;

        public List<LuaFunctionData> localFunctions;
        public int[] lineInfo;

        public string source;

        public List<LuaUpvalueDescriptionData> upvalues;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            originalAddress = address;

            ulong pointerSize = (ulong)DebugHelpers.GetPointerSize(process);

            if (LuaHelpers.luaVersion == 501)
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
                definitionStartLine = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionEndLine = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);

                gclistAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                DebugHelpers.SkipStructByte(process, ref address); // nups
                argumentCount = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                isVarargs = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                maxStackSize = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
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
                lastClosureCache = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                sourceAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                upvalueSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                constantSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                codeSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                lineInfoSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                localFunctionSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                localVariableSize = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionStartLine = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);
                definitionEndLine = DebugHelpers.ReadStructInt(process, ref address).GetValueOrDefault(0);

                gclistAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                argumentCount = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                isVarargs = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
                maxStackSize = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault(0);
            }
            else
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
                definitionStartLine = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
                address += sizeof(int);
                definitionEndLine = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
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
                lastClosureCache = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
                sourceAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
                gclistAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
                address += pointerSize;
            }

            // Sanity checks to 'guess' if we have wrong function data
            Debug.Assert(definitionStartLine >= 0 && definitionStartLine < 1000000);
            Debug.Assert(definitionEndLine >= 0 && definitionEndLine < 1000000);
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

        public void ReadLineInfo(DkmProcess process)
        {
            if (lineInfo != null)
                return;

            lineInfo = new int[lineInfoSize];

            for (int i = 0; i < lineInfoSize; i++)
                lineInfo[i] = DebugHelpers.ReadIntVariable(process, lineInfoDataAddress + (ulong)i * 4u).GetValueOrDefault(0);
        }

        public int ReadLineInfoFor(DkmProcess process, int instructionPointer)
        {
            Debug.Assert(instructionPointer < lineInfoSize);

            if (instructionPointer >= lineInfoSize)
                return 0;

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

            if (LuaHelpers.luaVersion == 501)
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
            else
            {
                funcAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                stackTopAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                previousAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);

                ulong unionStartAddress = address;

                stackBaseAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                savedInstructionPointerAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                DebugHelpers.SkipStructPointer(process, ref address); // ctx of a C function call info

                extra = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault(0);
                resultCount = DebugHelpers.ReadStructShort(process, ref address).GetValueOrDefault(0);
                callStatus = DebugHelpers.ReadStructShort(process, ref address).GetValueOrDefault(0);
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
            if (LuaHelpers.luaVersion == 502)
                return (callStatus & (int)CallStatus_5_2.Lua) != 0;

            return (callStatus & (int)CallStatus_5_3.Lua) != 0;
        }

        public bool CheckCallStatusFinalizer()
        {
            if (LuaHelpers.luaVersion == 502)
                return false;

            return (callStatus & (int)CallStatus_5_3.Finalizer) != 0;
        }

        public bool CheckCallStatusTailCall()
        {
            if (LuaHelpers.luaVersion == 502)
                return (callStatus & (int)CallStatus_5_2.Tail) != 0;

            return (callStatus & (int)CallStatus_5_3.TailCall) != 0;
        }
    }

    public class LuaNodeData
    {
        public LuaValueDataBase value;
        public LuaValueDataBase key;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            // Same in Lua 5.1, 5.2 and 5.3
            value = LuaHelpers.ReadValue(process, address);
            key = LuaHelpers.ReadValue(process, address + LuaHelpers.GetValueSize(process));
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
        public LuaTableData metaTable;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            if (LuaHelpers.luaVersion == 501)
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
                // Same in Lua 5.2 and 5.3
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
    }

    public class LuaClosureData
    {
        // CommonHeader
        public ulong nextAddress;
        public byte typeTag;
        public byte marked;

        // ClosureHeader
        public byte isC_5_1;
        public byte upvalueSize;
        public ulong gcListAddress;

        // LClosure
        public ulong envTableDataAddress_5_1;
        public ulong functionAddress;

        public ulong firstUpvaluePointerAddress;

        public LuaTableData envTable_5_1;
        public LuaFunctionData function;

        public void ReadFrom(DkmProcess process, ulong address)
        {
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

            firstUpvaluePointerAddress = address;
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

        public LuaUpvalueData ReadUpvalue(DkmProcess process, int index)
        {
            Debug.Assert(index < upvalueSize);

            ulong upvalueAddress = DebugHelpers.ReadPointerVariable(process, firstUpvaluePointerAddress + (ulong)(index * DebugHelpers.GetPointerSize(process))).GetValueOrDefault(0);

            if (upvalueAddress == 0)
                return null;

            LuaUpvalueData result = new LuaUpvalueData();

            result.ReadFrom(process, upvalueAddress);

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

    public class LuaUserDataData
    {
        // CommonHeader
        public ulong nextAddress;
        public byte typeTag;
        public byte marked;

        public byte userValueTypeTag_5_3;
        public ulong metaTableDataAddress;

        public LuaTableData metaTable;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            nextAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
            typeTag = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();
            marked = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

            if (LuaHelpers.luaVersion == 503)
                userValueTypeTag_5_3 = DebugHelpers.ReadStructByte(process, ref address).GetValueOrDefault();

            metaTableDataAddress = DebugHelpers.ReadStructPointer(process, ref address).GetValueOrDefault();
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
