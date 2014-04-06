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
        'version':  { 'type': 'integer', 'required': true, 'minimum': 1 },
        'title': { 'type': 'string', 'required': false },
        'baseUrl': { 'type': 'string', 'required': true },
        'rsyncUrl': { 'type': 'string', 'required': true },
        'revision': { 'type': 'string', 'required': false },
        'textColor': { 'type': 'string', 'required': false },

        'warnWhenSetupWithUntracked': { 'type': 'boolean', 'required': false },

        'fuzzy': { 'type': 'boolean', 'required': false },
        'ignoreCase': { 'type': 'boolean', 'required': false },

        'runActionAllowOutdated': { 'type': 'boolean', 'required': false },
        'runAction': { 'type': 'object', 'required': false },

        'sync': { 'type': 'array', 'required': true, 'items':  {
                'type': 'object',
                'properties': {
                    'name': { 'type': 'string', 'required': true },
                    'type': { 'type': 'string', 'required': false },
                    'mode': { 'type': 'string', 'required': false },
                    'size': { 'type': 'integer', 'required': true, 'minimum': 0 },
                    'count': { 'type': 'integer', 'required': false, 'minimum': 0 },
                    'purge': { 'type': 'boolean', 'required': false },
                    'mtime': { 'type': 'string', 'required': true },
                    'fuzzy': { 'type': 'boolean', 'required': false },
                    'ignoreCase': { 'type': 'boolean', 'required': false },
                    'ignoreExisting': { 'type': 'boolean', 'required': false }
                }
            }
        },
    },
}");

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

        // Same as setting fuzzy for all sync items
        public bool fuzzy;

        // Same as setting ignoreCase for all sync items
        public bool ignoreCase;

        public List<ManifestSyncItem> sync;

        public class ManifestSyncItem
        {
            public string name;
            public string type;
            // Uncompressed size in bytes
            public long   size;

            // Dir only: Number of files/directories in directory.
            public long   count;
            // Dir only: Pass --delete to rsync when syncing directory/ items.
            // Will remove all untracked files that are not present
            // in the repository.
            public bool purge;

            // Transfer mode. one of 'inplace' (default), 'replace'
            // Note that 'replace' may leave stray temp files if the user
            // cancels in just the wrong moment, so using it for big files
            // is not recommended - it's merely here to allow updating
            // running binaries or locked files.
            public string mode;

            [JsonProperty(ItemConverterType = typeof(IsoDateTimeConverter))]
            public DateTime mtime;

            // Makes the updater skip syncing this file or directory if it
            // exists on the client. Can be used for initial config-file syncs.
            public bool  ignoreExisting;

            // Do fuzzy-matching on the target. (--fuzzy)
            public bool fuzzy;

            // Ignore case when updating/copying files (--ignore-case).
            // Note: This needs a special patch on the server!
            public bool ignoreCase;
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
        }

        // Allow pressing the run button even if the local repository is not up to date.
        public bool runActionAllowOutdated;

        /*
         * Substitutable variables:
         * 
         * %root%     - the root directory (e.g. where catflap.exe is located)
         * %app%      - the app directory, where catflap stores it's internal data (e.g. catflap.exe.catflap)
         * %user%     - the stored user credential, if any
         */
    }
}
