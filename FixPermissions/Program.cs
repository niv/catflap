using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Windows;

namespace FixPermissions
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                MessageBox.Show("Please drop the directory you want to reset permissions for onto me.");
                return;
            }

            var dir = args[0];

            if (!Directory.Exists(dir))
            {
                MessageBox.Show("That's not a directory: " + dir);
                return;
            }

            DirectoryInfo di = new DirectoryInfo(dir);
            if (di.Parent == null)
            {
                MessageBox.Show("That's a root directory. U mad bro?");
                return;
            }


            if (!Utils.IsUserAdministrator())
            {
                MessageBox.Show("I need Administator privileges, but " +
                    "I can't request those myself.\n\nTry 'Run as administrator' with a shortcut.");
                return;
            }

            if (MessageBox.Show(
                    "This resets all permissions on the given directory\n\n"
                    + dir + 
                    "\n\nto the currently logged in user, " +
                    "removes all custom object ACLs and only allows inherited access entries " +
                    "(those of the parent directory '" + Directory.GetParent(dir) + "') to apply.\n\n" +
                    "!! This is a destructive operation and cannot be undone.\n" +
                    "!! This will not touch any files outside of the given directory.\n\n" +
                    "Continue?",
                    "Force-reset permissions on this directory?",
                    MessageBoxButtons.OKCancel) == DialogResult.OK)
            {
                Utils.FixPermissions(dir);
                MessageBox.Show("All done, exiting.");
            }
        }
    }
}
