﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
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
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Resources;

namespace Catflap
{
    public partial class MainWindow : MetroWindow
    {
        private Repository repository;
        private Dictionary<string, string> resolvedVariables = new Dictionary<string, string>();
       
        private bool IgnoreRepositoryLock = false;

        private bool CloseAfterSync = false;


        // UI colour states:
        // green/blue - all ok, repo up to date
        public static Accent accentOK = ThemeManager.GetAccent("Olive");
        // orange     - repo not current
        public static Accent accentWarning = ThemeManager.GetAccent("Amber");
        // red        - failure
        public static Accent accentError = ThemeManager.GetAccent("Crimson");
        // mauve     - busy
        public static Accent accentBusy = ThemeManager.GetAccent("Mauve");

        private Accent currTheme;

        public Accent SetTheme(Accent t)
        {
            Accent c = currTheme;
            if (currTheme != t)
            {
                currTheme = t;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var themestr = (repository.LatestManifest != null && repository.LatestManifest.darkTheme) ?
                        "BaseDark" : "BaseLight";

                    ThemeManager.ChangeAppStyle(Application.Current, t, ThemeManager.GetAppTheme(themestr));
                });
            }
            return c;
        }

        private void SetUIState(bool enabled)
        {
            btnVerify.IsEnabled = enabled && repository.Status.current;

            var wantEnabled = false;

            if (repository.LatestManifest != null)
                wantEnabled = true;

            if (repository.Status.current)
            {
                if (repository.LatestManifest.runAction == null)
                {
                    btnRun.Content = Text.t("run_no_action");
                    wantEnabled = false;
                }
                else
                {
                    btnRun.Content = Text.t("run_action_run");
                    wantEnabled = true;
                }
            }
            else
            {
                btnRun.Content = Text.t("run_action_sync");
                wantEnabled = true;
            }

            btnRun.IsEnabled = enabled && wantEnabled;
        }

        enum UIState {
            Unchanged,
            Busy,
            OK,
            Error
        };

        private void RefreshProgress(UIState state = UIState.Unchanged, string message = null)
        {
            if (message != null)
                labelDownloadStatus.Dispatcher.Invoke((Action)(() => labelDownloadStatus.Text = message.Trim()));

            bool busy = state == UIState.Busy || cts != null;

            taskBarItemInfo.Dispatcher.Invoke((Action)(() =>
            {
                if (busy)
                {
                    taskBarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
                }                    
                else
                {
                    taskBarItemInfo.ProgressState = TaskbarItemProgressState.None;
                }
            }));

            if (busy)
                SetTheme(accentBusy);
            else if (state == UIState.Error)
                SetTheme(accentError);
            else if (state == UIState.OK)
            {
                if (repository.Status.current)
                    SetTheme(accentOK);
                else
                    SetTheme(accentWarning);
            }

            this.Dispatcher.Invoke(() =>
            {
                progressIndeterminate.Visibility = busy ? Visibility.Visible : Visibility.Hidden;

                if (repository.LatestManifest != null)
                {

                    labelDLSize.ToolTip = Text.t("status_sync_tooltip",
                        repository.LatestManifest.sync.Select(f => f.count > 0 ? f.count : 1).Sum(),
                        repository.LatestManifest.sync.Count(),
                        repository.LatestManifest.sync.Select(f => f.mtime).Max().PrettyInterval()
                    );

                    if (repository.Status.directoriesToVerify.Any() || repository.Status.filesToVerify.Any())
                    {
                        labelDLSize.ToolTip += "\n" + Text.t("status_sync_tooltip_outdated");
                        foreach (var e in (repository.Status.directoriesToVerify.Concat(repository.Status.filesToVerify)))
                        {
                            labelDLSize.ToolTip += "\n" + e.name;
                            var tt = e.ToTransfer(repository);
                            if (tt > 1024)
                                labelDLSize.ToolTip += " ~ " + tt.BytesToHuman();
                            else if (e.type == "delete")
                                labelDLSize.ToolTip += " (" + Text.t("sync_will_be_deleted") + ")";
                        }
                    }

                    labelDLSize.Text = "";

                    this.Title = repository.LatestManifest != null && repository.LatestManifest.title != null ?
                        repository.LatestManifest.title : "Catflap";

                    if (repository.AlwaysAssumeCurrent)
                    {
                        labelDLSize.Text = Text.t("status_offline");
                    }

                    else if (repository.Status.guesstimatedBytesToVerify > 0 || repository.Status.maxBytesToVerify > 0)
                    {
                        if (repository.Status.guesstimatedBytesToVerify < 1)
                            labelDLSize.Text = Text.t(busy ? "busy_sync_objects_need_syncing" : "status_sync_objects_need_syncing");
                        else
                            labelDLSize.Text += Text.t(busy ? "busy_sync_n_need_syncing" : "status_sync_n_need_syncing",
                                repository.Status.guesstimatedBytesToVerify.BytesToHuman()
                            );

                        this.Title = labelDLSize.Text;
                    }
                    else
                    {
                        labelDLSize.Text = Text.t("status_n_in_sync", repository.Status.sizeOnRemote.BytesToHuman());
                    }


                }
                else
                    labelDLSize.Text = "?";
            });

        }

        private void RefreshBackgroundImage()
        {
            ImageBrush myBrush = new ImageBrush();
            myBrush.Stretch = Stretch.Uniform;
            Image image = new Image();

            if (File.Exists(repository.AppPath + "/catflap.bgimg"))
            {
                var bytes = System.IO.File.ReadAllBytes(repository.AppPath + "/catflap.bgimg");
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

            if (File.Exists(repository.AppPath + "/favicon.ico"))
            {
                var bytes = System.IO.File.ReadAllBytes(repository.AppPath + "/favicon.ico");
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

        public MainWindow() {
            InitializeComponent();

            btnVerify.Content = Text.t("mainwindow_button_verify");
            btnOpenInExplorer.Content = Text.t("mainwindow_button_openfolder");
            btnMakeShortcut.Content = Text.t("mainwindow_button_shortcut");
            btnShowHideLog.Content = Text.t("mainwindow_button_more");
            logFlyout.Header = Text.t("mainwindow_logflyout_header");

            this.Visibility = System.Windows.Visibility.Collapsed;

            labelDownloadStatus.Text = "";
            btnCancel.Visibility = System.Windows.Visibility.Hidden;

            var fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
            string rootPath = Directory.GetCurrentDirectory();

            string appPath = rootPath + "\\" + fi.Name + ".catflap";
            Directory.SetCurrentDirectory(rootPath);

            Logger.OnLogMessage += (string msg) =>
                logTextBox.Dispatcher.Invoke((Action)(() =>
                {
                    logTextBox.Text += msg;
                    logTextBox.ScrollToEnd();
                }));

            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string major = String.Join(".", fvi.FileVersion.Split('.').Take(3));
            string point = String.Join(".", fvi.FileVersion.Split('.').Skip(3));
            btnHelp.Content = "catflap v" + major + (point == "0" ? "" : "." + point);

            Logger.Info("Version: " + btnHelp.Content);

            if (!File.Exists(appPath + "\\catflap.json"))
            {
                Logger.Info("First time setup.");
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

            EmbeddedResources.Update(appPath);
            
            this.Activated += new EventHandler((o, ea) =>
            {
                if (repository.LatestManifest != null)
                {
                    repository.UpdateStatus();
                    this.RefreshProgress(UIState.Unchanged);
                }
            });

            this.KeyDown += new KeyEventHandler(async (o, kea) => {
                if (kea.Key == Key.F5 && cts == null)
                {
                    await UpdateRootManifest();
                    repository.UpdateStatus();
                    this.RefreshProgress(UIState.OK);
                }
            });

            this.repository = new Repository(rootPath, appPath);

            if (File.Exists(appPath + "/log.txt"))
                File.Delete(appPath + "/log.txt");

            if (appPath != null && Directory.Exists(appPath))
                Logger.OnLogMessage += (string msg) => {
                    if (msg != null)
                        try
                        {
                            System.IO.File.AppendAllText(appPath + "/log.txt", msg);
                        }
                        catch (IOException) { } // Cannot write log file. Weird but nothing to be done about it.
                };

            this.repository.OnDownloadStatusInfoChanged += OnDownloadStatusInfoChangedHandler;

            this.repository.OnDownloadMessage += (string message, bool show) =>
            {
                if (show)
                    labelDownloadStatus.Dispatcher.Invoke((Action)(() => labelDownloadStatus.Text = message.Trim()));
                Logger.Info(message);
            };

            Logger.Info("argv: " + String.Join(" ", App.mArgs));

            if (App.mArgs.Contains("-nolock"))
            {
                App.mArgs = App.mArgs.Where(x => x != "-nolock").ToArray();
                IgnoreRepositoryLock = true;
            }

            if (App.mArgs.Contains("-simulate"))
            {
                repository.Simulate = true;
            }

            if (App.mArgs.Contains("-nocheck"))
            {
                App.mArgs = App.mArgs.Where(x => x != "-nocheck").ToArray();
                this.repository.AlwaysAssumeCurrent = true;
            }

            if (App.mArgs.Contains("-fixperm"))
            {
                if (!Utils.IsUserAdministrator())
                {
                    MessageBox.Show("-fixperm requires Administator privileges, but " +
                        "I can't request those myself.\n\nTry 'Run as administrator' with a shortcut.");
                }
                else if (MessageBox.Show(
                    "This resets all permissions on the current repository to the currently logged in user, " +
                    "removes all custom object ACLs and only allows inherited access rights " +
                    "(those of the parent directory " + Directory.GetParent(rootPath) + ") to apply.\n\n" +
                    "!! This is a destructive operation and cannot be undone.\n" +
                    "!! This will not touch any files outside of the repository root " + rootPath + ".\n\n" +
                    "Continue?",
                    "Force-reset permissions on complete repository?",
                    MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                {
                    Utils.FixPermissions(rootPath);
                    MessageBox.Show("All done. Exiting.");
                }
                Application.Current.Shutdown();
            }
            else if (App.mArgs.Contains("-run"))
            {
                App.mArgs = App.mArgs.Where(x => x != "-run").ToArray();
                UpdateAndRun(false);
            }
            else if (App.mArgs.Contains("-runwait"))
            {
                App.mArgs = App.mArgs.Where(x => x != "-runwait").ToArray();
                UpdateAndRun(false);
            }
            else
                UpdateRootManifest();
        }

        private void OnDownloadStatusInfoChangedHandler(Catflap.Repository.DownloadStatusInfo info)
        {
            if (info.currentFile != null)
            {
                var msg = info.currentFile.PathEllipsis(60);
                if (info.currentBps > 0)
                    msg += " - " + info.currentBps.BytesToHuman() + "/s";
                if (info.currentPercentage > 0)
                    msg += ", " + ((int)(info.currentPercentage * 100)) + "%";

                if (info.details != "")
                    labelDownloadStatus.Dispatcher.Invoke(() => {
                        labelDownloadStatus.ToolTip = info.details;
                    });

                this.RefreshProgress(UIState.Busy, msg);
            }
            else
            {
                this.RefreshProgress(UIState.Busy);
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

            var t = RunAction(repository.LatestManifest.runAction);
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
            RefreshProgress(UIState.Busy);
            SetUIState(false);

            Retry:

            try
            {
                await repository.RefreshManifest(setNewAsCurrent);
            }
            catch (Exception err)
            {
                if (err is WebException)
                {
                    // WebException wex = (WebException) err;

                    MessageBox.Show(Text.t("err_manifest_network_failure_offline_mode", err.Message), err.Message);
                    repository.AlwaysAssumeCurrent = true;
                    goto Retry;
                }
                else if (err is ValidationException)
                {
                    MessageBox.Show(Text.t("err_manifest_parse_error", err.Message));
                }
                else
                    MessageBox.Show(Text.t("err_manifest_network_failure_exit", err.ToString()));

                Application.Current.Shutdown();
                return;
            }
            this.Visibility = System.Windows.Visibility.Visible;
            
            RefreshBackgroundImage();

            RefreshProgress(UIState.OK);

            var revText = string.Format("{0} -> {1}",
                repository.CurrentManifest != null && repository.CurrentManifest.revision != null ? repository.CurrentManifest.revision.ToString() : "?",
                repository.LatestManifest != null && repository.LatestManifest.revision != null ? repository.LatestManifest.revision.ToString() : "?"
            );
            Logger.Info("Revision: " + revText);

            /*if (repository.Status.current)
                btnDownload.Content = "verify";
            else
                btnDownload.Content = "sync";*/

            btnCancel.Visibility = System.Windows.Visibility.Hidden;

            /* Verify if we need to restart ourselves. */
            if (repository.RequireRestart)
            {
                await this.ShowMessageAsync(
                    Text.t("binary_updated_restart_required"),
                    Text.t("binary_updated_restart_required_long"));

                System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
                Application.Current.Shutdown();
            }

            if (!IgnoreRepositoryLock && repository.LatestManifest.locked != "")
            {
                await this.ShowMessageAsync(Text.t("repository_locked"), repository.LatestManifest.locked);
                Application.Current.Shutdown();
            }

            this.BorderThickness = new Thickness(repository.LatestManifest.border ? 1 : 0);

            var effect = repository.LatestManifest.dropShadows ? new DropShadowEffect() {
                // Color = (Color) ColorConverter.ConvertFromString(repository.LatestManifest.textColor),
                Opacity = 0.5,
                BlurRadius = 10 } : null;
            btnRun.Effect = effect;
            labelDLSize.Effect = effect;
            labelDownloadStatus.Effect = effect;
            signatureStatus.Effect = effect;

            signatureStatusContainer.Width = double.NaN;
            signatureStatusContainer.Visibility = System.Windows.Visibility.Visible;

            switch (repository.ManifestSecurityStatus.Status)
            {
                case Security.VerifyResponse.VerifyResponseStatus.NOT_CHECKED:
                    signatureStatusContainer.Width = 0;
                    signatureStatusContainer.Visibility = System.Windows.Visibility.Hidden;
                    break;

                case Security.VerifyResponse.VerifyResponseStatus.SIGNATURE_DOES_NOT_VERIFY:
                    await this.ShowMessageAsync(Text.t("repository_crypto_signature_incorrect"),
                        Text.t("repository_crypto_signature_incorrect_long"));
                    Application.Current.Shutdown();
                    break;
                    
                case Security.VerifyResponse.VerifyResponseStatus.OK:
                    signatureStatus.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/padlock-white.png"));
                    signatureStatus.ToolTip = Text.t("repository_crypto_signature_ok_long");
                    break;

                case Security.VerifyResponse.VerifyResponseStatus.NO_LOCAL_SIGNATURE:
                    await this.ShowMessageAsync(Text.t("repository_crypto_signature_missing"),
                        Text.t("repository_crypto_signature_missing_long"));
                    Application.Current.Shutdown();
                    break;

                // This shouldn't ever show up, because we have the UI popup for sig updates in
                // Repository.VerifyManifest
                case Security.VerifyResponse.VerifyResponseStatus.NO_LOCAL_PUBKEY:
                    signatureStatus.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/padlock-red.png"));
                    signatureStatus.ToolTip = Text.t("repository_crypto_pubkey_missing_long");
                    break;

                case Security.VerifyResponse.VerifyResponseStatus.PUBKEY_MISMATCH:
                    await this.ShowMessageAsync(Text.t("repository_crypto_pubkey_mismatch"),
                        Text.t("repository_crypto_pubkey_mismatch_long"));
                    Application.Current.Shutdown();
                    break;

                case Security.VerifyResponse.VerifyResponseStatus.SIGNATURE_OUTDATED:
                    await this.ShowMessageAsync(Text.t("repository_crypto_signature_outdated"),
                        Text.t("repository_crypto_signature_outdated_long"));
                    Application.Current.Shutdown();
                    break;

                default:
                    throw new Exception("Internal error: Unhandled case for Repository.SignatureStatusType. This is a bug.");
            }

            SetUIState(true);
        }

        private CancellationTokenSource cts;
        private async Task Sync(bool fullVerify)
        {
            cts = new CancellationTokenSource();

            btnCancel.Content = "stop";
            btnCancel.IsEnabled = true;
            btnCancel.Visibility = System.Windows.Visibility.Visible;
            SetUIState(false);
            RefreshProgress(UIState.Busy);

            long bytesOnNetwork = 0;
            try
            {
                bytesOnNetwork = await repository.UpdateEverything(fullVerify, cts);
                RefreshProgress(UIState.Busy);
            }
            catch (Exception eee)
            {
                if (eee is AggregateException)
                    eee = eee.InnerException;

                if (eee.Message.StartsWith("@ERROR: auth failed on module "))
                {
                }

                RefreshProgress(UIState.Error);

                Logger.Info("Error while downloading: " + eee.Message);
                this.ShowMessageAsync("Error while downloading", eee.Message);

                return;
            }
            finally
            {
                btnCancel.Visibility = System.Windows.Visibility.Hidden;
                SetUIState(true);
            }
            
            bool wasCancel = cts.IsCancellationRequested;
            cts = null;

            if (wasCancel)
            {
                // SetGlobalStatus(true, Text.t("status_aborted"));
                RefreshProgress(UIState.OK, Text.t("status_aborted_n_traffic", bytesOnNetwork.BytesToHuman()));
            } 
            else
            {
                // SetGlobalStatus(true, "100%", 1);
                RefreshProgress(UIState.OK, Text.t("status_done_n_traffic", bytesOnNetwork.BytesToHuman()));
                Logger.Info("Verify/download complete.");

                await UpdateRootManifest(!repository.Simulate);
            }

            if (CloseAfterSync)
                this.Close();
        }

        private async Task RunAction(Manifest.ManifestAction action)
        {
            Accent old = SetTheme(accentBusy);
            RefreshProgress(UIState.Busy);
            SetUIState(false);
            try
            {
                await action.Run(repository,
                    action.passArguments ? App.mArgs : new string[] { });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Text.t("run_error", ex.Message));
            }
            finally
            {
                SetUIState(true);
                SetTheme(old);
                RefreshProgress(UIState.OK);
            }
        }

        private async void btnRun_Click(object sender, RoutedEventArgs e)
        {
            long free = (long) Native.GetDiskFreeSpace(repository.RootPath);
            long needed = repository.Status.guesstimatedBytesToVerify + (200 * 1024 * 1024);
            if (free < needed)
            {
                var ret = await this.ShowMessageAsync(Text.t("warn_disk_space"),
                    Text.t("warn_disk_space_long", repository.RootPath, free.BytesToHuman(),
                        repository.Status.guesstimatedBytesToVerify.BytesToHuman()),
                    MessageDialogStyle.AffirmativeAndNegative);
                if (MessageDialogResult.Negative == ret)
                    return;
            }

            if (repository.Status.current)
                await RunAction(repository.LatestManifest.runAction);
            else
                await Sync(false);
        }
        
        private async void btnVerify_Click(object sender, RoutedEventArgs e)
        {
            if (repository.Status.current)
            {
                var ret = await this.ShowMessageAsync(Text.t("warn_verify"),
                    Text.t("warn_verify_long"),
                    MessageDialogStyle.AffirmativeAndNegative);
                if (MessageDialogResult.Negative == ret)
                    return;
            }
            var fullVerify = repository.Status.current;

            if (fullVerify && repository.Simulate)
            {
                await this.ShowMessageAsync(Text.t("err_cannot_simulate"),
                    Text.t("err_cannot_simulate_long"));
                return;
            }

            await Sync(true);
        }

        private void btnShowHideLog_Click(object sender, RoutedEventArgs e)
        {
            var flyout = this.Flyouts.Items[0] as Flyout;
            flyout.IsOpen = !flyout.IsOpen;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (cts != null && !cts.IsCancellationRequested)
            {
                cts.Cancel();
                btnCancel.Content = "stopping (patience!)";
                btnCancel.IsEnabled = false;
            }
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (cts != null && !cts.IsCancellationRequested)
            {
                cts.Cancel();
                e.Cancel = true;
                CloseAfterSync = true;
                btnCancel.Content = "stopping (patience!)";
                btnCancel.IsEnabled = false;
            }
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            Process myProcess = new Process();
            myProcess.StartInfo.UseShellExecute = true;
            myProcess.StartInfo.FileName = "https://github.com/niv/catflap";
            myProcess.Start();
        }

        private void btnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Process myProcess = new Process();
            myProcess.StartInfo.UseShellExecute = true;
            myProcess.StartInfo.FileName = repository.RootPath;
            myProcess.Start();
        }

        private void btnMakeShortcut_Click(object sender, RoutedEventArgs e)
        {
            repository.MakeDesktopShortcut();
            this.ShowMessageAsync(Text.t("shortcut_created"), Text.t("shortcut_created_long"));
        }

        private void btnSignatureStatus_Click(object sender, RoutedEventArgs e)
        {
            string msg = "";
            if (this.signatureStatus.ToolTip != null)
                msg = this.signatureStatus.ToolTip.ToString();
            if (repository.ManifestSecurityStatus.signingKey != null)
                msg += "\n\n" +
                    Text.t("repository_crypto_signing_key") + "\n" +
                    repository.ManifestSecurityStatus.signingKey + "\n" +
                    Text.t("repository_crypto_trusted_comment") + "\n" +
                    repository.ManifestSecurityStatus.trustedComment;

            this.ShowMessageAsync("", msg);
        }

        private void btnLogfile_Click(object sender, MouseButtonEventArgs e)
        {
            Process myProcess = new Process();
            myProcess.StartInfo.UseShellExecute = true;
            myProcess.StartInfo.FileName = repository.AppPath + "/log.txt";
            myProcess.Start();
        }
    }
}
