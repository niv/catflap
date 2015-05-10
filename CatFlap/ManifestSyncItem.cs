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
            public bool isCurrent(string rootPath)
            {
                return !isOutdated(rootPath);
            }

            public long SizeOnDisk(string rootPath)
            {
                // All the data we have, we assume to be correct, so we just take the diff.

                if (this.name.EndsWith("/"))
                    return (this.size - Utils.GetDirectoryElements(rootPath + "/" + this.name).Sum(file => file.Length));

                else if (File.Exists(rootPath + "/" + this.name))
                    return new FileInfo(rootPath + "/" + this.name).Length;

                else
                    return 0;
            }

            private bool isOutdated(string rootPath)
            {
                if (this.name.EndsWith("/"))
                {
                    var directoryElements = Utils.GetDirectoryElements(rootPath + "/" + this.name);

                    // Always check dirs that ..
                    return (this.type == "rsync" && (
                        // Always check dirs that ..

                                // don't exist locally yet
                                !Directory.Exists(rootPath + "/" + this.name) ||

                                (!this.ignoreExisting.GetValueOrDefault() && (

                                    // are not young enough mtime
                                    Math.Abs((new FileInfo(rootPath + "/" + this.name).LastWriteTime - this.mtime).TotalSeconds) > 1 ||

                                    // have mismatching item count
                                    directoryElements.Count() < this.count ||

                                    // are not big enough
                                    directoryElements.Sum(file => file.Length) < this.size
                                ))
                            )) || (this.type == "delete" && (
                                Directory.Exists(rootPath + "/" + this.name)
                            ));
                }
                else
                {
                    return (this.type == "rsync" && (
                                !File.Exists(rootPath + "/" + this.name) ||

                                (!this.ignoreExisting.GetValueOrDefault() && (
                                    (this.mtime != null && Math.Abs((new FileInfo(rootPath + "/" + this.name).LastWriteTime - this.mtime).TotalSeconds) > 1) ||

                                    (new FileInfo(rootPath + "/" + this.name).Length != this.size)
                                ))
                            )) || (this.type == "delete" && (
                                File.Exists(rootPath + "/" + this.name)
                            ));
                }
            }
        }
    }
}
