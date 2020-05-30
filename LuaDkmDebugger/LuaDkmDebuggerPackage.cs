using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Task = System.Threading.Tasks.Task;

namespace LuaDkmDebugger
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(LuaDkmDebuggerPackage.PackageGuidString)]
    [ProvideMenuResource("LuaDkmDebuggerMenus.ctmenu", 1)]
    public sealed class LuaDkmDebuggerPackage : AsyncPackage
    {
        /// <summary>
        /// LuaDkmDebuggerPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "99662a30-53ec-42a6-be5d-80aed0e1e2ea";

        public const int CommandId = 0x0120;
        public static readonly Guid CommandSet = new Guid("6EB675D6-C146-4843-990E-32D43B56706C");

        private IServiceProvider ServiceProvider => this;

        #region Package Members

        public static bool attachOnLaunch = true;
        private WritableSettingsStore configurationSettingsStore = null;

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                SettingsManager settingsManager = new ShellSettingsManager(ServiceProvider);

                configurationSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                configurationSettingsStore.CreateCollection("LuaDkmDebugger");

                attachOnLaunch = configurationSettingsStore.GetBoolean("LuaDkmDebugger", "AttachOnLaunch", true);

                LuaDkmDebuggerComponent.LocalComponent.attachOnLaunch = attachOnLaunch;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Failed to setup setting with " + e.Message);
            }

            OleMenuCommandService commandService = ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

            if (commandService != null)
            {
                CommandID menuCommandID = new CommandID(CommandSet, CommandId);

                OleMenuCommand menuItem = new OleMenuCommand(MenuItemCallback, menuCommandID);

                menuItem.BeforeQueryStatus += OnBeforeQueryStatus;

                menuItem.Checked = attachOnLaunch;

                commandService.AddCommand(menuItem);
            }
        }

        private void MenuItemCallback(object sender, EventArgs args)
        {
            if (sender is OleMenuCommand command)
            {
                attachOnLaunch = !attachOnLaunch;

                try
                {
                    if (configurationSettingsStore != null)
                        configurationSettingsStore.SetBoolean("LuaDkmDebugger", "AttachOnLaunch", attachOnLaunch);

                    LuaDkmDebuggerComponent.LocalComponent.attachOnLaunch = attachOnLaunch;
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to setup setting with " + e.Message);
                }

                command.Checked = attachOnLaunch;
            }
        }

        private void OnBeforeQueryStatus(object sender, EventArgs args)
        {
            if (sender is OleMenuCommand command)
            {
                command.Checked = attachOnLaunch;
            }
        }

        #endregion
    }
}
