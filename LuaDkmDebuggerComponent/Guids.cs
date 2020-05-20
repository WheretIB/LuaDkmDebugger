using System;

namespace LuaDkmDebuggerComponent
{
    static class Guids
    {
        public static readonly Guid luaCompilerGuid = new Guid("DD79A808-7001-4458-99D9-469BB771B9B4");
        public static readonly Guid luaLanguageGuid = new Guid("C241D4C1-BC0C-45F7-99D3-D5264155BD05");
        public static readonly Guid luaRuntimeGuid = new Guid("A2D176A1-8907-483C-9B36-4544EF424967");
        public static readonly Guid luaSymbolProviderGuid = new Guid("00BB9B25-E5EA-4B0F-AD3D-C017B16F4FA1");
    }

    static class MessageToRemote
    {
        public static readonly Guid guid = new Guid("ED25F587-E107-4F94-9775-885BEC371006");

        public static readonly int createRuntime = 1;
    }
}
