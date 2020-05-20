using Microsoft.VisualStudio.Debugger;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace LuaDkmDebuggerComponent
{
    internal enum LuaBaseType
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

    internal enum LuaExtendedType
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
            return 16u;
        }

        internal static ulong GetNodeSize(DkmProcess process)
        {
            return 32u;
        }

        internal static LuaValueDataBase ReadValue(DkmProcess process, ulong address)
        {
            var typeTag = DebugHelpers.ReadIntVariable(process, address + 8);

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
                                originalAddress = address,
                                value = value.Value != 0
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
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
                                originalAddress = address,
                                value = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        originalAddress = address
                    };
                case LuaExtendedType.FloatNumber:
                    {
                        var value = DebugHelpers.ReadDoubleVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataDouble()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                originalAddress = address,
                                value = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        originalAddress = address
                    };
                case LuaExtendedType.IntegerNumber:
                    {
                        var value = DebugHelpers.ReadIntVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataInt()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                originalAddress = address,
                                value = value.Value
                            };
                        }
                    }

                    return null;
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
                                    originalAddress = address,
                                    value = target,
                                    targetAddress = value.Value
                                };
                            }
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
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
                                    originalAddress = address,
                                    value = target,
                                    targetAddress = value.Value
                                };
                            }
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
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
                        originalAddress = address
                    };
                case LuaExtendedType.LuaFunction:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataLuaFunction()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                originalAddress = address,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
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
                                originalAddress = address,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        originalAddress = address
                    };
                case LuaExtendedType.ExternalClosure:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataExternalClosure()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                originalAddress = address,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        originalAddress = address
                    };
                case LuaExtendedType.UserData:
                    {
                        var value = DebugHelpers.ReadPointerVariable(process, address);

                        if (value.HasValue)
                        {
                            return new LuaValueDataUserData()
                            {
                                baseType = GetBaseType(typeTag.Value),
                                extendedType = GetExtendedType(typeTag.Value),
                                originalAddress = address,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
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
                                originalAddress = address,
                                targetAddress = value.Value
                            };
                        }
                    }

                    return new LuaValueDataError()
                    {
                        baseType = GetBaseType(typeTag.Value),
                        extendedType = GetExtendedType(typeTag.Value),
                        originalAddress = address
                    };
            }

            return null;
        }
    }

    internal class LuaLocalVariableData
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

    internal class LuaFunctionData
    {
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

        public void ReadFrom(DkmProcess process, ulong address)
        {
            ulong pointerSize = (ulong)DebugHelpers.GetPointerSize(process);

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
    }

    internal class LuaFunctionCallInfoData
    {
        public ulong funcAddress; // TValue*
        public ulong stackTopAddress; // TValue*
        public ulong previousAddress; // CallInfo*
        public ulong nextAddress; // CallInfo*
        public ulong stackBaseAddress; // TValue*
        public ulong savedInstructionPointerAddress; // unsigned*

        public void ReadFrom(DkmProcess process, ulong address)
        {
            ulong pointerSize = (ulong)DebugHelpers.GetPointerSize(process);

            funcAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            stackTopAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            previousAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            nextAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            stackBaseAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            savedInstructionPointerAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
        }
    }

    internal class LuaValueDataBase
    {
        public LuaBaseType baseType;
        public LuaExtendedType extendedType;
        public ulong originalAddress;
    }

    [DebuggerDisplay("error ({extendedType})")]
    internal class LuaValueDataError : LuaValueDataBase
    {
    }

    [DebuggerDisplay("nil ({extendedType})")]
    internal class LuaValueDataNil : LuaValueDataBase
    {
    }

    [DebuggerDisplay("value = {value} ({extendedType})")]
    internal class LuaValueDataBool : LuaValueDataBase
    {
        public bool value;
    }

    [DebuggerDisplay("value = 0x{value,x} ({extendedType})")]
    internal class LuaValueDataLightUserData : LuaValueDataBase
    {
        public ulong value;
    }

    [DebuggerDisplay("value = {value} ({extendedType})")]
    internal class LuaValueDataInt : LuaValueDataBase
    {
        public int value;
    }

    [DebuggerDisplay("value = {value} ({extendedType})")]
    internal class LuaValueDataDouble : LuaValueDataBase
    {
        public double value;
    }

    [DebuggerDisplay("value = {value} ({extendedType})")]
    internal class LuaValueDataString : LuaValueDataBase
    {
        public string value;
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    internal class LuaValueDataTable : LuaValueDataBase
    {
        public LuaTableData value;
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    internal class LuaValueDataLuaFunction : LuaValueDataBase
    {
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    internal class LuaValueDataExternalFunction : LuaValueDataBase
    {
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    internal class LuaValueDataExternalClosure : LuaValueDataBase
    {
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    internal class LuaValueDataUserData : LuaValueDataBase
    {
        public ulong targetAddress;
    }

    [DebuggerDisplay("({extendedType})")]
    internal class LuaValueDataThread : LuaValueDataBase
    {
        public ulong targetAddress;
    }

    internal class LuaNodeData
    {
        public LuaValueDataBase value;
        public LuaValueDataBase key;

        public void ReadFrom(DkmProcess process, ulong address)
        {
            value = LuaHelpers.ReadValue(process, address);
            key = LuaHelpers.ReadValue(process, address + LuaHelpers.GetValueSize(process));
        }
    }

    internal class LuaTableData
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
            ulong pointerSize = (ulong)DebugHelpers.GetPointerSize(process);

            address += pointerSize; // Skip CommonHeader
            address += 2;

            flags = DebugHelpers.ReadByteVariable(process, address).GetValueOrDefault(0);
            address += sizeof(byte);
            nodeArraySizeLog2 = DebugHelpers.ReadByteVariable(process, address).GetValueOrDefault(0);
            address += sizeof(byte);

            Debug.Assert((address & 0x3) == 0);

            arraySize = DebugHelpers.ReadIntVariable(process, address).GetValueOrDefault(0);
            address += sizeof(int);

            arrayDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            nodeDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            lastFreeNodeDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            metaTableDataAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
            gclistAddress = DebugHelpers.ReadPointerVariable(process, address).GetValueOrDefault(0);
            address += pointerSize;
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

    internal class LuaFrameData
    {
        public ulong state; // Address of the Lua state, called 'L' in Lua library

        public ulong registryAddress; // Address of the Lua global registry, accessible as '&L->l_G->l_registry' in Lua library
        public int version;

        public ulong callInfo; // Address of the CallInfo struct, called 'ci' in Lua library

        public ulong functionAddress; // Address of the Proto struct, accessible as '((LClosure*)ci->func->value_.gc)->p' in Lua library
        public string functionName;

        public int instructionLine;
        public int instructionPointer; // Current instruction within the Lua Closure, evaluated as 'ci->u.l.savedpc - p->code' in Lua library (TODO: do we need to subtract 1?)

        public string source;

        public ReadOnlyCollection<byte> Encode()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
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

        public void ReadFrom(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(stream))
                {
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
        }
    }
}
