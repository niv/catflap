using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;

namespace Catflap
{
    public class Utils
    {
        public static FileInfo[] GetDirectoryElements(string parentDirectory)
        {
            // This throws IOException when directories are being locked by catflap
            // (like partial dirs, or delayed updates).
            try
            {
                if (Directory.Exists(parentDirectory))
                    return new DirectoryInfo(parentDirectory).GetFiles("*", SearchOption.AllDirectories);
                else if (File.Exists(parentDirectory))
                    return new FileInfo[] { new FileInfo(parentDirectory) };
                else
                    return new FileInfo[] { };
            }
            catch (IOException)
            {
                return new FileInfo[] { };
            }
        }

        public static void FixPermissions(string obj, bool recurse = true)
        {
            if (!Directory.Exists(obj))
                return;

            DirectoryInfo dInfo = new DirectoryInfo(obj);
            DirectorySecurity dSecurity = dInfo.GetAccessControl();
            ReplaceAllDescendantPermissionsFromObject(dInfo, dSecurity, recurse);
        }

        private static void ReplaceAllDescendantPermissionsFromObject(
            DirectoryInfo dInfo, DirectorySecurity dSecurity, bool recurse)
        {
            dInfo.SetAccessControl(dSecurity);

            RemoveCustomACLs(dInfo.FullName);

            foreach (FileInfo fi in dInfo.GetFiles())
            {
                RemoveCustomACLs(fi.FullName);
            }

            if (recurse)
                dInfo.GetDirectories().ToList()
                    .ForEach(d => {
                        RemoveCustomACLs(d.FullName);
                        ReplaceAllDescendantPermissionsFromObject(d, dSecurity, recurse);
                    });
        }

        // http://stackoverflow.com/questions/12811850/setting-a-files-acl-to-be-inherited
        private static void RemoveCustomACLs(string destination)
        {
            FileInfo fileInfo;
            FileSecurity fileSecurity;
            AuthorizationRuleCollection fileRules;

            fileInfo = new FileInfo(destination);
            fileSecurity = fileInfo.GetAccessControl();
            fileSecurity.SetAccessRuleProtection(false, false);

            fileSecurity.SetOwner(WindowsIdentity.GetCurrent().User);

            /*
             * Only fetch the explicit rules since I want to keep the inherited ones. Not 
             * sure if the target type matters in this case since I am not examining the
             * IdentityReference.
             */
            fileRules = fileSecurity.GetAccessRules(includeExplicit: true,
                                     includeInherited: false, targetType: typeof(NTAccount));
            /*
             * fileRules is a AuthorizationRuleCollection object, which can contain objects 
             * other than FileSystemAccessRule (in theory), but GetAccessRules should only 
             * ever return a collection of FileSystemAccessRules, so we will just declare 
             * rule explicitly as a FileSystemAccessRule.
             */
            foreach (FileSystemAccessRule rule in fileRules)
            {
                /*
                 * Remove any explicit permissions so we are just left with inherited ones.
                 */
                fileSecurity.RemoveAccessRule(rule);
            }

            fileInfo.SetAccessControl(fileSecurity);
        }

        public static bool IsUserAdministrator()
        {
            bool isAdmin;
            try
            {
                WindowsIdentity user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (UnauthorizedAccessException)
            {
                isAdmin = false;
            }
            catch (Exception)
            {
                isAdmin = false;
            }
            return isAdmin;
        }
    }
}
