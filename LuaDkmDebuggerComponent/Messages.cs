using System;

namespace LuaDkmDebuggerComponent
{
    public class SupportBreakpointHitMessage
    {
        public Guid breakpointId;
        public Guid threadId;

        public ulong retAddr;
        public ulong frameBase;
        public ulong vframe;
    }
}
