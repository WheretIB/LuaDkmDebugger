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

    public class HelperLocationsMessage
    {
        public ulong helperBreakLineAddress = 0;
        public ulong helperStepOverAddress = 0;
        public ulong helperStepIntoAddress = 0;
        public ulong helperStepOutAddress = 0;

        public Guid breakpointLuaHelperBreakpointHit;
        public Guid breakpointLuaHelperStepComplete;
        public Guid breakpointLuaHelperStepInto;
        public Guid breakpointLuaHelperStepOut;
    }
}
