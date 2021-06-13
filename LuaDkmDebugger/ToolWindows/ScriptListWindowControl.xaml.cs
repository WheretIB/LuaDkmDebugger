using Microsoft.VisualStudio.Shell;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace LuaDkmDebugger.ToolWindows
{
    /// <summary>
    /// Interaction logic for ScriptListWindow.xaml
    /// </summary>
    public partial class ScriptListWindowControl : UserControl
    {
        private ScriptListWindowState _state;

        public ScriptListWindowControl(ScriptListWindowState state)
        {
            _state = state;

            InitializeComponent();

            ScriptList.ItemsSource = _state.scripts;

            StatusText1.Text = "Lua: ---";
            StatusText2.Text = "Attach: ---";
        }

        private void ListViewItem_DoubleClick(object sender, RoutedEventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is ListViewItem row)
            {
                if (row.Content is ScriptEntry scriptEntry)
                {
                    try
                    {
                        if (scriptEntry.status == "Stored in memory" && scriptEntry.content.Length > 0)
                        {
                            File.WriteAllText(scriptEntry.path, scriptEntry.content);

                            scriptEntry.status = "Loaded from memory";
                        }

                        var cmdobj = _state.dte.Commands.Item("File.OpenFile");

                        string name = scriptEntry.path;
                        object none = null;

                        _state.dte.Commands.Raise(cmdobj.Guid, cmdobj.ID, name, none);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine($"Failed to open file with: {e}");
                    }
                }
            }
        }
    }
}
