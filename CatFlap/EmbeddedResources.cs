using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Catflap
{
    class EmbeddedResources
    {
        private static Dictionary<string, string> resourcesToUnpack = new Dictionary<string, string>();

        static EmbeddedResources()
        {
            resourcesToUnpack.Add("rsync.exe.gz", "bin");
            resourcesToUnpack.Add("cygwin1.dll.gz", "bin");
            resourcesToUnpack.Add("cyggcc_s-1.dll.gz", "bin");
            resourcesToUnpack.Add("kill.exe.gz", "bin");
            resourcesToUnpack.Add("cygz.dll.gz", "bin");
            resourcesToUnpack.Add("minisign.exe.gz", "bin");

            resourcesToUnpack.Add("fstab", "etc");
        }

        private static string[] resourcesToPurge =
        {
            "cygpopt-0.dll", "gpgv.exe", "cygiconv-2.dll", "cygintl-8.dll", "cygbz2-1.dll",
            "rsync.exe", "cygwin1.dll",  "cyggcc_s-1.dll", "kill.exe", "cygz.dll", "minisign.exe"
        };

        private static void ExtractResource(string resource, string destination)
        {
            Stream stream = Application.Current.GetType().Assembly.GetManifestResourceStream("Catflap.Resources." + resource);
            if (resource.EndsWith(".gz"))
                stream = new GZipStream(stream, CompressionMode.Decompress);

            var dest = File.Create(destination);
            stream.CopyTo(dest);
            dest.Close();
        }

        public static void Update(string toPath)
        {
            var fi = new FileInfo(Assembly.GetExecutingAssembly().Location);

            foreach (string src in resourcesToPurge)
            {
                var x = toPath + Path.DirectorySeparatorChar + src;
                if (File.Exists(x))
                {
                    Logger.Info("Deleting obsolete bundled file: " + src);
                    File.Delete(x);
                }
            }

            foreach (KeyValuePair<string, string> src in resourcesToUnpack)
            {
                var packedFn = src.Key;
                var dstDir = src.Value;

                var dstFn = packedFn;
                if (dstFn.EndsWith(".gz"))
                    dstFn = dstFn.Substring(0, dstFn.Length - 3);

                var dstPath = toPath + Path.DirectorySeparatorChar + dstDir;
                var dstPathWithFn = dstPath + Path.DirectorySeparatorChar + dstFn;

                if (!File.Exists(dstPathWithFn) || File.GetLastWriteTime(dstPathWithFn) != File.GetLastWriteTime(fi.FullName))
                {
                    Logger.Info("Extracting bundled file: " + src);
                    Directory.CreateDirectory(dstPath);
                    ExtractResource(packedFn, dstPathWithFn);
                    File.SetLastWriteTime(dstPathWithFn, File.GetLastWriteTime(fi.FullName));
                }
            }
        }
    }
}
