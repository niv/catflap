using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Catflap
{
    class SignatureVerificationException : SecurityException { public SignatureVerificationException(string message) : base(message) { } };

    enum VerifyResponse
    {
        // Checks out!
        OK,
        // Fail.
        FAIL,
        // Public keys have changed.
        KEY_MISMATCH
    };

    class Security
    {
        private static Regex RegexKeyMismatch = new Regex(@"^Signature key id in (.+?) is ([A-F0-9]+)\s+but the key id in (.+?) is ([A-F0-9]+)");

        public static VerifyResponse VerifySignature(string appPath, string sigFile, string dataFile)
        {
            var keyring = Repository.TrustDBFile;
            var pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = appPath + "\\minisign.exe";
            pProcess.StartInfo.EnvironmentVariables.Add("CYGWIN", "nodosfilewarning");

            pProcess.StartInfo.Arguments = "-q -V " +
                " -p \"" + keyring.ShellEscape() + "\" " + 
                " -x \"" + sigFile.ShellEscape() + "\" " +
                " -m \"" + dataFile.ShellEscape() + "\" ";

            pProcess.StartInfo.CreateNoWindow = true;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.WorkingDirectory = appPath;
            // pProcess.StartInfo.RedirectStandardOutput = true;
            pProcess.StartInfo.RedirectStandardError = true;
            pProcess.Start();
            pProcess.WaitForExit();

            var all = pProcess.StandardError.ReadToEnd();

            Match mr;
            if ((mr = RegexKeyMismatch.Match(all)).Success)
            {
                var rxdataFile = mr.Groups[0].Value;
                var curKey = mr.Groups[1].Value;
                var rxsigFile = mr.Groups[2].Value;
                var newKey = mr.Groups[3].Value;

                return VerifyResponse.KEY_MISMATCH;
            }

            return pProcess.ExitCode == 0 ? VerifyResponse.OK : VerifyResponse.FAIL;
        }

        public static string HashMD5(String file)
        {
            using (FileStream fs = new FileStream(file, FileMode.Open))
            using (BufferedStream bs = new BufferedStream(fs))
            using (MD5 algo = MD5.Create())
            {
                byte[] hash = algo.ComputeHash(bs);
                StringBuilder formatted = new StringBuilder(2 * hash.Length);
                foreach (byte b in hash)
                {
                    formatted.AppendFormat("{0:X2}", b);
                }
                return formatted.ToString();
            }
        }
    }
}
