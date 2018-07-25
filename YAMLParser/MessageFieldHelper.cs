using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using YAMLParser;

namespace FauxMessages
{
    public static class MessageFieldHelper
    {
        public static string Generate(SingleType members)
        {
            string mt = "xamla/unknown";
            string pt = KnownStuff.GetNamespacedType(members);
            if (members.meta)
            {
                string t = members.Type.Replace("Messages.", "");
                if (!t.Contains('.'))
                {
                    if (members.Definer != null)
                    {
                        t = members.Definer.Package + "." + t;
                    }
                    else
                    {
                        t = null;
                    }
                }
                if (t != null)
                    mt = t.Replace(".", "/");
                else
                    members.meta = false;
            }
            return String.Format
                ("\"{0}\", new MsgFieldInfo(\"{0}\", {1}, {2}, {3}, \"{4}\", {5}, \"{6}\", {7}, {8})",
                    members.Name.Replace("@", ""),
                    members.IsLiteral.ToString().ToLower(),
                    ("typeof(" + pt + ")"),
                    members.Static.ToString().ToLower(),
                    members.StaticValue.TrimStart('"').TrimEnd('"'),
                    //members.Type.Equals("string", StringComparison.InvariantCultureIgnoreCase) ? ("new String("+members.StaticValue+")") : ("\""+members.StaticValue+"\""),
                    members.IsArray.ToString().ToLower(),
                    members.length,
                    //FIX MEEEEEEEE
                    members.meta.ToString().ToLower(),
                    mt);
        }
        public static KeyValuePair<string, MsgFieldInfo> Instantiate(SingleType member)
        {
            string mt = "xamla/unknown";
            if (member.meta)
            {
                string t = member.Type.Replace("Messages.", "");
                if (!t.Contains('.'))
                    t = "std_msgs." + t;
                mt = t.Replace(".", "/");
            }
            return new KeyValuePair<string, MsgFieldInfo>(member.Name, new MsgFieldInfo(member.Name, member.IsLiteral, member.Type, member.Static, member.StaticValue, member.IsArray, member.length, member.meta));
        }

        public static Dictionary<string, MsgFieldInfo> Instantiate(IEnumerable<SingleType> stuff)
        {
            return stuff.Select(Instantiate).ToDictionary(field => field.Key, field => field.Value);
        }
    }
}
