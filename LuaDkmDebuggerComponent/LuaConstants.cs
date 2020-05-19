namespace LuaDkmDebuggerComponent
{
    enum CallStatus
    {
        // original value of 'allowhook'
        OriginalAllowHook = (1 << 0),

        // call is running a Lua function
        Lua = (1 << 1),

        // call is running a debug hook
        Hooked = (1 << 2),

        //call is running on a fresh invocation of luaV_execute
        Fresh = (1 << 3),

        // call is a yieldable protected call
        YieldableProtectedCall = (1 << 4),

        // call was tail called
        TailCall = (1 << 5),

        // last hook called yielded
        HookYield = (1 << 6),

        // using __lt for __le
        LessEqual = (1 << 7),

        // call is running a finalizer
        Finalizer = (1 << 8),
    }

    enum InstructionCode
    {
        Call = 36,
        TailCall = 37,
    }
}
