using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LuaDkmDebugger.ToolWindows
{
	public class ScriptEntry : INotifyPropertyChanged
	{
		private string _name;
		public string name { get { return _name; } set { _name = value; Changed("name"); } }
		private string _path;
		public string path { get { return _path; } set { _path = value; Changed("path"); } }
		private string _status;
		public string status { get { return _status; } set { _status = value; Changed("status"); } }
		private string _content;
		public string content { get { return _content; } set { _content = value; Changed("content"); } }

		public event PropertyChangedEventHandler PropertyChanged;

		private void Changed(string propertyName)
		{
			if (PropertyChanged != null)
				PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public class ScriptListWindowState
    {
		public EnvDTE80.DTE2 dte;

		public ObservableCollection<ScriptEntry> scripts = new ObservableCollection<ScriptEntry>();
    }
}
