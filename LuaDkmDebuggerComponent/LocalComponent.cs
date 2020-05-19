using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;

namespace LuaDkmDebuggerComponent
{
    public class LuaStackFilter : IDkmCallStackFilter
    {
        DkmStackWalkFrame[] IDkmCallStackFilter.FilterNextFrame(DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            return new DkmStackWalkFrame[1] { input };
        }
    }
}
