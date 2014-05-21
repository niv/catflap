using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.IO;
using System.Net;
using MahApps.Metro.Controls;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Threading;
using System.Diagnostics;
using MahApps.Metro;
using MahApps.Metro.Controls.Dialogs;
using System.Windows.Shell;
using System.Windows.Navigation;
using System.Windows.Controls;
using vbAccelerator.Components.Shell;
using System.Text;

namespace Catflap
{
    public partial class MainWindow : MetroWindow
    {
        private Repository repository;
        private Dictionary<string, string> resolvedVariables = new Dictionary<string, string>();
       
        private string rootPath;
        private string appPath;

        private void Log(String str, bool showMessageBox = false) {
            Console.WriteLine(str);

            logTextBox.Dispatcher.BeginInvoke((Action)(() =>
            {
            logTextBox.Text += DateTime.Now.ToString("HH:mm:ss") + "> " + str + "\n";
                logTextBox.ScrollToEnd();

            if (showMessageBox)
                this.ShowMessageAsync("Log", str);
            }));
        }

        private string bytesToHuman(long bytes)
        {
            if (bytes > 1024 * 1024 * 1024)
                return string.Format("{0:F2} GB", ((float)bytes / 1024 / 1024 / 1024));
            if (bytes > 1024 * 1024)
                return string.Format("{0:F2} MB", ((float)bytes / 1024 / 1024));
            if (bytes > 1024)
                return string.Format("{0:F2} KB", ((float)bytes / 1024));
            else
                return bytes + " B";
        }

        private string SubstituteVars(string a)
        {
            return a.
                Replace("%app%", appPath).
                Replace("%root%", rootPath).
                Replace("%user%", repository.Username);
        }


        private static string[] resourcesToUnpack =
        {
            "rsync.exe.gz" , "cygwin1.dll.gz",  "cyggcc_s-1.dll.gz"
        };
        private static string[] resourcesToPurge =
        {
            "cygintl-8.dll", "cygpopt-0.dll", "cygiconv-2.dll"
        };

