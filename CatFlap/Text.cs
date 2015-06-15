using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading.Tasks;

namespace Catflap
{
    public class Text
    {
        private static ResourceManager _rm = new ResourceManager(
            "catflap.Resources.Texts_English", Assembly.GetExecutingAssembly());

        public static string t(string key, params Object[] args)
        {
            var t = _rm.GetString(key);
            if (t == null || t == "")
                return "<missing translation: " + key + ">";

            t = t.Replace("\\n", "\n");
            return String.Format(t, args);
        }
    }
}
