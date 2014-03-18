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
        public const int VERSION = 4;

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
        'infoPaneUrl': { 'type': 'string', 'required': false },
        'textColor': { 'type': 'string', 'required': false },

        'runActionAllowOutdated': { 'type': 'boolean', 'required': false },
        'runAction': { 'type': 'object', 'required': false },

        'sync': { 'type': 'array', 'required': true, 'items':  {
                'type': 'object',
                'properties': {
                    'name': { 'type': 'string', 'required': true },
                    'type': { 'type': 'string', 'required': false },
                    'mode': { 'type': 'string', 'required': false },
                    'wildcard':  { 'type': 'boolean', 'required': false },
                    'size': { 'type': 'integer', 'required': true, 'minimum': 0 },
                    'csize': { 'type': 'integer', 'required': false, 'minimum': 0 },
                    'count': { 'type': 'integer', 'required': false, 'minimum': 0 },
                    'purge': { 'type': 'boolean', 'required': false },
                    'mtime': { 'type': 'string', 'required': true },
                    'ignoreExisting': { 'type': 'boolean', 'required': false }
                }
            }
        },
    },
}");

        public int version;

        // Optional title
        public string title;

        public string baseUrl;
        public string rsyncUrl;
        // The URL to load in the info pane. Optional. Will be hidden if empty to allow
        // showing a prettier full-size background image.
        public string infoPaneUrl;

        // Setting a background: have a file named "catflap.bgimg" in the repository root, directly
        // under baseUrl. Make sure the webserver observes If-Modified-Since or clients will re-
        // download it on each check!
        // Supported file formats: jpg, png, gif, animated-gif, and everything else wpf-Image does.
        // This fills the white background completely, so maximum/optimal size is 400x454 px.
        // Leave empty to not set a bg img.

        // Text Color in hexadecimal notation (for example, "#ffee33").
        public string textColor;

        public string revision;

        public ManifestAction runAction;
        public bool runActionAllowOutdated;
        public List<ManifestSyncItem> sync;

        public class ManifestSyncItem
        {
            public string name;
            public string type;
            // Uncompressed size in bytes
            public long   size;
            // Compressed size in bytes
            public long   csize;

            // Dir only: Number of files/directories in directory.
            public long   count;
            // Dir only: Pass --delete to rsync when syncing directory/ items.
            // Will remove all untracked files that are not present
            // in the repository.
            public bool purge;

            // Unused at the moment
            //public bool wildcard;

            // Transfer mode. one of 'inplace' (default), 'replace'
            // Note that 'replace' may leave stray temp files if the user
            // cancels in just the wrong moment, so using it for big files
            // is not recommended - it's merely here to allow updating
            // running binaries or locked files.
            public string mode;

            [JsonProperty(ItemConverterType = typeof(IsoDateTimeConverter))]
            public DateTime mtime;

            public bool  ignoreExisting;
        }

        public class ManifestAction
        {
            public string name;
            public string execute;
            public string arguments;
        }
    }
}
