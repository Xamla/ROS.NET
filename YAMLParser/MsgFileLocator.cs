using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FauxMessages;

namespace YAMLParser
{
    public class MsgFileLocation
    {
        // Unicode categories according to https://stackoverflow.com/a/950651/4036588
        private static readonly UnicodeCategory[] validFirstChars =
        {
            UnicodeCategory.UppercaseLetter,    // Lu
            UnicodeCategory.LowercaseLetter,    // Ll
            UnicodeCategory.TitlecaseLetter,    // Lt
            UnicodeCategory.ModifierLetter,     // Lm
            UnicodeCategory.OtherLetter         // Lo
            // underscore is also permitted for the first char
        };
        private static readonly UnicodeCategory[] otherValidChars =
        {
            UnicodeCategory.LetterNumber,           // Nl
            UnicodeCategory.NonSpacingMark,         // Mn
            UnicodeCategory.SpacingCombiningMark,   // Mc
            UnicodeCategory.DecimalDigitNumber,     // Nd
            UnicodeCategory.ConnectorPunctuation,   // Pc
            UnicodeCategory.Format                  // Cf
        };
        private static readonly UnicodeCategory[] validCSharpChars = validFirstChars.Union(otherValidChars).ToArray();

        private static string[] MSG_GEN_FOLDER_NAMES =
        {
            "msg",
            "srv",
            "msgs",
            "srvs",
            "action"
        };

        /// <summary>
        /// Mangles a file's name to find the package name based on the name of the directory containing the file
        /// </summary>
        /// <param name="path">A file</param>
        /// <param name="targetmsgpath">The "package name"/"msg name" for the file at path</param>
        /// <returns>"package name"</returns>
        private static string getPackageName(string path)
        {
            string[] chunks = path.Split(System.IO.Path.DirectorySeparatorChar);
            string foldername = chunks[chunks.Length - 2];
            if (MSG_GEN_FOLDER_NAMES.Contains(foldername))
                foldername = chunks[chunks.Length - 3];

            if (!IsValidCSharpIdentifier(foldername))
                throw new ArgumentException(String.Format("'{0}' from '{1}' is not a compatible C# identifier name\n\tThe package name must conform to C# Language Specifications (refer to this StackOverflow answer: https://stackoverflow.com/a/950651/4036588)\n", foldername, path));
            
            return foldername;
        }

        private static string getPackagePath(string basedir, string msgpath)
        {
            string p = getPackageName(msgpath);
            return System.IO.Path.Combine(basedir, p);
        }

        public string path { get; private set; }
        public string basename { get; private set; }
        public string extension { get; private set; }
        public string package { get; private set; }
        public string packagedir { get; private set; }
        public string searchroot { get; private set; }

        public string Path
        {
            get { return path; }
        }

        public MsgFileLocation(string path, string root)
        {
            this.path = path;
            searchroot = root;
            packagedir = getPackagePath(root, path);
            package = getPackageName(path);
            basename = System.IO.Path.GetFileNameWithoutExtension(path);
            extension = System.IO.Path.GetExtension(path).TrimStart('.');
        }

        public override bool Equals(object obj)
        {
            MsgFileLocation other = obj as MsgFileLocation;
            return other != null && string.Equals(other.package, package) && string.Equals(other.basename, basename);
        }

        public override int GetHashCode()
        {
            return (package + "/" + basename).GetHashCode();
        }

        public override string  ToString()
        {
            return string.Format("{0}.{1}", System.IO.Path.Combine(package, basename), extension);
        }

        public static bool IsValidCSharpIdentifier(string toTest)
        {
            if (toTest.Length == 0) // obviously..?
                return false;

            if (SingleType.IsCSharpKeyword(toTest)) // best to avoid any complications
                return false;

            char[] letters = toTest.ToCharArray();
            char first = letters[0];

            if (first != '_' && !validFirstChars.Contains(CharUnicodeInfo.GetUnicodeCategory(first)))
                return false;

            foreach (char c in letters)
                if (!validCSharpChars.Contains(CharUnicodeInfo.GetUnicodeCategory(c)))
                    return false;

            return true;

            // TODO: fix this regex method, replace the above method with it, 
            // and get rid of the three UnicodeCategory arrays
            // (regex method found at https://stackoverflow.com/a/1904462/4036588)

            //const string start = @"(\p{Lu}|\p{Ll}|\p{Lt}|\p{Lm}|\p{Lo}|\p{Nl})";
            //const string extend = @"(\p{Mn}|\p{Mc}|\p{Nd}|\p{Pc}|\p{Cf})";
            //Regex ident = new Regex(string.Format("{0}({0}|{1})*", start, extend));
            //toTest = toTest.Normalize();
            //return ident.IsMatch(toTest);
        }
    }

    internal static class MsgFileLocator
    {
        /// <summary>
        /// Finds all msgs and srvs below path and adds them to
        /// </summary>
        /// <param name="m"></param>
        /// <param name="s"></param>
        /// <param name="path"></param>
        private static void explode(List<MsgFileLocation> m, List<MsgFileLocation> s, List<MsgFileLocation> actionFiles, string path)
        {
            var msgfiles = Directory.GetFiles(path, "*.msg", SearchOption.AllDirectories).Select(p => new MsgFileLocation(p, path)).ToArray();
            var srvfiles = Directory.GetFiles(path, "*.srv", SearchOption.AllDirectories).Select(p => new MsgFileLocation(p, path)).ToArray();
            var allActionFiles = Directory.GetFiles(path, "*.action", SearchOption.AllDirectories).Select(p => new MsgFileLocation(p, path)).ToArray();

            int mb4 = m.Count, sb4=s.Count;
            foreach (var nm in msgfiles)
            {
                if (!m.Contains(nm))
                    m.Add(nm);
            }
            foreach (var ns in srvfiles)
            {
                if (!s.Contains(ns))
                    s.Add(ns);
            }
            foreach (var actionFile in allActionFiles)
            {
                if (!actionFiles.Contains(actionFile))
                {
                    actionFiles.Add(actionFile);
                }
            }
            Console.WriteLine("Skipped " + (msgfiles.Length - (m.Count - mb4)) + " duplicate msgs and " + (srvfiles.Length - (s.Count - sb4)) + " duplicate srvs");
        }

        internal static int priority(string package)
        {
            switch (package)
            {
                case "std_msgs": return 1;
                case "geometry_msgs": return 2;
                case "actionlib_msgs": return 3;
                default: return 9;
            }
        }

        internal static List<MsgFileLocation> sortMessages(List<MsgFileLocation> msgs)
        {
            return msgs.OrderBy(m => "" + priority(m.package) + m.package + m.basename).ToList();
        }

        public static void findMessages(List<MsgFileLocation> msgs, List<MsgFileLocation> srvs, List<MsgFileLocation> actionFiles,
            params string[] args)
        {
            //solution directory (where the reference to msg_gen is) is passed -- or assumed to be in a file in the same directory as the executable (which would be the case when msg_gen is directly run in the debugger
            if (args.Length == 0)
            {
                Console.WriteLine("MsgGen needs to receive a list of paths to recursively find messages in order to work.");
                Environment.Exit(1);
            }
            foreach (string arg in args)
            {
                explode(msgs, srvs, actionFiles, new DirectoryInfo(arg).FullName);
            }
        }
    }
}