        // UI colour states:
        // green/blue - all ok, repo up to date
        private Accent accentOK = new MahApps.Metro.Accent("Olive",
                new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Accents/Olive.xaml"));
        // orange     - repo not current
        private Accent accentWarning = new MahApps.Metro.Accent("Amber",
                new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Accents/Amber.xaml"));
        // red        - failure
        private Accent accentError = new MahApps.Metro.Accent("Crimson",
                new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Accents/Crimson.xaml"));
        // mauve     - busy
        private Accent accentBusy = new MahApps.Metro.Accent("Mauve",
                new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Accents/Mauve.xaml"));

        private Accent currTheme;

        private Accent SetTheme(Accent t)
        {
            Accent c = currTheme;
            if (currTheme != t)
            {
                currTheme = t;
                ThemeManager.ChangeTheme(this, t, Theme.Light);
            }
            return c;
        }

        private void SetUIState(bool enabled)
        {
            btnDownload.IsEnabled = enabled;
            checkboxSimulate.IsEnabled = enabled;

            if (!enabled)
                btnRun.IsEnabled = false;

            else
            {
                if (repository.CurrentManifest == null)
                {
                    btnRun.Content = "sync required";
                    btnRun.IsEnabled = false;
                }
                else
                    if (repository.LatestManifest.runAction == null)
                    {
                        btnRun.IsEnabled = false;
                        btnRun.Content = "manifest has no run action";
                    }
                    else
                    {
                        btnRun.Content = repository.LatestManifest.runAction.name;
                        if (repository.Status.current)
                            btnRun.IsEnabled = true;
                        else
                        {
                            btnRun.Content = "sync required";
                            btnRun.IsEnabled = repository.LatestManifest.runActionAllowOutdated;
                        }
                            
                    }
            }
        }

        private void SetUIProgressState(bool indeterminate, double percentage = -1, string message = null)
        {
            // globalProgress.IsIndeterminate = indeterminate;
            if (percentage >= 0)
            {
                // int p = (int) (percentage * 100).Clamp(0, 100);
                // labelDLSize.Text = p + "%";
                // globalProgress.Value = (percentage * 100).Clamp(0, 100);
            }
                
            if (message != null)
                labelDownloadStatus.Text = message.Trim();

            if (indeterminate)
                taskBarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
            else
                if (percentage == -1)
                    taskBarItemInfo.ProgressState = TaskbarItemProgressState.None;
                else
                {
                    taskBarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                    taskBarItemInfo.ProgressValue = percentage.Clamp(0, 1);
                }
        }

        private void SetGlobalStatus(bool lastOperationOK = true, string message = null, double percent = -1, string progressMsg = null)
        {
            //if (repository.IsBusy())
            //    SetTheme(accentBusy);
            if (!lastOperationOK)
                SetTheme(accentError);
            else if (repository.Status.current)
                SetTheme(accentOK);
            else
                SetTheme(accentWarning);

            var title = repository.LatestManifest != null && repository.LatestManifest.title != null ? repository.LatestManifest.title : "Catflap";
            if (message != null)
                this.Title = message;
            //else if (repository.IsBusy())
            //    this.Title = title + " - Busy";
            else
                this.Title = title;

            // labelRepoSize.Text = bytesToHuman(repository.Status.sizeOnRemote);
            if (repository.LatestManifest != null)
            {
                labelDLSize.Text = "";
                /*if (repository.Status.guesstimatedBytesToXfer > 0 || repository.Status.maxBytesToXfer > 0)
                {
                    if (repository.Status.guesstimatedBytesToXfer == repository.Status.maxBytesToXfer)
                        labelDLSize.Text = string.Format("{0}",
                            bytesToHuman(repository.Status.guesstimatedBytesToXfer)
                        );
                    else
                        labelDLSize.Text = string.Format("{0} to {1}",
                        bytesToHuman(repository.Status.guesstimatedBytesToXfer),
                        bytesToHuman(repository.Status.maxBytesToXfer)
                    );

                }*/
                if (repository.Status.guesstimatedBytesToVerify > 0 || repository.Status.maxBytesToVerify > 0)
                {
                    // labelDLSize.Visibility = System.Windows.Visibility.Visible;
                    if (repository.Status.guesstimatedBytesToVerify < 1) //  true || repository.Status.guesstimatedBytesToVerify == repository.Status.maxBytesToVerify)
                        labelDLSize.Text = "objects need syncing";
                    else
                        labelDLSize.Text += string.Format("{0} need syncing",
                            bytesToHuman(repository.Status.guesstimatedBytesToVerify)
                        );                    
                    /*
                    else
                        labelDLSize.Text += string.Format("{0} to {1}",
                            bytesToHuman(repository.Status.guesstimatedBytesToVerify),
                            bytesToHuman(repository.Status.maxBytesToVerify)
                        );*/
                }

                else
                {
                    //labelDLSize.Visibility = System.Windows.Visibility.Hidden;
                    labelDLSize.Text = bytesToHuman(repository.Status.sizeOnRemote) + " are current"; // "0 GB ";
                }


            }
            else
                labelDLSize.Text = "?";
        }
        private void RefreshBackgroundImage()
        {
            ImageBrush myBrush = new ImageBrush();
            myBrush.Stretch = Stretch.Uniform;
            Image image = new Image();

            if (File.Exists(appPath + "/catflap.bgimg"))
            {
                var bytes = System.IO.File.ReadAllBytes(appPath + "/catflap.bgimg");
                var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.EndInit();
                image.Source = bi;
            }
            else
                image.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/bgimg.png"));

            myBrush.ImageSource = image.Source;
            gridMainWindow.Background = myBrush;

            if (repository.LatestManifest != null && repository.LatestManifest.textColor != null)
            {
                var fgBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom(repository.LatestManifest.textColor));
                labelDLSize.Foreground = fgBrush;
                labelDownloadStatus.Foreground = fgBrush;
            }

            if (File.Exists(appPath + "/favicon.ico"))
            {
                var bytes = System.IO.File.ReadAllBytes(appPath + "/favicon.ico");
                var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.EndInit();
                this.Icon = bi;
            }
            else
            {
                this.Icon = new BitmapImage(new Uri("pack://application:,,,/Resources/app.ico"));
            }
        }

        private void webBrowser1_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            // Internal pages
            if (e.Uri == null || e.Uri.ToString() == "")
                return;

            // Urls on the main repo load in-page
            if (e.Uri.ToString().StartsWith(repository.CurrentManifest.baseUrl))
                return;

            // all others load in external.
            e.Cancel = true;
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString()
            });
        }

