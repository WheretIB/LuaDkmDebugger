using System;
using System.Collections.ObjectModel;
using System.IO;

namespace LuaDkmDebuggerComponent
{
    public class SupportBreakpointHitMessage
    {
        public Guid breakpointId;
        public Guid threadId;

        public ulong retAddr;
        public ulong frameBase;
        public ulong vframe;

        public byte[] Encode()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(breakpointId.ToByteArray());
                    writer.Write(threadId.ToByteArray());

                    writer.Write(retAddr);
                    writer.Write(frameBase);
                    writer.Write(vframe);

                    writer.Flush();

                    return stream.ToArray();
                }
            }
        }

        public bool ReadFrom(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(stream))
                {
                    breakpointId = new Guid(reader.ReadBytes(16));
                    threadId = new Guid(reader.ReadBytes(16));

                    retAddr = reader.ReadUInt64();
                    frameBase = reader.ReadUInt64();
                    vframe = reader.ReadUInt64();
                }
            }

            return true;
        }
    }

    public class HelperLocationsMessage
    {
        public ulong helperBreakCountAddress = 0;
        public ulong helperBreakDataAddress = 0;
        public ulong helperBreakHitIdAddress = 0;
        public ulong helperBreakHitLuaStateAddress = 0;
        public ulong helperBreakSourcesAddress = 0;

        public ulong helperStepOverAddress = 0;
        public ulong helperStepIntoAddress = 0;
        public ulong helperStepOutAddress = 0;
        public ulong helperSkipDepthAddress = 0;

        public Guid breakpointLuaHelperBreakpointHit;
        public Guid breakpointLuaHelperStepComplete;
        public Guid breakpointLuaHelperStepInto;
        public Guid breakpointLuaHelperStepOut;

        public ulong helperStartAddress = 0;
        public ulong helperEndAddress = 0;

        public ulong executionStartAddress = 0;
        public ulong executionEndAddress = 0;

        public byte[] Encode()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(helperBreakCountAddress);
                    writer.Write(helperBreakDataAddress);
                    writer.Write(helperBreakHitIdAddress);
                    writer.Write(helperBreakHitLuaStateAddress);
                    writer.Write(helperBreakSourcesAddress);

                    writer.Write(helperStepOverAddress);
                    writer.Write(helperStepIntoAddress);
                    writer.Write(helperStepOutAddress);
                    writer.Write(helperSkipDepthAddress);

                    writer.Write(breakpointLuaHelperBreakpointHit.ToByteArray());
                    writer.Write(breakpointLuaHelperStepComplete.ToByteArray());
                    writer.Write(breakpointLuaHelperStepInto.ToByteArray());
                    writer.Write(breakpointLuaHelperStepOut.ToByteArray());

                    writer.Write(helperStartAddress);
                    writer.Write(helperEndAddress);

                    writer.Write(executionStartAddress);
                    writer.Write(executionEndAddress);

                    writer.Flush();

                    return stream.ToArray();
                }
            }
        }

        public bool ReadFrom(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(stream))
                {
                    helperBreakCountAddress = reader.ReadUInt64();
                    helperBreakDataAddress = reader.ReadUInt64();
                    helperBreakHitIdAddress = reader.ReadUInt64();
                    helperBreakHitLuaStateAddress = reader.ReadUInt64();
                    helperBreakSourcesAddress = reader.ReadUInt64();

                    helperStepOverAddress = reader.ReadUInt64();
                    helperStepIntoAddress = reader.ReadUInt64();
                    helperStepOutAddress = reader.ReadUInt64();
                    helperSkipDepthAddress = reader.ReadUInt64();

                    breakpointLuaHelperBreakpointHit = new Guid(reader.ReadBytes(16));
                    breakpointLuaHelperStepComplete = new Guid(reader.ReadBytes(16));
                    breakpointLuaHelperStepInto = new Guid(reader.ReadBytes(16));
                    breakpointLuaHelperStepOut = new Guid(reader.ReadBytes(16));

                    helperStartAddress = reader.ReadUInt64();
                    helperEndAddress = reader.ReadUInt64();

                    executionStartAddress = reader.ReadUInt64();
                    executionEndAddress = reader.ReadUInt64();
                }
            }

            return true;
        }
    }
}
