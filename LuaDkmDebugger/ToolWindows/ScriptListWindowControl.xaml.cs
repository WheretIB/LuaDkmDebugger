using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LuaDkmDebugger.ToolWindows
{
    /// <summary>
    /// Interaction logic for ScriptListWindow.xaml
    /// </summary>
    public partial class ScriptListWindowControl : UserControl
    {
        private ScriptListWindowState _state;
        private GridViewColumnHeader _activeHeader;
        private ListSortDirection _activeDirection = ListSortDirection.Ascending;

        public ScriptListWindowControl(ScriptListWindowState state)
        {
            _state = state;

            InitializeComponent();

            ScriptList.ItemsSource = _state.scripts;

            StatusText1.Text = _state.statusText1;
            StatusText2.Text = _state.statusText2;
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

        private void ListViewItem_ColumnClick(object sender, RoutedEventArgs args)
        {
            // WPF doesn't provide auto-sortable lists...
            if (args.OriginalSource is GridViewColumnHeader headerClicked)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    ListSortDirection direction = headerClicked != _activeHeader || _activeDirection == ListSortDirection.Descending ? ListSortDirection.Ascending : ListSortDirection.Descending;

                    var columnBinding = headerClicked.Column.DisplayMemberBinding as Binding;
                    var sortBy = columnBinding?.Path.Path ?? headerClicked.Column.Header as string;

                    ICollectionView dataView = CollectionViewSource.GetDefaultView(ScriptList.ItemsSource);
                    dataView.SortDescriptions.Clear();
                    SortDescription sd = new SortDescription(sortBy, direction);
                    dataView.SortDescriptions.Add(sd);
                    dataView.Refresh();

                    _activeHeader = headerClicked;
                    _activeDirection = direction;
                }
            }
        }

        private void SearchTerm_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(ScriptList.ItemsSource);
            view.Filter = (item) =>
            {
                if (item is ScriptEntry scriptEntry)
                    return scriptEntry.name.Contains(SearchTerm.Text) || scriptEntry.path.Contains(SearchTerm.Text);

                return true;
            };
        }
    }
}