        public MainWindow() {
            InitializeComponent();

            labelDownloadStatus.Text = "";
            btnCancel.Visibility = System.Windows.Visibility.Hidden;

            var fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
            rootPath = Directory.GetCurrentDirectory();

            Log("%root% = " + rootPath);
            appPath = rootPath + "\\" + fi.Name + ".catflap";
            Log("%app% = " + appPath);
            Directory.SetCurrentDirectory(rootPath);

            if (!File.Exists(appPath + "\\catflap.json"))
            {
                var sw = new SetupWindow();
                if (!sw.SetupOk)
                {
                    var ret = sw.ShowDialog();
                    if (!ret.Value)
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                }
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = String.Join(".", fvi.FileVersion.Split('.').Take(3));
            btnHelp.Content = version;

            foreach (string src in resourcesToPurge)
            {
                var x = appPath + "\\" + src;
                if (File.Exists(x))
                {
                    Log("Deleting obsolete bundled file: " + src);
                    File.Delete(x);
                }                    
            }

            foreach (string src in resourcesToUnpack)
            {
                var dst = src;
                if (dst.EndsWith(".gz"))
                    dst = dst.Substring(0, dst.Length - 3);

                if (!File.Exists(appPath + "\\" + dst) || File.GetLastWriteTime(appPath + "\\" + dst) != File.GetLastWriteTime(fi.FullName))
                {
                    Log("Extracting bundled file: " + src);
                    App.ExtractResource(src, appPath + "\\" + dst);
                    File.SetLastWriteTime(appPath + "\\" + dst, File.GetLastWriteTime(fi.FullName));
                }
            }
                    

            this.repository = new Repository(rootPath, appPath);

            this.repository.OnDownloadStatusInfoChanged += delegate(Catflap.Repository.DownloadStatusInfo info)
            {
                Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (info.currentFile != null)
                        {
                            var msg = info.currentFile;
                            if (info.currentBps > 0)
                                msg += " - " + bytesToHuman(info.currentBps) + "/s";

                            SetUIProgressState(info.globalFileTotal == 0, info.globalPercentage, msg);
                        }
                        else
                        {
                            SetUIProgressState(info.globalFileTotal == 0, info.globalPercentage, null);
                        }
                        
                        
                        /*SetUIProgressState(info.globalFileTotal == 0, info.globalPercentage,
                            info.currentFile != null ? string.Format("{0} - {1}% of {2} at {3}/s",
                                info.currentFile.PathEllipsis(),
                                (int)(info.currentPercentage * 100),
                                bytesToHuman(info.currentTotalBytes),
                                bytesToHuman(info.currentBps)
                                ) : null);*/

                        if (info.globalFileTotal > 0)
                            SetGlobalStatus(true, string.Format("{0}%", (int)(info.globalPercentage * 100).Clamp(0, 100), info.globalPercentage));
                        else
                        {
                            SetGlobalStatus(true);

                        }                            
                     }));
            };

            this.repository.OnDownloadMessage += (string message) => Log(message);

