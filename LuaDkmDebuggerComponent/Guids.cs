using System;

namespace LuaDkmDebuggerComponent
{
    static class Guids
    {
        public static readonly Guid luaLocalComponentGuid = new Guid("CF3F5D48-EFBB-49CB-916A-F19812A322ED");
        public static readonly Guid luaRemoteComponentGuid = new Guid("1A5CBF53-315C-4E2C-A790-4042F9435EB7");
        public static readonly Guid luaVsPackageComponentGuid = new Guid("B1C83EED-ADA7-492D-8E41-D47D97315BED");

        public static readonly Guid luaCompilerGuid = new Guid("DD79A808-7001-4458-99D9-469BB771B9B4");
        public static readonly Guid luaLanguageGuid = new Guid("C241D4C1-BC0C-45F7-99D3-D5264155BD05");
        public static readonly Guid luaRuntimeGuid = new Guid("A2D176A1-8907-483C-9B36-4544EF424967");
        public static readonly Guid luaSymbolProviderGuid = new Guid("00BB9B25-E5EA-4B0F-AD3D-C017B16F4FA1");

        public static readonly Guid luaSupportBreakpointGuid = new Guid("F8B5C32C-126E-49EC-979E-3AE10F8321FA");
        public static readonly Guid luaExceptionGuid = new Guid("AD1C7DA0-C25B-491D-8D9C-C86058F77034");
    }

    static class MessageToRemote
    {
        public static readonly Guid guid = new Guid("ED25F587-E107-4F94-9775-885BEC371006");

        public static readonly int createRuntime = 1;
        public static readonly int luaHelperDataLocations = 2;
        public static readonly int pauseBreakpoints = 3;
        public static readonly int resumeBreakpoints = 4;
        public static readonly int luaVersionInfo = 5;
        public static readonly int throwException = 6;
        public static readonly int registerLuaState = 7;
        public static readonly int unregisterLuaState = 8;
    }

    static class MessageToLocal
    {
        public static readonly Guid guid = new Guid("40C433F5-7EB9-400F-8DAC-DC4CAC739BE4");

        public static readonly int luaSupportBreakpointHit = 1;
        public static readonly int luaSymbols = 2;
    }

    static class MessageToLocalWorker
    {
        public static readonly Guid guid = new Guid("CD3A296C-3C54-4B5E-AF46-8B72F528E4B5");

        public static readonly int fetchLuaSymbols = 1;
    }

    static class MessageToVsService
    {
        public static readonly int reloadBreakpoints = 1;
    }
}
