using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Task = System.Threading.Tasks.Task;
using EnvDTE100;
using EnvDTE80;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Operations;

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

        public const int LuaAttachOnLaunchCommandId = 0x0120;
        public const int LuaBreakOnErrorCommandId = 0x0150;
        public const int LoggingCommandId = 0x0130;
        public const int LuaShowHiddenFramesCommandId = 0x0140;

        public static readonly Guid CommandSet = new Guid("6EB675D6-C146-4843-990E-32D43B56706C");

        private IServiceProvider ServiceProvider => this;

        #region Package Members

        public static bool attachOnLaunch = true;
        public static bool breakOnError = true;
        public static bool releaseDebugLogs = false;
        public static bool showHiddenFrames = false;

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
                breakOnError = configurationSettingsStore.GetBoolean("LuaDkmDebugger", "BreakOnError", true);
                releaseDebugLogs = configurationSettingsStore.GetBoolean("LuaDkmDebugger", "ReleaseDebugLogs", false);
                showHiddenFrames = configurationSettingsStore.GetBoolean("LuaDkmDebugger", "ShowHiddenFrames", false);

                LuaDkmDebuggerComponent.LocalComponent.attachOnLaunch = attachOnLaunch;
                LuaDkmDebuggerComponent.LocalComponent.breakOnError = breakOnError;
                LuaDkmDebuggerComponent.LocalComponent.releaseDebugLogs = releaseDebugLogs;
                LuaDkmDebuggerComponent.LocalComponent.showHiddenFrames = showHiddenFrames;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Failed to setup setting with " + e.Message);
            }

            OleMenuCommandService commandService = ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

            if (commandService != null)
            {
                {
                    CommandID menuCommandID = new CommandID(CommandSet, LuaAttachOnLaunchCommandId);

                    OleMenuCommand menuItem = new OleMenuCommand((object sender, EventArgs args) =>
                    {
                        HandleToggleMenuItem(sender, args, "AttachOnLaunch", ref LuaDkmDebuggerComponent.LocalComponent.attachOnLaunch, ref attachOnLaunch);
                    }, menuCommandID);

                    menuItem.BeforeQueryStatus += (object sender, EventArgs args) =>
                    {
                        if (sender is OleMenuCommand command)
                            command.Checked = attachOnLaunch;
                    };

                    menuItem.Enabled = true;
                    menuItem.Checked = attachOnLaunch;

                    commandService.AddCommand(menuItem);
                }
                {
                    CommandID menuCommandID = new CommandID(CommandSet, LuaBreakOnErrorCommandId);

                    OleMenuCommand menuItem = new OleMenuCommand((object sender, EventArgs args) =>
                    {
                        HandleToggleMenuItem(sender, args, "BreakOnError", ref LuaDkmDebuggerComponent.LocalComponent.breakOnError, ref breakOnError);
                    }, menuCommandID);

                    menuItem.BeforeQueryStatus += (object sender, EventArgs args) =>
                    {
                        if (sender is OleMenuCommand command)
                            command.Checked = breakOnError;
                    };

                    menuItem.Enabled = true;
                    menuItem.Checked = breakOnError;

                    commandService.AddCommand(menuItem);
                }

                {
                    CommandID menuCommandID = new CommandID(CommandSet, LoggingCommandId);

                    OleMenuCommand menuItem = new OleMenuCommand((object sender, EventArgs args) =>
                    {
                        HandleToggleMenuItem(sender, args, "ReleaseDebugLogs", ref LuaDkmDebuggerComponent.LocalComponent.releaseDebugLogs, ref releaseDebugLogs);
                    }, menuCommandID);

                    menuItem.BeforeQueryStatus += (object sender, EventArgs args) =>
                    {
                        if (sender is OleMenuCommand command)
                            command.Checked = releaseDebugLogs;
                    };

                    menuItem.Enabled = true;
                    menuItem.Checked = releaseDebugLogs;

                    commandService.AddCommand(menuItem);
                }

                {
                    CommandID menuCommandID = new CommandID(CommandSet, LuaShowHiddenFramesCommandId);

                    OleMenuCommand menuItem = new OleMenuCommand((object sender, EventArgs args) =>
                    {
                        HandleToggleMenuItem(sender, args, "ShowHiddenFrames", ref LuaDkmDebuggerComponent.LocalComponent.showHiddenFrames, ref showHiddenFrames);
                    }, menuCommandID);

                    menuItem.BeforeQueryStatus += (object sender, EventArgs args) =>
                    {
                        if (sender is OleMenuCommand command)
                            command.Checked = showHiddenFrames;
                    };

                    menuItem.Enabled = true;
                    menuItem.Checked = showHiddenFrames;

                    commandService.AddCommand(menuItem);
                }
            }
        }

        private void HandleToggleMenuItem(object sender, EventArgs args, string name, ref bool componentFlag, ref bool packageFlag)
        {
            if (sender is OleMenuCommand command)
            {
                packageFlag = !packageFlag;

                try
                {
                    if (configurationSettingsStore != null)
                        configurationSettingsStore.SetBoolean("LuaDkmDebugger", name, packageFlag);

                    componentFlag = packageFlag;
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to setup '{name}' setting with " + e.Message);
                }

                command.Checked = packageFlag;
            }
        }

        #endregion
    }

    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("Lua Data Tips Provider")]
    [ContentType("code++.Lua")]
    [Order]
    internal sealed class LuaDataTipsProvider : IAsyncQuickInfoSourceProvider
    {
        [Import]
        internal ITextStructureNavigatorSelectorService TextStructureNavigatorSelector { get; set; }

        [Import]
        internal SVsServiceProvider ServiceProvider = null;

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            // This ensures only one instance per textbuffer is created
            return textBuffer.Properties.GetOrCreateSingletonProperty(() =>
            {
                ITextStructureNavigator textStructureNavigator = TextStructureNavigatorSelector.GetTextStructureNavigator(textBuffer);

                DTE2 dte = (DTE2)ServiceProvider.GetService(typeof(SDTE));

                Debugger5 debugger = dte?.Debugger as Debugger5;

                return new LuaDataTipsSourceSource(textBuffer, textStructureNavigator, debugger);
            });
        }
    }

    internal sealed class LuaDataTipsSourceSource : IAsyncQuickInfoSource
    {
        private static readonly ImageId icon = KnownMonikers.AbstractCube.ToImageId();

        private readonly ITextBuffer textBuffer;

        private readonly ITextStructureNavigator textStructureNavigator;

        private readonly Debugger5 debugger;

        public LuaDataTipsSourceSource(ITextBuffer textBuffer, ITextStructureNavigator textStructureNavigator, Debugger5 debugger)
        {
            this.textBuffer = textBuffer;
            this.textStructureNavigator = textStructureNavigator;
            this.debugger = debugger;
        }

        // This is called on a background thread.
        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            var triggerPoint = session.GetTriggerPoint(textBuffer.CurrentSnapshot);

            if (triggerPoint != null)
            {
                TextExtent extent = textStructureNavigator.GetExtentOfWord(triggerPoint.Value);
                SnapshotSpan extentSpan = extent.Span;

                ITextSnapshotLine line = triggerPoint.Value.GetContainingLine();
                ITrackingSpan lineSpan = textBuffer.CurrentSnapshot.CreateTrackingSpan(line.Extent, SpanTrackingMode.EdgeInclusive);

                try
                {
                    if (debugger == null)
                        return null;

                    var stackFrame = debugger.CurrentStackFrame;

                    if (stackFrame == null)
                        return null;

                    // Try to extent the span to the potential chain of member accesses on the left
                    int lineStartPosition = lineSpan.GetStartPoint(textBuffer.CurrentSnapshot).Position;
                    string lineText = lineSpan.GetText(textBuffer.CurrentSnapshot);

                    int localPosition = extentSpan.Start.Position - lineStartPosition;

                    while (localPosition > 1 && (lineText[localPosition - 1] == '.' || lineText[localPosition - 1] == ':'))
                    {
                        TextExtent leftExtent = textStructureNavigator.GetExtentOfWord(new SnapshotPoint(textBuffer.CurrentSnapshot, lineStartPosition + localPosition - 2));
                        SnapshotSpan leftExtentSpan = leftExtent.Span;

                        if (leftExtentSpan.Start.Position >= lineStartPosition)
                        {
                            extentSpan = new SnapshotSpan(leftExtentSpan.Start, extentSpan.End.Position - leftExtentSpan.Start.Position);

                            localPosition = leftExtentSpan.Start.Position - lineStartPosition;
                        }
                    }

                    var expressionText = extentSpan.GetText();

                    // Switch to main thread to access properties
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    var language = stackFrame.Language;

                    if (language != "Lua")
                        return null;

                    var expression = debugger.GetExpression3($"```{expressionText}", stackFrame, false, true, false, 500);

                    if (expression == null)
                        return null;

                    if (!expression.IsValidValue)
                        return null;

                    string value = expression.Value;

                    string type = "";
                    string name = "";

                    if (value.IndexOf("```") >= 0)
                    {
                        type = value.Substring(0, value.IndexOf("```"));
                        value = value.Substring(value.IndexOf("```") + 3);
                    }

                    if (value.IndexOf("```") >= 0)
                    {
                        name = value.Substring(0, value.IndexOf("```"));
                        value = value.Substring(value.IndexOf("```") + 3);
                    }

                    var element = new ContainerElement(ContainerElementStyle.Wrapped, new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Type, $"{type} "), new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, name), new ClassifiedTextRun(PredefinedClassificationTypeNames.String, $" = {value}")));

                    return new QuickInfoItem(lineSpan, element);
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return null;
        }

        public void Dispose()
        {
            // This provider does not perform any cleanup.
        }
    }
}
