using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;

namespace LuaDkmDebugger.ToolWindows
{
    [Guid(WindowGuidString)]
    public class ScriptListWindow : ToolWindowPane
    {
        public const string WindowGuidString = "E579870C-6E14-4EA5-BD89-86C627D50EAF";
        public const string Title = "Lua Script List";

        public ScriptListWindow(ScriptListWindowState state) : base()
        {
            Caption = Title;
            BitmapImageMoniker = KnownMonikers.ImageIcon;

            Content = new ScriptListWindowControl(state);
        }
    }
}
