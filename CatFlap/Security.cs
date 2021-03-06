﻿using System;
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
    public class Security
    {
        public struct VerifyResponse
        {
            public enum VerifyResponseStatus
            {
                NOT_CHECKED,

                // Signature Checks out!
                OK,

                // Fail.
                SIGNATURE_DOES_NOT_VERIFY,

                // Public keys have changed.
                PUBKEY_MISMATCH,

                // File wasn't signed.
                // This could mean that the signature disappeared!
                NO_LOCAL_SIGNATURE,

                // We have no (local) key.
                // File was signed but we have no keyring
                NO_LOCAL_PUBKEY,

                // Signature was outdated (i.e. replay attack)
                SIGNATURE_OUTDATED,
            };

            public VerifyResponseStatus Status;
            public string signingKey;
            public string trustedComment;
        };
        
        
        private string AppPath;

        public Security(string appPath)
        {
            this.AppPath = appPath;
        }


        private static Regex RegexKeyMismatch = new Regex(@"^Signature key id in (.+?) is ([A-F0-9]+)\s+but the key id in (.+?) is ([A-F0-9]+)");
        private static Regex RegexVerified = new Regex(@"^Signature and comment signature verified");

        public VerifyResponse VerifySignature(string sigFile, string dataFile)
        {
            VerifyResponse ret = new VerifyResponse();

            var keyring = Repository.TrustDBFile;

            if (!File.Exists(AppPath + "/" + keyring))
            {
                ret.Status = VerifyResponse.VerifyResponseStatus.NO_LOCAL_PUBKEY;
                Logger.Info("VerifySignature(\"" + dataFile + "\") = " + ret.Status);
                return ret;
            }

            if (!File.Exists(AppPath + "/" + sigFile) || !File.Exists(AppPath + "/" + dataFile))
            {
                ret.Status = VerifyResponse.VerifyResponseStatus.NO_LOCAL_SIGNATURE;
                Logger.Info("VerifySignature(\"" + dataFile + "\") = " + ret.Status);
                return ret;
            }
                
            var pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = AppPath + "\\bin\\minisign.exe";
            pProcess.StartInfo.EnvironmentVariables.Add("CYGWIN", "nodosfilewarning");

            pProcess.StartInfo.Arguments = "-Q -V " +
                " -p \"" + keyring.ShellEscape() + "\" " + 
                " -x \"" + sigFile.ShellEscape() + "\" " +
                " -m \"" + dataFile.ShellEscape() + "\" ";

            pProcess.StartInfo.CreateNoWindow = true;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.WorkingDirectory = AppPath;
            pProcess.StartInfo.RedirectStandardOutput = true;
            pProcess.StartInfo.RedirectStandardError = true;
            pProcess.Start();
            pProcess.WaitForExit();

            var stdout = pProcess.StandardOutput.ReadToEnd();
            var stderr = pProcess.StandardError.ReadToEnd();
            
            if (pProcess.ExitCode == 0)
            {
                ret.Status = VerifyResponse.VerifyResponseStatus.OK;
                ret.trustedComment = stdout.Trim();

            } else {
                Match mr;
                if ((mr = RegexKeyMismatch.Match(stderr)).Success)
                {
                    /*var rxdataFile = mr.Groups[0].Value;
                    var curKey = mr.Groups[1].Value;
                    var rxsigFile = mr.Groups[2].Value;
                    var newKey = mr.Groups[3].Value;*/
                    ret.Status = VerifyResponse.VerifyResponseStatus.PUBKEY_MISMATCH;

                } else
                ret.Status = VerifyResponse.VerifyResponseStatus.SIGNATURE_DOES_NOT_VERIFY;
            }

            byte[] keyringData = Convert.FromBase64String(System.IO.File.ReadAllLines(AppPath + "/" + keyring)[1]);
            keyringData = keyringData.Skip(2).Take(8).Reverse().ToArray();
            ret.signingKey = BitConverter.ToString(keyringData).ToUpperInvariant();

            Logger.Info("VerifySignature(\"" + dataFile + "\") = " + ret.Status +
                ", key: " + ret.signingKey +
                ", comment: " + ret.trustedComment);

            return ret;
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
