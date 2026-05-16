using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Task = System.Threading.Tasks.Task;

namespace CmdRun
{
    internal sealed class ExecuteCmdCommand
    {
        public const int CommandId = 0x0100;

        public static readonly Guid CommandSet = new Guid("0619aa44-75cd-4078-95ad-7cbea4aac41b");

        private readonly AsyncPackage package;

        private ExecuteCmdCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            menuItem.BeforeQueryStatus += this.BeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static ExecuteCmdCommand Instance
        {
            get;
            private set;
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new ExecuteCmdCommand(package, commandService);
        }

        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        private void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var command = (OleMenuCommand)sender;
            string file = GetActiveFilePath(ServiceProvider);
            bool isCmdFile = IsCmdFile(file);

            command.Visible = isCmdFile;
            command.Enabled = isCmdFile;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string file = GetActiveFilePath(ServiceProvider);
            if (!IsCmdFile(file))
            {
                return;
            }

            try
            {
                var fileInfo = new FileInfo(file);
                string cmdExe = Environment.GetEnvironmentVariable("COMSPEC");
                if (string.IsNullOrWhiteSpace(cmdExe))
                {
                    cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = cmdExe,
                    Arguments = "/k call \"" + fileInfo.FullName + "\"",
                    WorkingDirectory = fileInfo.DirectoryName,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(
                    this.package,
                    ex.Message,
                    "Execute CMD",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        private static bool IsCmdFile(string file)
        {
            return !string.IsNullOrWhiteSpace(file)
                && File.Exists(file)
                && string.Equals(Path.GetExtension(file), ".cmd", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetActiveFilePath(Microsoft.VisualStudio.Shell.IAsyncServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            EnvDTE80.DTE2 applicationObject = ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                return await serviceProvider.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            });
            if (applicationObject == null || applicationObject.SelectedItems.Count == 0)
            {
                return null;
            }

            foreach (EnvDTE.SelectedItem selectedItem in applicationObject.SelectedItems)
            {
                if (selectedItem.ProjectItem == null)
                {
                    return null;
                }

                return GetProjectItemFileName(selectedItem.ProjectItem);
            }

            return null;
        }

        private static string GetProjectItemFileName(EnvDTE.ProjectItem projectItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                string fileName = projectItem.FileNames[1];
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (COMException)
            {
            }

            try
            {
                EnvDTE.Property fullPathProperty = projectItem.Properties.Item("FullPath");
                return fullPathProperty?.Value?.ToString();
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (COMException)
            {
                return null;
            }
        }
    }
}
