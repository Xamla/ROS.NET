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
    public class SingleType
    {
        // TODO extend check to other C# keywords
        private static readonly string[] CSharpKeywords = { "object", "params", "namespace", "const", "static" };
        
        private static bool IsCSharpKeyword(string name)
        {
            return CSharpKeywords.Contains(name);
        }

        public bool Static;
        public string StaticValue = "";
        public bool IsArray;
        public bool IsLiteral;
        public string Name;
        public string Package;
        public string Type;
        private string[] backup;
        public string input;
        public string length = "";
        public string lowestindent = "\t\t";
        public bool meta;
        public string output;
        public string rostype = "";
        public MsgFile Definer;

        public SingleType(string s)
            : this("", s, "")
        {
        }

        public SingleType(string package, string s, string extraindent)
        {
            this.Package = package;
            lowestindent += extraindent;
            if (s.Contains('[') && s.Contains(']'))
            {
                string front = "";
                string back = "";
                string[] parts = s.Split('[');
                front = parts[0];
                parts = parts[1].Split(']');
                length = parts[0];
                back = parts[1];
                IsArray = true;
                s = front + back;
            }
            input = s;
        }

        public bool IsPrimitve
        {
            get
            {
                if (!String.IsNullOrEmpty(Package))
                {
                    return false;
                }
                var typeName = input.Split(' ')[0];
                if (KnownStuff.KnownTypes.ContainsKey(typeName))
                {
                    return true;
                }
                return false;
            }
        }

        public static void Finalize(MsgFile parent, SingleType thing)
        {
            string[] parts = thing.input.Split(' ');
            thing.rostype = parts[0];
            if (!KnownStuff.KnownTypes.ContainsKey(thing.rostype))
            {
                thing.meta = true;
            } else
            {
                parts[0] = KnownStuff.KnownTypes[thing.rostype];
            }
            thing.Finalize(parent, parts, true);
        }

        public void Finalize(MsgFile parent, string[] s, bool isliteral)
        {
            backup = new string[s.Length];
            Array.Copy(s, backup, s.Length);
            bool isstatic = false;
            IsLiteral = isliteral;
            string type = s[0];
            string name = s[1];
            string otherstuff = "";
            if (name.Contains('='))
            {
                string[] parts = name.Split('=');
                isstatic = true;
                name = parts[0];
                otherstuff = " = " + parts[1];
            }

            if (IsCSharpKeyword(name))
            {
                name = "@" + name;
            }

            for (int i = 2; i < s.Length; i++)
                otherstuff += " " + s[i];

            if (otherstuff.Contains('='))
                isstatic = true;

            if (!IsArray)
            {
                if (otherstuff.Contains('=') && type.Equals("string", StringComparison.CurrentCultureIgnoreCase))
                {
                    otherstuff = otherstuff.Replace("\\", "\\\\");
                    otherstuff = otherstuff.Replace("\"", "\\\"");
                    string[] split = otherstuff.Split('=');
                    otherstuff = split[0] + " = " + split[1].Trim() + "";
                }
                if (otherstuff.Contains('=') && type == "bool")
                {
                    otherstuff = otherstuff.Replace("0", "false").Replace("1", "true");
                }
                if (otherstuff.Contains('=') && type == "byte")
                {
                    otherstuff = otherstuff.Replace("-1", "255");
                }
                Static = isstatic;
                bool wantsconstructor = true;
                if (otherstuff.Contains("="))
                {
                    string[] chunks = otherstuff.Split('=');
                    StaticValue = chunks[chunks.Length - 1].Trim();
                    if (type.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        otherstuff = chunks[0] + " = \"" + chunks[1].Trim() + "\"";
                        wantsconstructor = false;
                    }
                }
                string prefix = "", suffix = "";
                if (isstatic)
                {
                    if (!type.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        prefix = "static ";
                        wantsconstructor = false;
                    }
                }
                string t = KnownStuff.GetNamespacedType(this, type);
                if (otherstuff.Contains('='))
                {
                    if (wantsconstructor)
                    {
                        if (type == "string")
                            suffix = " = \"\"";
                        else
                            suffix = " = new " + type + "()";
                    }
                    else
                        suffix = KnownStuff.GetConstTypesAffix(type);
                }
                else{
                    if (type == "string")
                        suffix = " = \"\"";
                    else
                        suffix = " = new " + prefix + t + "()";
                }
                output = lowestindent + "public " + prefix + t + " " + name + otherstuff + suffix + ";";
            }
            else
            {
                if (length.Length > 0)
                    IsLiteral = true;
                if (otherstuff.Contains('='))
                {
                    string[] split = otherstuff.Split('=');
                    otherstuff = split[0] + " = (" + type + ")" + split[1];

                }
                string t = KnownStuff.GetNamespacedType(this, type);
                if (length.Length > 0)
                    output = lowestindent + "public " + t + "[] " + name + otherstuff + " = new " + type + "[" + length + "];";
                else
                    output = lowestindent + "public " + "" + t + "[] " + name + otherstuff + ";";
            }
            Type = type;
            parent.resolve(this);
            if (!KnownStuff.KnownTypes.ContainsKey(rostype))
            {
                meta = true;
            }
            Name = name.Length == 0 ? otherstuff.Trim() : name;
            if (Name.Contains('='))
            {
                Name = Name.Substring(0, Name.IndexOf("=")).Trim();
            }
        }

        public void refinalize(MsgFile parent, string REALTYPE)
        {
            bool isstatic = false;
            Type = REALTYPE;
            string name = backup[1];
            string otherstuff = "";
            if (name.Contains('='))
            {
                string[] parts = name.Split('=');
                isstatic = true;
                name = parts[0];
                otherstuff = " = " + parts[1];
            }
            if (IsCSharpKeyword(name))
            {
                name = "@" + name;
            }
            for (int i = 2; i < backup.Length; i++)
                otherstuff += " " + backup[i];
            if (otherstuff.Contains('='))
                isstatic = true;
            parent.resolve(this);
            if (!IsArray)
            {
                if (otherstuff.Contains('=') && Type.Equals("string", StringComparison.CurrentCultureIgnoreCase))
                {
                    otherstuff = otherstuff.Replace("\\", "\\\\");
                    otherstuff = otherstuff.Replace("\"", "\\\"");
                    string[] split = otherstuff.Split('=');
                    otherstuff = split[0] + " = \"" + split[1].Trim() + "\"";
                }
                if (otherstuff.Contains('=') && Type == "bool")
                {
                    otherstuff = otherstuff.Replace("0", "false").Replace("1", "true");
                }
                if (otherstuff.Contains('=') && Type == "byte")
                {
                    otherstuff = otherstuff.Replace("-1", "255");
                }
                Static = isstatic;
                bool wantsconstructor = false;
                if (otherstuff.Contains("="))
                {
                    string[] chunks = otherstuff.Split('=');
                    StaticValue = chunks[chunks.Length - 1].Trim();
                    if (Type.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        otherstuff = chunks[0] + " = \"" + chunks[1].Trim().Replace("\"", "") + "\"";
                    }
                }
                else if (!Type.Equals("String"))
                {
                    wantsconstructor = true;
                }
                string prefix = "", suffix = "";
                if (isstatic)
                {
                    if (!Type.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        prefix = "static ";
                    }
                }
                if (otherstuff.Contains('='))
                    if (wantsconstructor)
                        if (Type == "string")
                            suffix = " = \"\"";
                        else
                            suffix = " = new " + Type + "()";
                    else
                        suffix = KnownStuff.GetConstTypesAffix(Type);
                string t = KnownStuff.GetNamespacedType(this, Type);
                output = lowestindent + "public " + prefix + t + " " + name + otherstuff + suffix + ";";
            }
            else
            {
                if (length.Length != 0)
                    IsLiteral = true; //type != "string";
                if (otherstuff.Contains('='))
                {
                    string[] split = otherstuff.Split('=');
                    otherstuff = split[0] + " = (" + Type + ")" + split[1];
                }
                string t = KnownStuff.GetNamespacedType(this, Type);
                if (length.Length != 0)
                    output = lowestindent + "public " + t + "[] " + name + otherstuff + " = new " + t + "[" + length + "];";
                else
                    output = lowestindent + "public " + "" + t + "[] " + name + otherstuff + ";";
            }
            if (!KnownStuff.KnownTypes.ContainsKey(rostype))
                meta = true;
            Name = name.Length == 0 ? otherstuff.Split('=')[0].Trim() : name;
        }
    }
}
