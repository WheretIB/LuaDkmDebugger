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

        public void ReadLocals(DkmProcess process)
        {
            // Check if alraedy loaded
            if (locals != null)
                return;

            locals = new List<LuaLocalVariableData>();

            for (int i = 0; i < localVariableSize; i++)
            {
                LuaLocalVariableData local = new LuaLocalVariableData();

                local.ReadFrom(process, localVariableDataAddress + (ulong)(i * LuaLocalVariableData.StructSize(process)));

                locals.Add(local);
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

    internal class LuaFrameData
    {
        public ulong state; // Address of the Lua state, called 'L' in Lua library

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
