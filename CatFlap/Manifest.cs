using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;

namespace Catflap
{
    public class Manifest
    {
        public const int VERSION = 5;

        public static JsonSchema Schema = JsonSchema.Parse(@"{
	'$schema': 'http://json-schema.org/draft-04/schema#',
	'description': 'Catflap Manifest',
	'type': 'object',
	'properties': {
        'locked': { 'type': 'string', 'required': false },
        'version':  { 'type': 'integer', 'required': true, 'minimum': 1 },
        'title': { 'type': 'string', 'required': false },
        'baseUrl': { 'type': 'string', 'required': true },
        'rsyncUrl': { 'type': 'string', 'required': true },
        'revision': { 'type': 'string', 'required': false },
        'textColor': { 'type': 'string', 'required': false },

        'warnWhenSetupWithUntracked': { 'type': 'boolean', 'required': false },

        'fuzzy': { 'type': 'boolean', 'required': false },
        'ignoreCase': { 'type': 'boolean', 'required': false },
        'ignoreExisting': { 'type': 'boolean', 'required': false },
        'ignoreNewer': { 'type': 'boolean', 'required': false },
        'additionalArguments': { 'type': 'string', 'required': false },
        'purge': { 'type': 'boolean', 'required': false },

        'runAction': { 'type': 'object', 'required': false },

        'sync': { 'type': 'array', 'required': true, 'items':  {
                'type': 'object',
                'properties': {
                    'name': { 'type': 'string', 'required': true },
                    'type': { 'type': 'string', 'required': false },
                    'mode': { 'type': 'string', 'required': false },
                    'size': { 'type': 'integer', 'required': false, 'minimum': 0 },
                    'count': { 'type': 'integer', 'required': false, 'minimum': 0 },
                    'purge': { 'type': 'boolean', 'required': false },
                    'mtime': { 'type': 'string', 'required': false },
                    'fuzzy': { 'type': 'boolean', 'required': false },
                    'ignoreCase': { 'type': 'boolean', 'required': false },
                    'ignoreExisting': { 'type': 'boolean', 'required': false },
                    'ignoreNewer': { 'type': 'boolean', 'required': false },
                    'additionalArguments': { 'type': 'string', 'required': false },
                }
            }
        },
    },
}");
        // Repositories can be locked so that clients will be denied with a
        // appropriate message. Set to "" to unlock.
        public string locked = "";

        // The manifest file version. Do not touch.
        public int version;

        // Optional title, which will be displayed in the title bar.
        public string title;

        // The base URL where catflap.json can be found.
        public string baseUrl;

        // The rsync URL where all sync data can be found.
        // Supports a variable %user% for the current user, if any.
        public string rsyncUrl;

        // Setting a background: have a file named "catflap.bgimg" in the repository root, directly
        // under baseUrl. Make sure the webserver observes If-Modified-Since or clients will re-
        // download it on each check!
        // Supported file formats: jpg, png, gif, animated-gif, and everything else wpf-Image does.
        // This fills the white background completely, so the recommended image size is exactly
        // 400x470 px.
        // Leave empty to use the default background image.

        // Likewise, you can set a application icon that will be used for shortcuts and the taskbar
        // by having a favicon.ico on your webserver.

        // Text color in hexadecimal notation (for example, "#ffee33"). Can be used to adjust
        // to background images where black does not work.
        public string textColor;

        // A optional revision string, which will be printed to the log, but has no bearing on syncing.
        // Useful for debugging or informational displays on clients.
        public string revision;

        // Warn the user if he's doing setup in a directory that contains data not tracked by this repository.
        public Boolean warnWhenSetupWithUntracked = false;

        // This sets a default for "fuzzy" on all sync items, unless otherwise given in each item.
        public bool? fuzzy;

        // This sets a default for "ignoreCase" on all sync items, unless otherwise given in each item.
        public bool? ignoreCase;

        // This sets a default for "ignoreExisting" on all sync items, unless otherwise given in each item.
        public bool? ignoreExisting;

        // This sets a default for "ignoreNewer" on all sync items, unless otherwise given in each item.
        public bool? ignoreNewer;

        // Addional raw arguments to pass into rsync. Be very, very careful! This "stacks" with
        // additionalArguments of each sync item.
        public string additionalArguments;

        // This sets a default for "purge" on all sync items, unless otherwise given in each item.
        public bool? purge;

        public List<SyncItem> sync;

        public class SyncItem
        {
            // The filename or dirname of the sync item in question.
            // Make sure that directories ALWAYS terminate with a /.
            public string name;

            // The sync type. Currently supported:
            // * "rsync": Download the given item via rsync (default).
            // * "delete": Delete the given directory or file from the local repository.
            public string type = "rsync";

            // Uncompressed size in bytes
            public long   size;

            // Dir only: Number of files/directories in directory.
            public long   count;
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

            // Makes the updater skip syncing this file or directory if it
            // exists on the client and is of newer last modified time.
            public bool? ignoreNewer;

            // Addional raw arguments to pass into rsync. Be very, very careful!
            public string additionalArguments;

            // Do fuzzy-matching on the target. (--fuzzy)
            public bool? fuzzy;

            // Ignore case when updating/copying files (--ignore-case).
            // Note: This needs a special patch on the server!
            public bool? ignoreCase;
        }

        public ManifestAction runAction;

        public class ManifestAction
        {
            // The displayed string on the button.
            public string name;
            // The path to a binary file to execute.
            // Supports substituting variables as described below.
            public string execute;
            // Argument string passed to binary.
            // Supports substituting variables as described below.
            public string arguments;

            // Set to true to allow client arguments being passed/appended
            // to the command line for run action.
            public bool passArguments;

            // Allow pressing the run button even if the local repository is not up to date.
            public bool allowOutdated;
        }


        /*
         * Substitutable variables:
         * 
         * %root%     - the root directory (e.g. where catflap.exe is located)
         * %app%      - the app directory, where catflap stores it's internal data (e.g. catflap.exe.catflap)
         * %user%     - the stored user credential, if any
         */
    }
}
