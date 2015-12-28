using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Catflap
{
    public static class ExtensionMethods
    {
        public static T Clamp<T>(this T val, T min) where T : IComparable<T>
        {
            if (val.CompareTo(min) < 0) return min;
            else return val;
        }


        public static T Clamp<T>(this T val, T min, T max) where T : IComparable<T>
        {
            if (val.CompareTo(min) < 0) return min;
            else if (val.CompareTo(max) > 0) return max;
            else return val;
        }

        public static string NormalizePath(this string path)
        {
            string result = Path.GetFullPath(path.Replace("/", "\\")).ToLowerInvariant();
            result = result.TrimEnd('\\');
            return result;
        }

        public static string PathEllipsis(this string rawString, int maxLength = 30, char delimiter = '/')
        {
            maxLength -= 3; //account for delimiter spacing

            if (rawString.Length <= maxLength)
            {
                return rawString;
            }

            string final = rawString;
            List<string> parts;

            int loops = 0;
            while (loops++ < 100)
            {
                parts = rawString.Split(delimiter).ToList();
                parts.RemoveRange(parts.Count - 1 - loops, loops);
                if (parts.Count == 1)
                {
                    return parts.Last();
                }

                parts.Insert(parts.Count - 1, "...");
                final = string.Join(delimiter.ToString(), parts);
                if (final.Length < maxLength)
                {
                    return final;
                }
            }

            return rawString.Split(delimiter).ToList().Last();
        }

        public static string PrettyInterval(this DateTime d)
        {
            TimeSpan s = DateTime.Now.Subtract(d);
            // Don't allow future dates.
            int dayDiff = (int)s.TotalDays.Clamp(0);
            int secDiff = (int)s.TotalSeconds.Clamp(0);

            if (dayDiff == 0)
            {
                if (secDiff < 60)
                {
                    return "just now";
                }
                else if (secDiff < 120)
                {
                    return "1 minute ago";
                }
                else if (secDiff < 3600)
                {
                    return string.Format("{0} minutes ago",
                        Math.Floor((double)secDiff / 60));
                }
                else if (secDiff < 7200)
                {
                    return "1 hour ago";
                }
                else if (secDiff < 86400)
                {
                    return string.Format("{0} hours ago",
                        Math.Floor((double)secDiff / 3600));
                }
            }

            if (dayDiff == 1)
            {
                return "yesterday";
            }
            else if (dayDiff < 7)
            {
                return string.Format("{0} days ago",
                dayDiff);
            }
            else
            {
                return string.Format("{0} weeks ago",
                Math.Ceiling((double)dayDiff / 7));
            }
        }

        public static string BytesToHuman(this long bytes)
        {
            if (bytes >= 0x4000000000000)
                return string.Format("{0:F2} PB", (float)bytes / 0x4000000000000);
            if (bytes >= 0x10000000000)
                return string.Format("{0:F2} TB", (float)bytes / 0x10000000000);
            if (bytes >= 0x40000000)
                return string.Format("{0:F2} GB", (float)bytes / 0x40000000);
            if (bytes >= 0x100000)
                return string.Format("{0:F2} MB", (float)bytes / 0x100000);
            if (bytes >= 0x400)
                return string.Format("{0:F2} KB", (float)bytes / 0x400);
            else
                return bytes + " B";
        }

        public static string ShellEscape(this string str)
        {
            return str.Replace("\\", "\\\\").TrimEnd('\\');
        }

        public static long SizeOnDisk(this DirectoryInfo dir)
        {
            return dir.GetFiles().Sum(fi => fi.Length) +
                   dir.GetDirectories().Sum(di => SizeOnDisk(di));
        }
    }
}
