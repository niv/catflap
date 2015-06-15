using System.Security.AccessControl;
using System.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Polly;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using vbAccelerator.Components.Shell;
using System.Security.Cryptography;

namespace Catflap
{
    public class Repository
    {
        public const string TrustDBFile = "signify.pub";
        public const string SignatureFile = "catflap.json.sig";

        public class AuthException : Exception { };
        

        public Policy AuthPolicy { get; private set; }

        // The latest available manifest on the server.
        public Manifest LatestManifest { get; private set; }

        // The manifest currently active.
        public Manifest CurrentManifest { get; private set; }

        public bool AlwaysAssumeCurrent = false;

        public bool Simulate = false;

        private bool verifyUpdateFull = false;

        public string RootPath { get; private set; }
        public string AppPath { get; private set; }
        public string TmpPath { get; private set; }

        private WebClient wc;

        public string Username { get; private set; }
        public string Password { get; private set; }
        public void Authorize(string u, string p)
        {
            if (u == "" || u == null || p == "" || p == null)
            {
                this.Username = null; this.Password = null;
                wc.Credentials = null;
                File.Delete(AppPath + "\\auth");
            }
            else
            {
                this.Username = u; this.Password = p;
                wc.Credentials = new NetworkCredential(Username, Password);
                Logger.Info("%user% = " + u);
                System.IO.File.WriteAllText(AppPath + "\\auth", Username + ":" + Password);
            }
        }

        // Saves a new public key to our trust db, overwriting the old one.
        public void SaveTrustDB(string publicKey)
        {
            System.IO.File.WriteAllText(AppPath + "/" + TrustDBFile,
                "untrusted comment: added from UI on " + new DateTime().ToString() + "\n" +
                publicKey.Trim() + "\n");
        }

        public void ResetTrustDB()
        {
            try
            {
                File.Delete(AppPath + "/" + TrustDBFile);
            }
            catch (IOException) { }
        }

        public class DownloadStatusInfo
        {
            public double globalPercentage;
            public long globalFileCurrent;
            public long globalFileTotal;
            public long globalBytesCurrent;
            public long globalBytesTotal;

            public double currentPercentage;
            public string currentFile;
            public long currentBytes;
            public long currentTotalBytes;
            public long currentBps;
            public long currentBytesOnNetwork;
        }
        
        public struct RepositoryStatus
        {
            // Repository is current & intact.
            public bool current;

            // Total size of repository
            public long sizeOnRemote;
            public long sizeOnDisk;

            // How much we need to verify in the worst case.
            public long maxBytesToVerify;
            // How many bytes we're guessing at having to refresh really.
            public long guesstimatedBytesToVerify;

            // How much we need to transfer (on the wire) in the worst case.
            // public long maxBytesToXfer = -1;
            // How many bytes we're guessing at having to transfer (on the wire) really.
            // public long guesstimatedBytesToXfer = -1;

            // How many files (estimatedly) are outdated.
            public long fileCountToVerify;
            public long directoryCountToVerify;

            public List<Manifest.SyncItem> directoriesToVerify;
            public List<Manifest.SyncItem> filesToVerify;
        }

        public Security Security { get; private set; }

        public Security.VerifyResponse ManifestSecurityStatus { get; private set; }

        /* Can be set to true to have the updater restart after checking for new manifests. */
        public bool RequireRestart = false;

        public delegate void DownloadStatusInfoChanged(DownloadStatusInfo dsi);
        public delegate void DownloadProgressChanged(string fullFileName, int percentage = -1, long bytesReceived = -1, long bytesTotal = -1, long bytesPerSecond = -1);
        public delegate void DownloadEnd(bool wasError, string message, long bytesOnNetwork);
        public delegate void DownloadMessage(string message, bool showInProgressIndicator = false);
        public delegate bool DownloadVerifyChecksum(string file, string hash);

        public event DownloadStatusInfoChanged OnDownloadStatusInfoChanged;
        public event DownloadMessage OnDownloadMessage;

