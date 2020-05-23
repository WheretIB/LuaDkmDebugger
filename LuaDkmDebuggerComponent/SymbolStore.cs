using Microsoft.VisualStudio.Debugger;
using System.Collections.Generic;
using System.Diagnostics;

namespace LuaDkmDebuggerComponent
{
    public class SymbolSource
    {
        public string sourceFileName = null;
        public string resolvedFileName = null;

        public Dictionary<ulong, LuaFunctionData> knownFunctions = new Dictionary<ulong, LuaFunctionData>();
    }

    public class SymbolStore
    {
        public Dictionary<string, SymbolSource> knownSources = new Dictionary<string, SymbolSource>();

        public void Add(DkmProcess process, LuaFunctionData function)
        {
            if (function.originalAddress == 0)
            {
                Debug.Assert(false, "Initialize function data before adding to symbol store");
                return;
            }

            function.ReadSource(process);

            if (function.source == null)
                return;

            if (!knownSources.ContainsKey(function.source))
                knownSources.Add(function.source, new SymbolSource() { sourceFileName = function.source });

            SymbolSource source = knownSources[function.source];

            if (source.knownFunctions.ContainsKey(function.originalAddress))
                return;

            source.knownFunctions[function.originalAddress] = function;

            if (function.definitionStartLine == 0)
            {
                function.ReadLocalFunctions(process);
            }
        }
    }
}
