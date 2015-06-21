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
        private static string[] resourcesToUnpack =
        {
            "rsync.exe.gz" , "cygwin1.dll.gz",  "cyggcc_s-1.dll.gz", "kill.exe.gz",
            // minisign
            "minisign.exe.gz"
        };
        private static string[] resourcesToPurge =
        {
            "cygpopt-0.dll",
            // gpgv
            "gpgv.exe.gz", "cygz.dll.gz", "cygintl-8.dll.gz", "cygiconv-2.dll.gz", "cygbz2-1.dll.gz"
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
                var x = toPath + "\\" + src;
                if (File.Exists(x))
                {
                    Logger.Info("Deleting obsolete bundled file: " + src);
                    File.Delete(x);
                }
            }

            foreach (string src in resourcesToUnpack)
            {
                var dst = src;
                if (dst.EndsWith(".gz"))
                    dst = dst.Substring(0, dst.Length - 3);

                if (!File.Exists(toPath + "\\" + dst) || File.GetLastWriteTime(toPath + "\\" + dst) != File.GetLastWriteTime(fi.FullName))
                {
                    Logger.Info("Extracting bundled file: " + src);
                    ExtractResource(src, toPath + "\\" + dst);
                    File.SetLastWriteTime(toPath + "\\" + dst, File.GetLastWriteTime(fi.FullName));
                }
            }
        }
    }
}
