﻿/*
 * Original author: Brendan MacLean <brendanx .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2009 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using pwiz.Common.Controls;
using pwiz.Common.SystemUtil;
using pwiz.Skyline.Alerts;
using pwiz.Skyline.Controls;
using pwiz.Skyline.Controls.Startup;
using pwiz.Skyline.Model;
using pwiz.Skyline.Model.Tools;
using pwiz.Skyline.Properties;
using pwiz.Skyline.Util;
using pwiz.Skyline.Util.Extensions;

// Once-per-assembly initialization to perform logging with log4net.
[assembly: log4net.Config.XmlConfigurator(ConfigFile = "SkylineLog4Net.config", Watch = true)]

namespace pwiz.Skyline
{
    /// <summary>
    /// Anything in this class is really application global, and is better stored
    /// on some more local object like the document settings, or the
    /// <see cref="Settings"/> class.  Even the only existing <see cref="Name"/>
    /// property is just a shortcut to the <see cref="Settings"/> application
    /// scope property for easier use in <see cref="MessageBox"/>.
    /// 
    /// Should anything else need to be added, it should be clearly described
    /// why it is necessary.
    /// </summary>
    public static class Program
    {
        public const int LICENSE_VERSION_CURRENT = 5;   // Added Shimadzu license

        public static string MainToolServiceName { get; private set; }
        
        // Parameters for testing.
        public static bool StressTest { get; set; }                 // Set true when doing stress testing.
        public static bool FunctionalTest { get; set; }             // Set to true by AbstractFunctionalTest
        public static bool SkylineOffscreen { get; set; }           // Set true to move Skyline windows offscreen.
        public static bool DemoMode { get; set; }
        public static bool NoVendorReaders { get; set; }            // Set true to avoid calling vendor readers.
        public static bool NoSaveSettings { get; set; }             // Set true to use separate settings file.
        public static bool ShowFormNames { get; set; }              // Set true to show each Form name in title.
        public static bool ShowMatchingPages { get; set; }          // Set true to show tutorial pages automatically when pausing for moust click
        public static int UnitTestTimeoutMultiplier { get; set; }   // Set to positive multiplier for multi-process stress runs.
        public static int PauseSeconds { get; set; }                // Positive to pause when displaying dialogs for unit test, <0 to pause for mouse click
        public static IList<string> PauseForms { get; set; }        // List of forms to pause after displaying.
        public static string ExtraRawFileSearchFolder { get; set; }
        public static List<Exception> TestExceptions { get; set; }
        public static bool DisableJoining { get; set; }
        public static bool NoAllChromatogramsGraph { get; set; }
 
        private static bool _initialized;                           // Flag to do some initialization just once per process.
        private static string _name;                                // Program name.

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static void Main(string[] args = null)
        {
            if (String.IsNullOrEmpty(Settings.Default.InstallationId)) // Each instance to have GUID
                Settings.Default.InstallationId = Guid.NewGuid().ToString();

            // don't allow 64-bit Skyline to run in a 32-bit process
            if (Install.Is64Bit && !Environment.Is64BitProcess)
            {
                string installUrl = Install.Url;
                string installLabel = (installUrl == string.Empty) ? string.Empty : string.Format(Resources.Program_Main_Install_32_bit__0__, Name);
                AlertLinkDlg.Show(null,
                    string.Format(Resources.Program_Main_You_are_attempting_to_run_a_64_bit_version_of__0__on_a_32_bit_OS_Please_install_the_32_bit_version, Name),
                    installLabel,
                    installUrl);
                return;
            }

            CommonFormEx.TestMode = FunctionalTest;
            CommonFormEx.Offscreen = SkylineOffscreen;
            CommonFormEx.ShowFormNames = FormEx.ShowFormNames = ShowFormNames;

            // For testing and debugging Skyline command-line interface
            if (args != null && args.Length > 0) 
            {
                if (!CommandLineRunner.HasCommandPrefix(args[0]))
                {
                    var writer = new CommandStatusWriter(new DebugWriter());
                    if (args[0].Equals("--ui", StringComparison.InvariantCultureIgnoreCase)) // Not L10N
                    {
                        // ReSharper disable once ObjectCreationAsStatement
                        new CommandLineUI(args, writer);
                    }
                    else
                    {
                        CommandLineRunner.RunCommand(args, writer);
                    }
                }
                else
                {
                    // For testing SkylineRunner without installation
                    CommandLineRunner clr = new CommandLineRunner();
                    clr.Start(args[0]);
                }

                return;
            }
            // The way Skyline command-line interface is run for an installation
            else if (AppDomain.CurrentDomain.SetupInformation.ActivationArguments != null &&
                AppDomain.CurrentDomain.SetupInformation.ActivationArguments.ActivationData != null &&
                AppDomain.CurrentDomain.SetupInformation.ActivationArguments.ActivationData.Length > 0 &&
                CommandLineRunner.HasCommandPrefix(AppDomain.CurrentDomain.SetupInformation.ActivationArguments.ActivationData[0])) // Not L10N
            {
                CommandLineRunner clr = new CommandLineRunner();
                clr.Start(AppDomain.CurrentDomain.SetupInformation.ActivationArguments.ActivationData[0]);

                // HACK: until the "invalid string binding" error is resolved, this will prevent an error dialog at exit
                Process.GetCurrentProcess().Kill();
                return;
            }

            try
            {
                Init();
                if (!string.IsNullOrEmpty(Settings.Default.DisplayLanguage))
                {
                    try
                    {
                        LocalizationHelper.CurrentUICulture =
                            CultureInfo.GetCultureInfo(Settings.Default.DisplayLanguage);
                    }
                    catch (CultureNotFoundException)
                    {
                    }
                }
                LocalizationHelper.InitThread(Thread.CurrentThread);

                // Make sure the user has agreed to the current license version
                // or one more recent.
                int licenseVersion = Settings.Default.LicenseVersionAccepted;
                if (licenseVersion < LICENSE_VERSION_CURRENT && !NoSaveSettings)
                {
                    // If the user has never used the application before, then
                    // they must have agreed to the current license agreement during
                    // installation.  Otherwise, make sure they agree to the new
                    // license agreement.
                    if (Install.Type == Install.InstallType.release &&
                            (licenseVersion != 0 || !Settings.Default.MainWindowSize.IsEmpty))
                    {
                        using (var dlg = new UpgradeDlg(licenseVersion))
                        {
                            if (dlg.ShowDialog() == DialogResult.Cancel)
                                return;
                        }
                    }

                    try
                    {
                        // Make sure the user never sees this again for this license version
                        Settings.Default.LicenseVersionAccepted = LICENSE_VERSION_CURRENT;
                        Settings.Default.Save();
                    }
// ReSharper disable EmptyGeneralCatchClause
                    catch (Exception)
// ReSharper restore EmptyGeneralCatchClause
                    {
                        // Just try to update the license version next time.
                    }
                }

                try
                {
                    // If this is a new installation copy over installed external tools from previous installation location.
                    var toolsDirectory = ToolDescriptionHelpers.GetToolsDirectory();
                    if (!Directory.Exists(toolsDirectory))
                    {
                        using (var longWaitDlg = new LongWaitDlg
                        {
                            Text = Name,
                            Message = Resources.Program_Main_Copying_external_tools_from_a_previous_installation,
                            ProgressValue = 0
                        })
                        {
                            longWaitDlg.PerformWork(null, 1000*3, broker => CopyOldTools(toolsDirectory, broker));
                        }
                    }
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch
                {
                    
                }

                // Force live reports (though tests may reset this)
                //Settings.Default.EnableLiveReports = true;

                if (ReportShutdownDlg.HadUnexpectedShutdown())
                {
                    using (var reportShutdownDlg = new ReportShutdownDlg())
                    {
                        reportShutdownDlg.ShowDialog();
                    }
                }
                SystemEvents.DisplaySettingsChanged += SystemEventsOnDisplaySettingsChanged;
                // Careful, a throw out of the SkylineWindow constructor without this
                // catch causes Skyline just to appear to silently never start.  Makes for
                // some difficult debugging.
                try
                {
                    var activationArgs = AppDomain.CurrentDomain.SetupInformation.ActivationArguments;
                    if ((activationArgs != null &&
                        activationArgs.ActivationData != null &&
                        activationArgs.ActivationData.Length != 0) ||
                        !Settings.Default.ShowStartupForm)
                    {
                        MainWindow = new SkylineWindow(args);
                    }
                    else
                    {
                        StartWindow = new StartPage();
                        try
                        {
                            if (StartWindow.ShowDialog() != DialogResult.OK)
                            {
                                Application.Exit();
                                return;
                            }

                            MainWindow = StartWindow.MainWindow;
                        }
                        finally
                        {
                            StartWindow.Dispose();
                            StartWindow = null;
                        }
                    }
                }
                catch (Exception x)
                {
                    ReportExceptionUI(x, new StackTrace(1, true));
                }

                ConcurrencyVisualizer.StartEvents(MainWindow);

                // Position window offscreen for stress testing.
                if (SkylineOffscreen)
                    FormEx.SetOffscreen(MainWindow);

                ActionUtil.RunAsync(() =>
                {
                    try {
                        SendAnalyticsHit(); 
                    } catch (Exception ex) {
                        Trace.TraceWarning("Exception sending analytics hit {0}", ex);  // Not L10N
                    }
                });
                MainToolServiceName = Guid.NewGuid().ToString();
                Application.Run(MainWindow);
                StopToolService();
            }
            catch (Exception x)
            {
                // Send unhandled exceptions to the console.
                Console.WriteLine(x.Message);
                Console.Write(x.StackTrace);
            }

            MainWindow = null;
        }

        private static void SystemEventsOnDisplaySettingsChanged(object sender, EventArgs eventArgs)
        {
            foreach (Form form in Application.OpenForms)
            {
                Rectangle rcForm = form.Bounds;
                var screen = Screen.FromControl(form);
                if (!rcForm.IntersectsWith(screen.WorkingArea))
                {
                    FormEx.ForceOnScreen(form);
                }
            }
        }

        private static void SendAnalyticsHit()
        {
            if (!Install.Version.Equals(String.Empty) && 
                Install.Type != Install.InstallType.developer) {
                // ReSharper disable NonLocalizedString
                var postData = "v=1"; // Version 
                postData += "&t=event"; // Event hit type
                postData += "&tid=UA-9194399-1"; // Tracking Id 
                postData += "&cid=" + Settings.Default.InstallationId; // Anonymous Client Id
                postData += "&ec=Instance"; // Event Category
                postData += "&ea="+ Uri.EscapeDataString(Install.Version + "-" + (Install.Is64Bit?"64bit":"32bit")); // Event Action
                postData += "&el=" + Install.Type; // Event Label
                postData += "&p=" + "Instance"; // Page
               
                var data = Encoding.UTF8.GetBytes(postData);
                var request = (HttpWebRequest)WebRequest.Create("http://www.google-analytics.com/collect");
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = data.Length;
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }
                var response = (HttpWebResponse)request.GetResponse();
                var responseStream = response.GetResponseStream();
                if (null != responseStream)
                {
                    new StreamReader(responseStream).ReadToEnd();
                }
            }
            // ReSharper restore NonLocalizedString
        }

        public static void StartToolService()
        {
            if (MainToolService == null)
            {
                MainToolService = new ToolService(MainToolServiceName, MainWindow);
                MainWindow.DocumentChangedEvent += DocumentChangedEventHandler;
                MainToolService.RunAsync();
            }
        }

        public static void StopToolService()
        {
            if (MainToolService != null)
            {
                MainWindow.DocumentChangedEvent -= DocumentChangedEventHandler;
                MainToolService.Stop();
                MainToolService = null;
            }
        }

        private static void DocumentChangedEventHandler(object sender, DocumentChangedEventArgs args)
        {
            MainToolService.SendDocumentChange();
        }

        private static void CopyOldTools(string outerToolsFolderPath, ILongWaitBroker broker)
        {
            //Copy tools to a different folder then Directory.Move if successful.
            string tempOuterToolsFolderPath = string.Concat(outerToolsFolderPath, "_installing"); // Not L10N
            if (Directory.Exists(tempOuterToolsFolderPath))
            {
                DirectoryEx.SafeDelete(tempOuterToolsFolderPath);
                // Not sure this is necessay, but just to be safe
                if (Directory.Exists(tempOuterToolsFolderPath))
                    throw new Exception(Resources.Program_CopyOldTools_Error_copying_external_tools_from_previous_installation);
            }

            // Must create the tools directory to avoid ending up here again next time
            Directory.CreateDirectory(tempOuterToolsFolderPath);

            ToolList toolList = Settings.Default.ToolList;
            int numTools = toolList.Count;
            const int endValue = 100;
            int progressValue = 0;
            int increment = (endValue - progressValue)/(numTools +1);

            foreach (var tool in toolList)
            {
                string toolDirPath = tool.ToolDirPath;
                if (!string.IsNullOrEmpty(toolDirPath) && Directory.Exists(toolDirPath))
                {
                    string foldername = Path.GetFileName(toolDirPath);
                    string newDir = Path.Combine(outerToolsFolderPath, foldername);
                    string tempNewDir = Path.Combine(tempOuterToolsFolderPath, foldername);
                    if (!Directory.Exists(tempNewDir))
                        DirectoryEx.DirectoryCopy(toolDirPath, tempNewDir, true);
                    tool.ToolDirPath = newDir; // Update the tool to point to its new directory.
                    tool.ArgsCollectorDllPath = tool.ArgsCollectorDllPath.Replace(toolDirPath, newDir);
                }
                if (broker.IsCanceled)
                {
                    // Don't leave around a corrupted directory
                    DirectoryEx.SafeDelete(tempOuterToolsFolderPath);
                    return;
                }

                progressValue += increment;
                broker.ProgressValue = progressValue;                
            }
            Directory.Move(tempOuterToolsFolderPath, outerToolsFolderPath);
            Settings.Default.ToolList = ToolList.CopyTools(toolList);
        }

        public static void Init()
        {
            if (!_initialized)
            {
                _initialized = true;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += ThreadExceptionEventHandler;

                // Add handler for non-UI thread exceptions. 
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            }
        }

        private static readonly object _unhandledExceptionLock = new object();
        public static ToolService MainToolService;

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Only the first unhandled exception is reported.
            lock (_unhandledExceptionLock)
            {
                try
                {
                    ReportShutdownDlg.SaveExceptionFile((Exception)e.ExceptionObject);
                }
// ReSharper disable once EmptyGeneralCatchClause
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                }

                // Other threads may continue to execute and cause unhandled exceptions.  Kill the process as soon as possible.
                Process.GetCurrentProcess().Kill();
            }
        }

        public static void CloseSkyline()
        {
            if (MainWindow != null && !MainWindow.IsDisposed)
            {
                MainWindow.Invoke(new Action(MainWindow.Close));
            }
        }

        /// <summary>
        /// Asynchronously brings up the <see cref="ReportErrorDlg"/> dialog.
        /// Unhandled exceptions on the UI thread are handled by the
        /// <see cref="Application.ThreadException"/> event, but code on
        /// other threads should catch and report exceptions here.
        /// </summary>
        public static void ReportException(Exception exception)
        {
            if (TestExceptions != null)
            {
                AddTestException(exception);
                return;
            }

            Trace.TraceError("Unhandled exception: {0}", exception); // Not L10N
            var stackTrace = new StackTrace(1, true);
            var mainWindow = MainWindow;
            try
            {
                if (mainWindow != null && !mainWindow.IsDisposed)
                {
                    mainWindow.BeginInvoke(new Action<Exception, StackTrace>(ReportExceptionUI), exception, stackTrace);
                }
            }
            catch (Exception exception2)
            {
                Trace.TraceError("Exception in ReportException: {0}", exception2); // Not L10N
            }
        }

        private static void ThreadExceptionEventHandler(Object sender, ThreadExceptionEventArgs e)
        {
            if (TestExceptions != null)
            {
                AddTestException(e.Exception);
                return;
            }

            Trace.TraceError("Unhandled exception on UI thread: {0}", e.Exception); // Not L10N
            var stackTrace = new StackTrace(1, true);
            ReportExceptionUI(e.Exception, stackTrace);
        }

        private static void ReportExceptionUI(Exception exception, StackTrace stackTrace)
        {
            using (var reportForm = new ReportErrorDlg(exception, stackTrace))
            {
                reportForm.ShowDialog(MainWindow);
            }         
        }

        public static void AddTestException(Exception exception)
        {
            lock (TestExceptions)
            {
                TestExceptions.Add(exception);
            }
        }

        public static SkylineWindow MainWindow { get; private set; }
        public static StartPage StartWindow { get; private set; }
        public static SrmDocument ActiveDocument { get { return MainWindow != null ? MainWindow.Document : null; } }
        public static SrmDocument ActiveDocumentUI { get { return MainWindow != null ? MainWindow.DocumentUI : null; } }

        /// <summary>
        /// Shortcut to the application name stored in <see cref="Settings"/>
        /// </summary>
        public static string Name
        {
            get
            {
                return _name ??
                       (_name =
                        Settings.Default.ProgramName + (Install.Type == Install.InstallType.daily ? "-daily" : string.Empty)); // Not L10N
            }
        }

        /// <summary>
        /// A text writer that writes to the debug console
        /// </summary>
        private class DebugWriter : TextWriter
        {
            public override Encoding Encoding
            {
                get { return Console.Out.Encoding; }
            }

            public override void Write(char value)
            {
                Trace.Write(value);
            }

            public override void WriteLine()
            {
                Trace.WriteLine(string.Empty);
            }

            public override void WriteLine(string value)
            {
                Trace.WriteLine(value);
            }
        }
    }

    public class CommandLineRunner
    {
        private const string COMMAND_PREFIX = "CMD"; // Not L10N

        public static bool HasCommandPrefix(string arg)
        {
            return arg.StartsWith(COMMAND_PREFIX);
        }

        private static string RemoveCommandPrefix(string arg)
        {
            // Remove prefix and potential trailing dash
            return arg.Length > COMMAND_PREFIX.Length ? arg.Substring(COMMAND_PREFIX.Length) : string.Empty;
        }

        public static void RunCommand(string[] inputArgs, CommandStatusWriter consoleOut)
        {
            using (CommandLine cmd = new CommandLine(consoleOut))
            {
                cmd.Run(inputArgs);
            }
        }


        /// <summary>
        /// This function will try for 5 seconds to open a named pipe ("SkylineInputPipe").
        /// If this operation is not successful, the function will exit. Otherwise,
        /// the function will print each line received from the pipe
        /// out to the console and then wait for a newline from the user.
        /// </summary>
        public void Start(string arg0)
        {
            string guidSuffix = RemoveCommandPrefix(arg0);

            List<string> args = new List<string>();
            using (NamedPipeClientStream pipeStream = new NamedPipeClientStream("SkylineInputPipe" + guidSuffix)) // Not L10N
            {
                // The connect function will wait 5s for the pipe to become available
                try
                {
                    pipeStream.Connect(5 * 1000);
                }
                catch (Exception)
                {
                    // Nothing to output, because no connection to command-line process.
                    return;
                }

                using (StreamReader sr = new StreamReader(pipeStream))
                {
                    string line;
                    //While (!done reading)
                    while ((line = sr.ReadLine()) != null)
                    {
                        args.Add(line);
                    }
                }
            }

            string outPipeName = "SkylineOutputPipe" + guidSuffix; // Not L10N
            using (var serverStream = new NamedPipeServerStream(outPipeName)) // Not L10N
            {
                var namedPipeServerConnector = new NamedPipeServerConnector();
                if (!namedPipeServerConnector.WaitForConnection(serverStream, outPipeName))
                {
                    return;
                }
                using (var sw = new CommandStatusWriter(new StreamWriter(serverStream)))
                {
                    RunCommand(args.ToArray(), sw);
                }
            }
        }
    }
}
