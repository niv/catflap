using MahApps.Metro;
using MahApps.Metro.Controls;
using Newtonsoft.Json;
using Polly;
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
using MahApps.Metro.Controls.Dialogs;

namespace Catflap
{
    public partial class SetupWindow : MetroWindow
    {
        public SetupWindow()
        {
            InitializeComponent();

            ThemeManager.ChangeTheme(this,
                new MahApps.Metro.Accent("Steel",
                    new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Accents/Steel.xaml")),
                Theme.Light);

            ImageBrush myBrush = new ImageBrush();
            myBrush.Stretch = Stretch.Uniform;
            Image image = new Image();
            image.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/bgimg.png"));
            myBrush.ImageSource = image.Source;
            gridSetupWindow.Background = myBrush;

            txtUrl.Focus();
        }

        private void btnGo_Click(object sender, RoutedEventArgs e)
        {
            if (txtUrl.Text == null || txtUrl.Text.Trim() == "") 
                return;

            var url = txtUrl.Text.Trim().TrimEnd('/') + "/";

            if (!url.StartsWith("http://") || !!url.StartsWith("https://"))
                url = "http://" + url;

            var fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
            var rootPath = Directory.GetCurrentDirectory();
            var appPath = rootPath + "\\" + fi.Name + ".catflap";

            var repo = new Repository(url, rootPath, appPath);
            Manifest mf;

            try
            {
                mf = repo.AuthPolicy.Execute(() => repo.GetManifestFromRemote());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "This doesn't look like a valid repository");
                Console.WriteLine(ex.ToString());
                return;
            }

            if (mf.warnWhenSetupWithUntracked)
            {
                var currentContents =
                    Directory.GetFiles(rootPath).
                        Select(x => System.IO.Path.GetFileName(x)).
                    Concat(
                        Directory.GetDirectories(rootPath).
                        Select(x => System.IO.Path.GetFileName(x)).
                        Select(x => x + "/")
                    ).Select(x => x.ToLower());

                var skip = new string[] { fi.Name.ToLower(), fi.Name.ToLower() + ".catflap/" };

                var untracked = currentContents.Except(mf.sync.Select(x => x.name.ToLower())).Except(skip);
                
                if (untracked.Count() > 0)
                {
                    var ret = MessageBox.Show(
                        "THIS REPOSITORY RECOMMENDS TO START IN A EMPTY DIRECTORY.\n\n" +
                        "You are setting up in a directory that already contains data that is not tracked by this repository:\n\n" +
                        String.Join("\n", untracked.Take(10)) + "\n..\n\n" +
                        "Do you want to continue anyways?",
                        "Untracked files in directory, continue anyways?", MessageBoxButton.YesNo);
                    if (ret == MessageBoxResult.No)
                        return;
                }
            }

            Directory.CreateDirectory(appPath);
            System.IO.File.WriteAllText(appPath + "\\catflap.json", JsonConvert.SerializeObject(mf));

            this.DialogResult = true;
            this.Close();
        }

        private void txtUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtUrl.Text.StartsWith("http://", StringComparison.InvariantCulture))
            {
                txtUrl.Text = txtUrl.Text.Replace("http://", "");
                txtUrl.Select(txtUrl.Text.Length, txtUrl.Text.Length);
            }
                
            if (txtUrl.Text.StartsWith("https://", StringComparison.InvariantCulture))
            {
                txtUrl.Text = txtUrl.Text.Replace("https://", "");
                txtUrl.Select(txtUrl.Text.Length, txtUrl.Text.Length);
            }
        }
    }
}