            if (App.mArgs.Count() > 0 && App.mArgs[0].ToLower() == "-run")
            {
                UpdateAndRun(false);
            }
            else if (App.mArgs.Count() > 0 && App.mArgs[0].ToLower() == "-runwait")
            {
                UpdateAndRun(false);
            }
            else
            {
                UpdateRootManifest();
            }
        }

        private async void UpdateAndRun(bool waitForExit)
        {
            await UpdateRootManifest();
            await Sync(false);
            if (!repository.Status.current)
                return;
            await Task.Delay(100);
            WindowState = WindowState.Minimized;

            var t = RunAction(App.mArgs.Skip(1).ToArray());
            if (waitForExit)
                await t;
            else
                await Task.Delay(1000);

            Application.Current.Shutdown();
        }

        private static FileInfo[] GetDirectoryElements(string parentDirectory)
        {
            return new DirectoryInfo(parentDirectory).GetFiles("*", SearchOption.AllDirectories);
        }

        private async Task UpdateRootManifest(bool setNewAsCurrent = false)
        {
            SetUIProgressState(true);
            SetUIState(false);

            try
            {
                await repository.RefreshManifest(setNewAsCurrent);
            } catch (Exception err)
            {
                if (err is WebException)
                    {
                        MessageBox.Show("Could not retrieve repository manifest: " + err.Message);
                    }
                    else if (err is Repository.ValidationException)
                    {
                        MessageBox.Show("There are problems with the repository manifest " +
                            "(This is probably not your fault, it needs to be fixed in the repository!):" +
                            "\n\n" + err.Message);
                    }
                    else
                    MessageBox.Show("There has been some problem downloading/parsing the repository manifest:\n\n" +
                            err.ToString());

                Application.Current.Shutdown();
                return;
            }
            
            RefreshBackgroundImage();

            SetGlobalStatus(true);
            SetUIProgressState(false);

            var revText = string.Format("{0} -> {1}",
                repository.CurrentManifest != null && repository.CurrentManifest.revision != null ? repository.CurrentManifest.revision.ToString() : "?",
                repository.LatestManifest != null && repository.LatestManifest.revision != null ? repository.LatestManifest.revision.ToString() : "?"
            );
            Log("Revision: " + revText);

            if (repository.Status.current)
                btnDownload.Content = "verify";
            else
                btnDownload.Content = "sync";

            btnCancel.Visibility = System.Windows.Visibility.Hidden;

            /* Verify if we need to restart ourselves. */
            if (repository.RequireRestart)
            {
                await this.ShowMessageAsync("Restart required", "The updater needs to restart.");

                System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
                Application.Current.Shutdown();
            }

            SetUIState(true);
        }

        private CancellationTokenSource cts;
        private async Task Sync(bool fullVerify)
        {
            cts = new CancellationTokenSource();

            btnCancel.Visibility = System.Windows.Visibility.Visible;
            SetUIState(false);
            SetGlobalStatus(true, null, 0);
            SetUIProgressState(true);

            long bytesOnNetwork = 0;
            try
            {
                bytesOnNetwork = await repository.UpdateEverything(fullVerify, checkboxSimulate.IsChecked.Value, cts);
            }
            catch (Exception eee)
            {
                if (eee is AggregateException)
                    eee = eee.InnerException;

                if (eee.Message.StartsWith("@ERROR: auth failed on module "))
                {
                }

                Dispatcher.BeginInvoke((Action)(() =>
                {
                    SetGlobalStatus(false, "ERROR");
                    SetUIProgressState(false, -1, null);
                    Log("Error while downloading: " + eee.Message, true);
                    Console.WriteLine(eee.ToString());
                }));

                return;
            }
            finally
            {
                btnCancel.Visibility = System.Windows.Visibility.Hidden;
                SetUIState(true);
            }
            

            if (cts.IsCancellationRequested)
            {
                SetGlobalStatus(true);
                SetUIProgressState(false, -1, "ABORTED (" + bytesToHuman(bytesOnNetwork) + " of actual network traffic)");
                return;
            } 
            else
            {
                SetGlobalStatus(true, null, 100);
                // SetUIProgressState(false, 100, "");
                SetUIProgressState(false, 100, "Done (" + bytesToHuman(bytesOnNetwork) + " of actual network traffic)");
                Log("Verify/download complete.");
            }

            cts = null;

            await UpdateRootManifest(!checkboxSimulate.IsChecked.Value);
        }


        private async Task ExecuteAction(Catflap.Manifest.ManifestAction ac, string[] additionalArgs)
        {
            var cmd = SubstituteVars(ac.execute);
            var args = SubstituteVars(ac.arguments) + (" " + string.Join(" ", additionalArgs)).TrimEnd(' ');
            Log("Run Action: " + cmd + " " + args);

            Process pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = cmd;
            pProcess.StartInfo.UseShellExecute = true;
            pProcess.StartInfo.Arguments = args;
            pProcess.StartInfo.WorkingDirectory = rootPath;

            await Task.Run(delegate()
            {
                pProcess.Start();
                pProcess.WaitForExit();
            });
        }

        private async Task RunAction(string[] additionalArgs)
        {
            Accent old = SetTheme(accentBusy);
            SetGlobalStatus(true, "Running");
            SetUIState(false);
            try
            {
                await ExecuteAction(repository.LatestManifest.runAction, additionalArgs);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error executing runAction: " + ex.Message);
            }
            finally
            {
                SetUIState(true);
                SetTheme(old);
                SetGlobalStatus(true);
            }
        }

        private async void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            long free = (long) Native.GetDiskFreeSpace(rootPath);
            long needed = repository.Status.guesstimatedBytesToVerify + (200 * 1024 * 1024);
            if (free < needed)
            {
                var ret = await this.ShowMessageAsync("Disk space?",
                        "You seem to be running out of disk space on " + rootPath + ". " +
                        "Advanced calculations indicate you might not be able to " +
                        "sync everything: \n\n" +
                        bytesToHuman(free) +  " free, but \n" +
                        bytesToHuman(repository.Status.guesstimatedBytesToVerify) + " needed (plus change for temporary files).\n\n" +
                        "Do you still want to run this sync?",
                    MessageDialogStyle.AffirmativeAndNegative);
                if (MessageDialogResult.Negative == ret)
                    return;
            }

            if (repository.Status.current)
            {
                var ret = await this.ShowMessageAsync("Verify?", "Running a full sync will take longer, " +
                    "since it will verify checksums.\n\n" +
                    "This is usually not needed, except when you suspect corruption. You can cancel at any time.\n\n" +
                    "Are you sure this is what you want?",
                    MessageDialogStyle.AffirmativeAndNegative);
                if (MessageDialogResult.Negative == ret)
                    return;
            }
            var fullVerify = repository.Status.current;

            if (fullVerify && checkboxSimulate.IsChecked.Value)
            {
                await this.ShowMessageAsync("Cannot simulate", "Full verify does not support simulate-mode, sorry!");
                return;
            }

            await Sync(fullVerify);
        }

        private async void btnRun_Click(object sender, RoutedEventArgs e)
        {
            if (!repository.Status.current)
            {
                var ret = await this.ShowMessageAsync("Warning", "Warning, your files are outdated or incomplete. Do you still want to run?",
                    MessageDialogStyle.AffirmativeAndNegative);

                if (MessageDialogResult.Negative == ret)
                    return;
            }

            await RunAction(new string[]{});
        }

        private void btnShowHideLog_Click(object sender, RoutedEventArgs e)
        {
            var flyout = this.Flyouts.Items[0] as Flyout;
            flyout.IsOpen = !flyout.IsOpen;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (cts != null && !cts.IsCancellationRequested)
                cts.Cancel();
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            Process myProcess = new Process();
            myProcess.StartInfo.UseShellExecute = true;
            myProcess.StartInfo.FileName = "https://github.com/niv/catflap";
            myProcess.Start();
        }

        private void btnMakeShortcut_Click(object sender, RoutedEventArgs e)
        {
            using (ShellLink shortcut = new ShellLink())
            {
                var fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                shortcut.Target = fi.FullName;
                shortcut.Arguments = "-run";
                shortcut.WorkingDirectory = rootPath;
                if (repository.CurrentManifest != null)
                    shortcut.Description = repository.CurrentManifest.title;
                else
                    shortcut.Description = fi.Name + " - run";
                shortcut.DisplayMode = ShellLink.LinkDisplayMode.edmNormal;

                if (File.Exists(appPath + "/favicon.ico"))
                    shortcut.IconPath = appPath + "\\favicon.ico";

                var fname = new string(shortcut.Description.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray());
                shortcut.Save(desktopPath + "/" + fname + ".lnk");
            }

            this.ShowMessageAsync("Shortcut created", "A shortcut to update & run this repository was created on your Desktop.\n\n" +
                "Feel free to rename it and/or change the icon.");
        }
    }
}
