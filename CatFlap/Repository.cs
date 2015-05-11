﻿using System.Security.AccessControl;
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

namespace Catflap
{
    public class Repository
    {
        public class ValidationException : Exception { public ValidationException(string message) : base(message) { } };
        public class AuthException : Exception { };

        public Policy AuthPolicy;

        // The latest available manifest on the server.
        public Manifest LatestManifest { get; private set; }

        // The manifest currently active.
        public Manifest CurrentManifest { get; private set; }

        public bool AlwaysAssumeCurrent = false;

        private bool simulate = false;
        private bool verifyUpdateFull = false;
        private string rootPath;
        private string appPath;
        private string tmpPath;
        private WebClient wc;

        public string Username { get; private set; }
        public string Password { get; private set; }
        public void Authorize(string u, string p)
        {
            if (u == "" || u == null || p == "" || p == null)
            {
                this.Username = null; this.Password = null;
                wc.Credentials = null;
                File.Delete(appPath + "\\auth");
            }
            else
            {
                this.Username = u; this.Password = p;
                wc.Credentials = new NetworkCredential(Username, Password);
                System.IO.File.WriteAllText(appPath + "\\auth", Username + ":" + Password);
            }
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

        /* Can be set to true to have the updater restart after checking for new manifests. */
        public bool RequireRestart = false;

        public delegate void DownloadStatusInfoChanged(DownloadStatusInfo dsi);
        public delegate void DownloadProgressChanged(string fullFileName, int percentage = -1, long bytesReceived = -1, long bytesTotal = -1, long bytesPerSecond = -1);
        public delegate void DownloadEnd(bool wasError, string message, long bytesOnNetwork);
        public delegate void DownloadMessage(string message, bool showInProgressIndicator = false);

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
            this.rootPath = rootPath.NormalizePath();
            this.appPath = appPath.NormalizePath();
            this.tmpPath = this.appPath + "\\temp";

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
                {
                    outdated = outdated.Where(f => {

                        if (f.revision != 0)
                        {
                            var f_old = CurrentManifest.sync.Find(_f_old => _f_old.name == f.name);
                            // No old sync item: new item
                            if (f_old == null)
                                return !f.isCurrent(rootPath);

                            // No old revision: fallback to old check (and don't trigger force updates)
                            if (f_old != null && f_old.revision == 0)
                                return !f.isCurrent(rootPath);

                            // Mismatching revision: always force check
                            if (f_old != null && f_old.revision != f.revision)
                                return true;

                            // Matching revisions still incur the regular check to catch deleted/touched files
                        }
                        
                        return !f.isCurrent(rootPath);
                    });
                }
                    
                dirsToCheck  = outdated.Where(f => f.name.EndsWith("/"));
                filesToCheck = outdated.Where(f => !f.name.EndsWith("/"));

                ret.guesstimatedBytesToVerify = outdated.Select(n => n.SizeOnDisk(rootPath)).Sum();
                ret.maxBytesToVerify = outdated.Select(n => n.size).Sum();

                ret.guesstimatedBytesToVerify = ret.guesstimatedBytesToVerify.Clamp(0);
                ret.maxBytesToVerify = ret.maxBytesToVerify.Clamp(0);

                ret.directoryCountToVerify = dirsToCheck.Count();
                ret.fileCountToVerify = dirsToCheck.Select(n => n.count).Sum() + filesToCheck.Count();
                ret.directoriesToVerify = dirsToCheck.ToList();
                ret.filesToVerify = filesToCheck.ToList();
            }

            ret.sizeOnRemote = LatestManifest.sync.Select(n => n.size).Sum();
            ret.sizeOnDisk = LatestManifest.sync.Select(n => n.SizeOnDisk(rootPath)).Sum();

            ret.current = LatestManifest != null && CurrentManifest != null &&
                ret.fileCountToVerify == 0 &&
                ret.directoryCountToVerify == 0;

            this.Status = ret;
        }

        public Manifest GetManifestFromRemote()
        {
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            string jsonStr = wc.DownloadString("catflap.json?catflap=" + fvi.FileVersion);
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

            // Basic sanity checks for all sync items
            foreach (var syncItem in mf.sync)
            {
                var fullPath = (rootPath + "/" + syncItem.name).NormalizePath();

                if (fullPath == rootPath)
                    throw new ValidationException("cannot sync the root path directly at " + syncItem.name);

                if (!fullPath.StartsWith(rootPath))
                    throw new ValidationException("would place synced item outside of root path: " + syncItem.name);

                if (syncItem.type != "delete" && syncItem.type != "rsync")
                    throw new ValidationException("invalid sync item type: " + syncItem.type + " for " + syncItem.name);
            }

            if (!mf.baseUrl.StartsWith("http://") && !mf.baseUrl.StartsWith("https://"))
                throw new ValidationException("baseUrl does not start with http(s)://");
            if (!mf.rsyncUrl.StartsWith("rsync://"))
                throw new ValidationException("rsyncUrl does not start with rsync://");

            if (mf.version != Manifest.VERSION)
                throw new ValidationException("Your catflap.exe is of a different version than this repository (Expected: " +
                    mf.version + ", you: " + Manifest.VERSION + "). Please make sure you're using the right version.");

            if (mf.ignoreCase.HasValue)
                foreach (var syncItem in mf.sync)
                    if (!syncItem.ignoreCase.HasValue)
                        syncItem.ignoreCase = mf.ignoreCase.Value;

            if (mf.fuzzy.HasValue)
                foreach (var syncItem in mf.sync)
                    if (!syncItem.fuzzy.HasValue)
                        syncItem.fuzzy = mf.fuzzy.Value;

            if (mf.ignoreExisting.HasValue)
                foreach (var syncItem in mf.sync)
                    if (!syncItem.ignoreExisting.HasValue)
                        syncItem.ignoreExisting = mf.ignoreExisting.Value;

            if (mf.purge.HasValue)
                foreach (var syncItem in mf.sync)
                    if (!syncItem.purge.HasValue)
                        syncItem.purge = mf.purge.Value;

            return mf;
        }

