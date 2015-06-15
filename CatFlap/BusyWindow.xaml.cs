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
    public partial class BusyWindow : MetroWindow
    {
        public BusyWindow()
        {
            InitializeComponent();

            KeyDown += new KeyEventHandler((object sender, KeyEventArgs e) =>
            {
                if (e.Key == Key.System && e.SystemKey == Key.F4)
                    e.Handled = true;
            });
        }

        public static TResult WithBusyWindow<TResult>(Func<TResult> any)
        {
            return WithBusyWindow("Busy!", any);
        }

        public static void WithBusyWindow(string text, Action any)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var busy = new BusyWindow();
                busy.infotext.Text = text;
                try
                {
                    busy.Show();
                    any.Invoke();
                }
                finally
                {
                    busy.Close();
                }
            });
        }

        public static TResult WithBusyWindow<TResult>(string text, Func<TResult> any)
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                var busy = new BusyWindow();
                busy.infotext.Text = text;
                try
                {
                    busy.Show();
                    return any.Invoke();
                }
                finally
                {
                    busy.Close();
                }
            });
        }
    }
}
