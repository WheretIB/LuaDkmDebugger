namespace LuaDkmDebuggerComponent
{
    enum CallStatus_5_2
    {
        // call is running a Lua function
        Lua = (1 << 0),

        // call is running a debug hook
        Hooked = (1 << 1),

        // call is running on same invocation of luaV_execute of previous call
        Reentry = (1 << 2),

        // call reentered after suspension
        Yielded = (1 << 3),

        // call is a yieldable protected call
        YieldableProtectedCall = (1 << 4),

        // call has an error status (pcall)
        Stat = (1 << 5),

        // call was tail called
        Tail = (1 << 6),

        // last hook called yielded
        HookYield = (1 << 7),
    }

    enum CallStatus_5_3
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

    enum CallStatus_5_4
    {
        // original value of 'allowhook'
        OriginalAllowHook = (1 << 0),

        // call is running a C function
        C = (1 << 1),

        // call is running a debug hook
        Hooked = (1 << 2),

        // call is a yieldable protected call
        YieldableProtectedCall = (1 << 3),

        // call was tail called
        TailCall = (1 << 4),

        // last hook called yielded
        HookYield = (1 << 5),

        // call is running a finalizer
        Finalizer = (1 << 6),

        // 'ci' has transfer information
        Transfer = (1 << 7),

        // using __lt for __le
        LessEqual = (1 << 8),
    }

    enum InstructionCode
    {
        Call = 36,
        TailCall = 37,
    }
}