        private void RefreshManifestResource(string filename)
        {
            try
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
                var req = (HttpWebRequest)WebRequest.Create(CurrentManifest.baseUrl + "/" + filename + "?catflap=" + fvi.FileVersion);
                if (File.Exists(appPath + "/" + filename))
                    req.IfModifiedSince = new FileInfo(appPath + "/" + filename).LastWriteTime;
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

                            System.IO.File.WriteAllBytes(appPath + "/" + filename, memoryStream.ToArray());
                            File.SetLastWriteTime(appPath + "/" + filename, res.LastModified);
                        }
                    }
                }
            }
            catch (WebException wex)
            {
                switch (((HttpWebResponse)wex.Response).StatusCode)
                {
                    case HttpStatusCode.Forbidden:
                    case HttpStatusCode.NotFound:
                        if (File.Exists(appPath + "/" + filename))
                            File.Delete(appPath + "/" + filename);
                        break;
                }

                Console.WriteLine("while getting manifest resource: " + wex.ToString());
            }
        }

        // Refresh the remote manifest.
        public Task RefreshManifest(bool setNewAsCurrent = false)
        {
            return Task.Run(delegate()
            {
                if (this.AlwaysAssumeCurrent)
                {
                    if (File.Exists(appPath + "\\catflap.json"))
                    {
                        LatestManifest = JsonConvert.DeserializeObject<Manifest>(System.IO.File.ReadAllText(appPath + "\\catflap.json"));
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
                        System.IO.File.WriteAllText(appPath + "\\catflap.json", JsonConvert.SerializeObject(LatestManifest));
                        CurrentManifest = LatestManifest;
                    }
                    else
                        if (File.Exists(appPath + "\\catflap.json"))
                            CurrentManifest = JsonConvert.DeserializeObject<Manifest>(System.IO.File.ReadAllText(appPath + "\\catflap.json"));

                    Console.WriteLine(LatestManifest);

                    RefreshManifestResource("catflap.bgimg");
                    RefreshManifestResource("favicon.ico");
                }

                UpdateStatus();
            });
        }

        private Task<bool> RunSyncItem(Manifest.SyncItem f,
            bool verify, bool simulate,
            DownloadProgressChanged dpc, DownloadEnd de, DownloadMessage dm,
            CancellationTokenSource cts,
            string overrideDestination = null)
        {
            switch (f.type)
            {
                case "rsync":
                    RSyncDownloader dd = new RSyncDownloader(this);
                    dd.appPath = appPath;
                    dd.tmpPath = tmpPath;
                    dd.VerifyChecksums = verify;
                    dd.Simulate = simulate;
                    return dd.Download(LatestManifest.rsyncUrl + "/" + f.name, f, rootPath,
                        dpc, de, dm, cts, overrideDestination);

                case "delete":
                    return Task<bool>.Run(() =>
                    {
                        if (f.name.EndsWith("/") && Directory.Exists(rootPath + "/" + f.name)) {
                            dm.Invoke("Deleting directory " + f.name);
                            Directory.Delete(rootPath + "/" + f.name, true);
                        } else if (File.Exists(rootPath + "/" + f.name)) {
                            dm.Invoke("Deleting file " + f.name);
                            File.Delete(rootPath + "/" + f.name);
                        }
                        return true;
                    });

                default:
                    return null;
            }
        }

        public Task<long> UpdateEverything(bool verify, bool simulate,
                CancellationTokenSource cts)
        {
            this.verifyUpdateFull = verify;
            this.simulate = simulate;

            return RunAllSyncItems(cts);
        }

        private Task<long> RunAllSyncItems(CancellationTokenSource cts)
        {
            string basePath = rootPath;

            /* Cleanup leftover files from a forced abort. */
            if (Directory.Exists(tmpPath))
            {
                var di = new DirectoryInfo(tmpPath).GetFiles("*", SearchOption.TopDirectoryOnly);
                foreach (var tmpfile in di)
                {
                    OnDownloadMessage("<deleting leftover file from forced abort: " + tmpfile.Name + ">", true);
                    File.Delete(tmpfile.FullName);
                }
            }
            Directory.CreateDirectory(tmpPath);

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
                        EnsureWriteable(rootPath + "/" + f.name);
                    }
                    catch (Exception e)
                    {
                    }

                    var t = RunSyncItem(f, verifyUpdateFull, simulate, delegate(string fname, int percentage, long bytesReceived, long bytesTotal, long bytesPerSecond)
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
                    }, cts);

                    try
                    {
                        t.Wait();
                    }
                    catch (System.AggregateException x)
                    {
                        if (x.InnerException is TaskCanceledException)
                        {
                            break;
                        }

                        else
                            throw x;
                    }
                    var ret = t.Result;

                    if (cts.IsCancellationRequested)
                        break;

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
    }
}
