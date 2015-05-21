using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System.IO;
using System.Linq;

namespace Catflap
{
    public partial class Manifest
    {
        public class SyncItem
        {
            // The filename or dirname of the sync item in question.
            // Make sure that directories ALWAYS terminate with a /.
            public string name;

            // The serverside revision. This is used to quickly match local revisions
            // is incremented by the serverside utility automatically.
            // Note that this is a internal uint, not a user-supplied string.
            public uint revision;

            // The sync type. Currently supported:
            // * "rsync": Download the given item via rsync (default).
            // * "delete": Delete the given directory or file from the local repository.
            public string type = "rsync";

            // Uncompressed size in bytes
            public long size;

            // Dir only: Number of files/directories in directory.
            public long count;
            // Dir only: Pass --delete to rsync when syncing directory/ items.
            // Will remove all untracked files that are not present
            // in the repository.
            public bool? purge;

            // Transfer mode. one of 'inplace', 'replace' (default)
            public string mode;

            [JsonProperty(ItemConverterType = typeof(IsoDateTimeConverter))]
            public DateTime mtime;

            // Makes the updater skip syncing this file or directory if it
            // exists on the client. Can be used for initial config-file syncs.
            public bool? ignoreExisting;

            // Do fuzzy-matching on the target. (--fuzzy)
            public bool? fuzzy;

            // Ignore case when updating/copying files (--ignore-case).
            // Note: This needs a special patch on the server!
            public bool? ignoreCase;

            // Returns true if this sync item is current on the given rootPath.
            public bool isCurrent(Repository repository)
            {
                return !isOutdated(repository);
            }

            public long SizeOnDisk(Repository repository)
            {
                long sz = 0;

                if (this.name.EndsWith("/"))
                    sz = (Utils.GetDirectoryElements(repository.RootPath + "/" + this.name).Sum(file => file.Length));

                else
                {
                    var fileInfo = new FileInfo(repository.RootPath + "/" + this.name);

                    if (fileInfo.Exists)
                        sz += fileInfo.Length;

                    // Check partial dir
                    var partialInfo = new FileInfo(fileInfo.Directory.FullName + "/catflap.partials/" + fileInfo.Name);

                    // Check the current-transfer temp dir for in-flight ransfers
                    if (Directory.Exists(repository.TmpPath))
                    {
                        sz += Directory.EnumerateFiles(repository.TmpPath).
                            Where(x => new FileInfo(x).Name.StartsWith(fileInfo.Name)).
                            Select(x => new FileInfo(x).Length).Sum();
                    }
                        // Otherwise, check for partials
                    else if (partialInfo.Exists)
                        sz += partialInfo.Length;
                }

                return sz.Clamp(0);
            }

            private bool isOutdated(Repository repository)
            {
                bool thisIsDirectory = this.name.EndsWith("/");
                string path = repository.RootPath + "/" + this.name;

                SyncItem thisOld = repository.CurrentManifest.sync.Find(_f_old =>
                    _f_old.name.ToLowerInvariant() == this.name.ToLowerInvariant());

                if (thisOld == null)
                    return true;

                FileInfo fileInfo = new FileInfo(path);
                bool thisPathExists =
                    (thisIsDirectory && new DirectoryInfo(path).Exists) ||
                    (!thisIsDirectory && fileInfo.Exists);

                // Special handling for delete syncitems: any existing item is outdated because
                // we need to delete it.
                if (this.type == "delete")
                    if (this.ignoreExisting.GetValueOrDefault())
                        return false;
                    else
                        return thisPathExists;

                // Local data doesn't even exist. outdated.
                if (!thisPathExists)
                    return true;

                // special sync item flag: ignore any existing data.
                if (this.ignoreExisting.GetValueOrDefault())
                    return false;

                // old manifest item has a revision, we have a revision, and it's a mismatch
                if (thisOld.revision > 0 && this.revision > 0 && this.revision != thisOld.revision)
                    return true;

                // Timestamp mismatch - we're too old (or too young)
                if (Math.Abs((fileInfo.LastWriteTime - this.mtime).TotalSeconds) > 1)
                    return true;

                // We can't possibly have enough data, size mismatch.
                if (SizeOnDisk(repository) < this.size)
                    return true;

                if (thisIsDirectory)
                {
                    var directoryElements = Utils.GetDirectoryElements(path);

                    // Not enough files.
                    if (directoryElements.Count() < this.count)
                        return true;
                }

                return false;
            }
        }
    }
}
