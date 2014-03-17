﻿using MahApps.Metro;
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
    public partial class LoginWindow : MetroWindow
    {
        private Repository repo;

        public LoginWindow(Repository r)
        {
            InitializeComponent();

            ThemeManager.ChangeTheme(this,
                new MahApps.Metro.Accent("Crimson",
                    new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Accents/Crimson.xaml")),
                Theme.Light);

            repo = r;
            txtUser.Text = r.Username;
            txtPasswd.Text = r.Password;
            txtUser.Focus();
        }

        private void btnGo_Click(object sender, RoutedEventArgs e)
        {
            repo.Authorize(txtUser.Text, txtPasswd.Text);
            this.DialogResult = true;
            this.Close();
        }        
    }
}
