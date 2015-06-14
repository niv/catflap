using MahApps.Metro;
using MahApps.Metro.Controls;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FORMS = System.Windows.Forms;

namespace Catflap
{
    public partial class TrustDBWindow : MetroWindow
    {
        private Repository repository;

        public TrustDBWindow(Repository r)
        {
            InitializeComponent();

            ThemeManager.ChangeAppStyle(Application.Current,
                MainWindow.accentOK,
                ThemeManager.AppThemes.First(x => x.Name == "BaseLight"));

            repository = r;
        }

        private void btnGo_Click(object sender, RoutedEventArgs e)
        {
            repository.SaveTrustDB(this.publicKey.Text);
            
            repository.VerifyManifest();
            switch (repository.ManifestSecurityStatus.Status)
            {
                case Security.VerifyResponse.VerifyResponseStatus.OK:
                    this.DialogResult = true;
                    this.Close();
                    return;

                default:
                    repository.ResetTrustDB();
                    MessageBox.Show("Could not verify signature. (Internal status: " +
                        repository.ManifestSecurityStatus.Status.ToString() + ")");
                    return;
            }
        }

        private void publicKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            var pubStr = this.publicKey.Text.Trim();
            if (pubStr == "")
            {
                btnGo.IsEnabled = false;
                btnGo.Content = "enter key";
                return;
            }
            try
            {
                byte[] bin = Convert.FromBase64String(pubStr);
                if (bin[0] != 'E' || bin[1] != 'd' || bin.Length < 10)
                    throw new Exception("Not a Ed curve key");
            }
            catch (Exception)
            {
                btnGo.IsEnabled = false;
                btnGo.Content = "invalid public key";
                return;
            }

            btnGo.IsEnabled = true;
            btnGo.Content = "save!";
        }
    }
}
