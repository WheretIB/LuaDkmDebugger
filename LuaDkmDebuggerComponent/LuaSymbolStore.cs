using Microsoft.VisualStudio.Debugger;
using System.Collections.Generic;
using System.Diagnostics;

namespace LuaDkmDebuggerComponent
{
    public class LuaScriptSymbols
    {
        public string sourceFileName = null;
        public string scriptContent = null;
        public byte[] sha1Hash = null;

        public string resolvedFileName = null;
    }

    public class LuaSourceSymbols
    {
        public string sourceFileName = null;
        public ulong address = 0;
        public string resolvedFileName = null;

        public Dictionary<ulong, LuaFunctionData> knownFunctions = new Dictionary<ulong, LuaFunctionData>();
    }

    public class LuaStateSymbols
    {
        public Dictionary<string, LuaSourceSymbols> knownSources = new Dictionary<string, LuaSourceSymbols>();
        public Dictionary<ulong, string> functionNames = new Dictionary<ulong, string>();
        public Dictionary<string, LuaScriptSymbols> knownScripts = new Dictionary<string, LuaScriptSymbols>();
        public Dictionary<string, string> unnamedScriptMapping = new Dictionary<string, string>();

        public void AddSourceFromFunction(DkmProcess process, LuaFunctionData function)
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
                knownSources.Add(function.source, new LuaSourceSymbols() { sourceFileName = function.source, address = function.sourceAddress });

            LuaSourceSymbols source = knownSources[function.source];

            if (source.knownFunctions.ContainsKey(function.originalAddress))
                return;

            source.knownFunctions[function.originalAddress] = function;

            if (function.hasDefinitionLineInfo && function.definitionStartLine_opt == 0)
            {
                function.ReadLocalFunctions(process);
            }
        }

        public LuaSourceSymbols FetchSourceSymbols(string sourceFileName)
        {
            if (knownSources.ContainsKey(sourceFileName))
                return knownSources[sourceFileName];

            return null;
        }

        public void AddFunctionName(ulong address, string name)
        {
            if (!functionNames.ContainsKey(address))
                functionNames.Add(address, name);
            else
                functionNames[address] = name;
        }

        public string FetchFunctionName(ulong address)
        {
            if (functionNames.ContainsKey(address))
                return functionNames[address];

            return null;
        }

        public void AddScriptSource(string scriptName, string scriptContent, byte[] sha1Hash)
        {
            if (!knownScripts.ContainsKey(scriptName))
                knownScripts.Add(scriptName, new LuaScriptSymbols { sourceFileName = scriptName, scriptContent = scriptContent, sha1Hash = sha1Hash });
            else
                knownScripts[scriptName] = new LuaScriptSymbols { sourceFileName = scriptName, scriptContent = scriptContent, sha1Hash = sha1Hash };
        }

        public LuaScriptSymbols FetchScriptSource(string sourceFileName)
        {
            if (knownScripts.ContainsKey(sourceFileName))
                return knownScripts[sourceFileName];

            return null;
        }
    }

    public class LuaSymbolStore
    {
        public Dictionary<ulong, LuaStateSymbols> knownStates = new Dictionary<ulong, LuaStateSymbols>();

        public LuaStateSymbols FetchOrCreate(ulong stateAddress)
        {
            if (!knownStates.ContainsKey(stateAddress))
                knownStates.Add(stateAddress, new LuaStateSymbols());

            return knownStates[stateAddress];
        }

        public void Remove(ulong stateAddress)
        {
            knownStates.Remove(stateAddress);
        }

        public LuaSourceSymbols FetchSourceSymbols(string sourceFileName)
        {
            foreach (var state in knownStates)
            {
                var sourceSymbols = state.Value.FetchSourceSymbols(sourceFileName);

                if (sourceSymbols != null)
                    return sourceSymbols;
            }

            return null;
        }

        public LuaScriptSymbols FetchScriptSource(string sourceFileName)
        {
            foreach (var state in knownStates)
            {
                var scriptSource = state.Value.FetchScriptSource(sourceFileName);

                if (scriptSource != null)
                    return scriptSource;
            }

            return null;
        }
    }
}