        public Repository(string rootPath, string appPath)
        {
            var fu = JsonConvert.DeserializeObject<Manifest>(System.IO.File.ReadAllText(appPath + "\\catflap.json"));
            init(fu.baseUrl, rootPath, appPath);
        }

        public Repository(string baseUrl, string rootPath, string appPath)
        {
            init(baseUrl, rootPath, appPath);
        }

        private void init(string baseUrl, string rootPath, string appPath)
        {
            this.RootPath = rootPath.NormalizePath();
            this.AppPath = appPath.NormalizePath();
            this.TmpPath = this.AppPath + "\\temp";

            Logger.Info("%app% = " + AppPath);
            Logger.Info("%root% = " + RootPath);
            Logger.Info("%temp% = " + TmpPath);

            this.Security = new Security(AppPath);

            Directory.CreateDirectory(appPath);
            
            wc = new WebClient();
            wc.Proxy = null;
            wc.UseDefaultCredentials = false;
            wc.Credentials = null;

            wc.BaseAddress = baseUrl;
            if (!wc.BaseAddress.EndsWith("/")) wc.BaseAddress += "/";

            if (File.Exists(appPath + "\\auth"))
            {
                var x = System.IO.File.ReadAllText(appPath + "\\auth").Split(new char[] { ':' }, 2);
                Authorize(x[0], x[1]);
            }

            AuthPolicy = Policy
                .Handle<WebException>((wex) =>
                    wex.Response is HttpWebResponse &&
                    (wex.Response as HttpWebResponse).StatusCode == HttpStatusCode.Unauthorized)
                .RetryForever(ex =>
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        var lw = new LoginWindow(this).ShowDialog();
                        if (!lw.Value)
                        {
                            Application.Current.Shutdown();
                            return;
                        }
                    });
                });
        }

        private void bw_updateRepositoryStatus(object sender, RunWorkerCompletedEventArgs e)
        {
            if (!e.Cancelled && e.Error == null)
                UpdateStatus();
        }

        private void EnsureWriteable(string obj)
        {
            new FileInfo(obj) { IsReadOnly = false }.Refresh();

            foreach (FileInfo e in Utils.GetDirectoryElements(obj))
            {
                e.IsReadOnly = false;
                e.Refresh();
            }
        }

        public RepositoryStatus Status { get; private set; }


        private DateTime updateLimiterLast = DateTime.Now;
        public void UpdateStatus(bool limit = false)
        {
            // Limit checks to x/second.
            if (limit && Status.directoriesToVerify != null && (DateTime.Now - updateLimiterLast) < TimeSpan.FromSeconds(1))
                return;

            updateLimiterLast = DateTime.Now;

            RepositoryStatus ret = new RepositoryStatus();

            IEnumerable<Manifest.SyncItem>
                outdated = new List<Manifest.SyncItem>(),
                dirsToCheck = new List<Manifest.SyncItem>(),
                filesToCheck = new List<Manifest.SyncItem>();

            if (!AlwaysAssumeCurrent)
            {
                outdated = LatestManifest.sync;

                if (CurrentManifest != null)
                    outdated = outdated.Where(f => !f.isCurrent(this));
                    
                dirsToCheck  = outdated.Where(f => f.name.EndsWith("/"));
                filesToCheck = outdated.Where(f => !f.name.EndsWith("/"));

                long outdatedSizeLocally = outdated.Select(n => n.SizeOnDisk(this)).Sum();
                long outdatedSizeRemote = outdated.Select(n => n.size).Sum();

                ret.guesstimatedBytesToVerify = outdatedSizeRemote - outdatedSizeLocally;
                ret.maxBytesToVerify = outdatedSizeRemote;

                ret.guesstimatedBytesToVerify = ret.guesstimatedBytesToVerify.Clamp(0);
                ret.maxBytesToVerify = ret.maxBytesToVerify.Clamp(0);

                ret.directoryCountToVerify = dirsToCheck.Count();
                ret.fileCountToVerify = dirsToCheck.Select(n => n.count).Sum() + filesToCheck.Count();
            }

            ret.directoriesToVerify = dirsToCheck.ToList();
            ret.filesToVerify = filesToCheck.ToList();

            ret.sizeOnRemote = LatestManifest.sync.Select(n => n.size).Sum();
            ret.sizeOnDisk = LatestManifest.sync.Select(n => n.SizeOnDisk(this)).Sum();

            ret.current = LatestManifest != null && CurrentManifest != null &&
                ret.fileCountToVerify == 0 &&
                ret.directoryCountToVerify == 0;

            this.Status = ret;
        }

        public Manifest GetManifestFromRemote()
        {
            Logger.Info("Getting manifest.");
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            string jsonStr = wc.DownloadString("catflap.json?catflap=" + fvi.FileVersion);
            System.IO.File.WriteAllText(AppPath + "\\catflap.remote.json", jsonStr);

            JsonTextReader reader = new JsonTextReader(new StringReader(jsonStr));

            JsonValidatingReader validatingReader = new JsonValidatingReader(reader);
            validatingReader.Schema = Manifest.Schema;
            // validatingReader.Schema.AllowAdditionalItems = false;
            // validatingReader.Schema.AllowAdditionalProperties = false;
            IList<string> messages = new List<string>();
            validatingReader.ValidationEventHandler += (o, a) => messages.Add(a.Message);
        
            JsonSerializer serializer = new JsonSerializer();
            Manifest mf = serializer.Deserialize<Manifest>(validatingReader);

            if (messages.Count > 0)
                throw new ValidationException("manifest is not valid: " + string.Join("\n", messages));

            mf.Validate(RootPath);

            return mf;
        }

        private bool RefreshManifestResource(string filename, bool neverOverwrite = false)
        {
            Logger.Info("RefreshManifestResource(\"" + filename + "\")");

            if (File.Exists(AppPath + "/" + filename) && neverOverwrite)
                return true;

            try
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
                var req = (HttpWebRequest)WebRequest.Create(LatestManifest.baseUrl + "/" + filename + "?catflap=" + fvi.FileVersion);
                if (File.Exists(AppPath + "/" + filename))
                    req.IfModifiedSince = new FileInfo(AppPath + "/" + filename).LastWriteTime;
                if (Username != null)
                    req.Credentials = new NetworkCredential(Username, Password);
                req.Proxy = null;

                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                {
                    using (Stream responseStream = res.GetResponseStream())
                    {
                        using (MemoryStream memoryStream = new MemoryStream())
                        {
                            byte[] buffer = new byte[4096];
                            int count = 0;
                            do
                            {
                                count = responseStream.Read(buffer, 0, buffer.Length);
                                memoryStream.Write(buffer, 0, count);
                            } while (count != 0);

                            System.IO.File.WriteAllBytes(AppPath + "/" + filename, memoryStream.ToArray());
                            File.SetLastWriteTime(AppPath + "/" + filename, res.LastModified);
                        }
                    }
                }
            }
            catch (WebException wex)
            {
                switch (((HttpWebResponse)wex.Response).StatusCode)
                {
                    case HttpStatusCode.NotModified:
                        return true;

                    case HttpStatusCode.Forbidden:
                    case HttpStatusCode.NotFound:
                        if (File.Exists(AppPath + "/" + filename))
                            File.Delete(AppPath + "/" + filename);
                       return false;
                }

                Logger.Info("while getting manifest resource: " + wex.ToString());
                return false;
            }

            return true;
        }

        // Refresh the remote manifest.
        public Task RefreshManifest(bool setNewAsCurrent = false)
        {
            return Task.Run(delegate()
            {
                BusyWindow.WithBusyWindow("Refreshing manifest!", () =>
                {
                    if (this.AlwaysAssumeCurrent)
                    {
                        if (File.Exists(AppPath + "\\catflap.json"))
                        {
                            LatestManifest = JsonConvert.DeserializeObject<Manifest>(System.IO.File.ReadAllText(AppPath + "\\catflap.json"));
                            CurrentManifest = LatestManifest;
                        }
                        else
                        {
                            throw new Exception("Cannot use AlwaysAssumeCurrent with no local manifest.");
                        }
                    }
                    else
                    {
                        LatestManifest = AuthPolicy.Execute(() => GetManifestFromRemote());

                        if (setNewAsCurrent)
                        {
                            System.IO.File.WriteAllText(AppPath + "\\catflap.json", JsonConvert.SerializeObject(LatestManifest));
                            CurrentManifest = LatestManifest;
                        }
                        else
                            if (File.Exists(AppPath + "\\catflap.json"))
                                CurrentManifest = JsonConvert.DeserializeObject<Manifest>(System.IO.File.ReadAllText(AppPath + "\\catflap.json"));

                        Console.WriteLine(LatestManifest);

                        RefreshManifestResource("catflap.bgimg");
                        RefreshManifestResource("favicon.ico");
                    }

                    UpdateStatus();
                });

                VerifyManifest();
            });
        }

        public void VerifyManifest()
        {
            this.ManifestSecurityStatus = new Catflap.Security.VerifyResponse();

            // Try to fetch signature file!
            BusyWindow.WithBusyWindow("Refreshing manifest", () => RefreshManifestResource(SignatureFile));

            /* Convenience trust weakening bad-bad-bad of the day: Always retrieve trustDB if it is over SSL.
             * We need to add a (compile) toggle to disable this in the future. */
            if (!File.Exists(AppPath + "/" + TrustDBFile) && wc.BaseAddress.StartsWith("https://"))
                BusyWindow.WithBusyWindow("Refreshing manifest", () => RefreshManifestResource(TrustDBFile, true));

            bool HaveTrustedKeys = File.Exists(AppPath + "/" + TrustDBFile);
            bool HaveSignature = File.Exists(AppPath + "/" + SignatureFile);

            // We always expect our repo to be signed if the old one is signed, we have a trustdb, or the new one starts being signed
            // This also ensures that we can never downgrade to unsigned without changing the updater binary.
            bool expectRepoSigned =
                HaveTrustedKeys ||
                HaveSignature ||
                LatestManifest.signed ||
                CurrentManifest.signed;

            if (expectRepoSigned && !HaveTrustedKeys)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var w = new TrustDBWindow(this);
                    var ret = w.ShowDialog();
                    if (!ret.GetValueOrDefault())
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                });
            }

            if (expectRepoSigned)
                this.ManifestSecurityStatus = Security.VerifySignature(SignatureFile, "catflap.remote.json");
        }

        private Task<bool> RunSyncItem(Manifest.SyncItem f,
            bool verify, bool simulate,
            DownloadProgressChanged dpc,
            DownloadEnd de,
            DownloadMessage dm,
            DownloadVerifyChecksum dvc,
            CancellationTokenSource cts,
            string overrideDestination = null)
        {
            switch (f.type)
            {
                case "rsync":
                    RSyncDownloader dd = new RSyncDownloader(this);
                    dd.appPath = AppPath;
                    dd.tmpPath = TmpPath;
                    dd.VerifyChecksums = verify;
                    dd.Simulate = simulate;
                    return dd.Download(LatestManifest.rsyncUrl + "/" + f.name, f, RootPath,
                        dpc, de, dm, dvc, cts, overrideDestination);

                case "delete":
                    return Task<bool>.Run(() =>
                    {
                        if (f.name.EndsWith("/") && Directory.Exists(RootPath + "/" + f.name)) {
                            dm.Invoke("Deleting directory " + f.name);
                            Directory.Delete(RootPath + "/" + f.name, true);
                        } else if (File.Exists(RootPath + "/" + f.name)) {
                            dm.Invoke("Deleting file " + f.name);
                            File.Delete(RootPath + "/" + f.name);
                        }
                        return true;
                    });

                default:
                    return null;
            }
        }

        public Task<long> UpdateEverything(bool verify, CancellationTokenSource cts)
        {
            this.verifyUpdateFull = verify;

            return RunAllSyncItems(cts);
        }

        private Task<long> RunAllSyncItems(CancellationTokenSource cts)
        {
            string basePath = RootPath;

            /* Cleanup leftover files from a forced abort. */
            if (Directory.Exists(TmpPath))
            {
                var di = new DirectoryInfo(TmpPath).GetFiles("*", SearchOption.TopDirectoryOnly);
                foreach (var tmpfile in di)
                {
                    OnDownloadMessage("<deleting leftover file from forced abort: " + tmpfile.Name + ">", true);
                    File.Delete(tmpfile.FullName);
                }
            }
            Directory.CreateDirectory(TmpPath);

            var updaterBinaryLastWriteTimeBefore = new FileInfo(Assembly.GetExecutingAssembly().Location).LastWriteTime;

            var info = new DownloadStatusInfo();

            info.globalFileTotal = Status.directoryCountToVerify + Status.fileCountToVerify;
            info.globalFileCurrent = 0;
            info.globalBytesTotal = Status.maxBytesToVerify;

            var toCheck = verifyUpdateFull ?
                LatestManifest.sync.Where((syncItem) => !(syncItem.ignoreExisting.GetValueOrDefault() && (File.Exists(syncItem.name) || Directory.Exists(syncItem.name)))) :
                (Status.filesToVerify.Concat(Status.directoriesToVerify));

            /*var globalFileTotalStart = verifyUpdateFull ?
                LatestManifest.sync.Select(syncItem => syncItem.count > 0 ? syncItem.count : 1).Sum() :
                Status.fileCountToVerify;*/

            var globalBytesTotalStart = verifyUpdateFull ?
                LatestManifest.sync.Select(syncItem => syncItem.size).Sum() :
                info.globalBytesTotal;


            return Task<long>.Factory.StartNew(delegate()
            {
                long bytesTotalPrev = 0;

                foreach (Manifest.SyncItem f in toCheck)
                {
                    info.currentFile = f.name;

                    // Hacky: Make sure we can write to our target. This is mostly due to rsync
                    // sometimes setting "ReadOnly" on .exe files when the remote is configured
                    // not quite correctly and only affects updating the updater itself.
                    try
                    {
                        // new FileInfo(rootPath + "/" + f.name) {IsReadOnly = false}.Refresh();
                        EnsureWriteable(RootPath + "/" + f.name);
                    }
                    catch (Exception e)
                    {
                    }

                    bool lastHashFailed = false;
                    string lastHashFileFailed = "";
                    string lastHashFailedMessage = "";

                    var t = RunSyncItem(f, verifyUpdateFull, Simulate, delegate(string fname, int percentage, long bytesReceived, long bytesTotal, long bytesPerSecond)
                    {
                        if (bytesReceived > -1) info.currentBytes = bytesReceived;
                        if (bytesTotal > -1) info.currentTotalBytes = bytesTotal;
                        if (bytesPerSecond > -1) info.currentBps = bytesPerSecond;
                        info.currentPercentage = bytesTotal > 0 ? (bytesReceived / (bytesTotal / 100.0)) / 100 : 0;

                        if (fname != info.currentFile)
                        {
                            info.globalBytesCurrent += bytesTotalPrev;
                            bytesTotalPrev = bytesTotal;
                            info.globalFileCurrent++;
                            info.currentFile = fname;
                        }
                        UpdateStatus(true);

                        info.globalFileTotal = Status.fileCountToVerify;

                        var bytesDone = info.globalBytesCurrent + bytesReceived;
                        info.globalPercentage = globalBytesTotalStart > 0 ? (bytesDone / (globalBytesTotalStart / 100.0)) / 100 : 1;

                        info.globalPercentage = info.globalPercentage.Clamp(0, 1);

                        OnDownloadStatusInfoChanged(info);

                    }, delegate(bool wasError, string str, long bytesOnNetwork)
                    {
                        if (wasError)
                        {
                            throw new Exception(str);
                        }
                        UpdateStatus();

                        info.currentFile = null;
                        info.currentBytesOnNetwork += bytesOnNetwork;

                        OnDownloadStatusInfoChanged(info);

                    }, delegate(string message, bool show)
                    {
                        OnDownloadMessage(message, show);

                    }, delegate(string file, string hash)
                    {
                        if (lastHashFailed)
                            return false;

                        lastHashFileFailed = file;

                        // Do not error hard on missing manifest hashes, since that can be trusted
                        // due to gpg signing.
                        if (f.hashes == null || !f.hashes.ContainsKey(file.ToLowerInvariant()))
                        {
                            OnDownloadMessage(file + " has no hash", false);
                            return true;
                        }
                            

                        if (hash != f.hashes[file.ToLowerInvariant()])
                        {
                            lastHashFailed = true;
                            lastHashFailedMessage = "Hash comparison failed for " +
                                file.ToLowerInvariant() + ". Expected: " +
                                f.hashes[file.ToLowerInvariant()] + ", got: " + hash;
                            OnDownloadMessage(lastHashFailedMessage, false);
                            return false;
                        }

                        OnDownloadMessage(file + ": hash OK (" + f.hashes[file.ToLowerInvariant()] + ")", false);

                        return true;
                    }, cts);

                    try
                    {
                        t.Wait();
                    }
                    catch (System.AggregateException x)
                    {
                        if (lastHashFailed)
                        {
                            OnDownloadMessage("hash verification failed", true);

                            throw new Exception("Problem verifying " + lastHashFileFailed + ". " +
                                " This might be a repository error, or someone is messing with your connection. " +
                                "Please contact your repository admin " +
                                "with the contents of this message:\n\n" + lastHashFailedMessage);
                            
                        }

                        if (x.InnerException is TaskCanceledException)
                        {
                            OnDownloadMessage("cancelled", true);
                            break;
                        }

                        else
                            throw x;
                    }
                    var ret = t.Result;



                    // Verify hashes
                    // Security.VerifyHashes(f.hashes)
                    /*foreach (KeyValuePair<string, string> entry in f.hashes)
                    {
                        // key = filename, value = md5 hash string
                        OnDownloadMessage("Verifying checksum of " + entry.Key, false);
                        var ourHash = Security.HashMD5(rootPath + "/" + entry.Key);
                        if (ourHash.ToLowerInvariant() != entry.Value.ToLowerInvariant())
                            throw new Exception("verify: " + entry.Key + "=" + entry.Value + " vs " + ourHash);
                    }*/

                    if (cts.IsCancellationRequested)
                    {
                        OnDownloadMessage("cancelled", true);
                        break;
                    }
                        

                    // This is a really ugly hackyhack to check if running binary was touched while we were upating.
                    // We just ASSUME that the binary was touched by the sync itself.
                    var updaterBinaryLastWriteTimeAfter = new FileInfo(Assembly.GetExecutingAssembly().Location).LastWriteTime;
                    if (Math.Abs((updaterBinaryLastWriteTimeAfter - updaterBinaryLastWriteTimeBefore).TotalSeconds) > 1)
                    {
                        RequireRestart = true;
                        break;
                    }

                    info.globalPercentage = globalBytesTotalStart > 0 ? (info.globalBytesCurrent / (globalBytesTotalStart / 100.0)) / 100 : 1;
                    info.globalPercentage = info.globalPercentage.Clamp(0, 1);

                    OnDownloadStatusInfoChanged(info);
                }

                return info.currentBytesOnNetwork;
            }, cts.Token);
        }

        public void MakeDesktopShortcut()
        {
            using (ShellLink shortcut = new ShellLink())
            {
                var fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                shortcut.Target = fi.FullName;
                shortcut.Arguments = "-run";
                shortcut.WorkingDirectory = RootPath;
                if (this.LatestManifest != null)
                    shortcut.Description = this.LatestManifest.title;
                else
                    shortcut.Description = fi.Name + " - run";
                shortcut.DisplayMode = ShellLink.LinkDisplayMode.edmNormal;

                shortcut.IconPath = AppPath + "\\favicon.ico";

                var fname = new string(shortcut.Description.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray());
                shortcut.Save(desktopPath + "/" + fname + ".lnk");
            }
        }
    }
}
