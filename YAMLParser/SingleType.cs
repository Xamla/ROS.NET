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
        // c# keywords listed on https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/ as of August 1st, 2018
        private static readonly string[] reserved_keywords = { "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "using static", "virtual", "void", "volatile", "while" };
        private static readonly string[] contextual_keywords = { "add", "alias", "ascending", "async", "await", "descending", "dynamic", "from", "get", "global", "group", "into", "join", "let", "nameof", "orderby", "partial", "partial", "remove", "select", "set", "value", "var", "when", "where", "where", "yield" };
        private static readonly string[] CSharpKeywords = reserved_keywords.Union(contextual_keywords).ToArray();

        public static bool IsCSharpKeyword(string name)
        {
            return CSharpKeywords.Contains(name);
        }

        public bool Const;
        public string ConstValue = "";
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
            bool isconst = false;
            IsLiteral = isliteral;
            string type = s[0];
            string name = s[1];
            string otherstuff = "";
            if (name.Contains('='))
            {
                string[] parts = name.Split('=');
                isconst = true;
                name = parts[0];
                otherstuff = " = " + parts[1];
            }

            if (name == parent.Name.Split(".").Last() || !MsgFileLocation.IsValidCSharpIdentifier(name) && name.Length > 0)
            {
                if (IsCSharpKeyword(name))
                {
                    name = "@" + name;
                }
                else if (MsgFileLocation.IsValidCSharpIdentifier(name) && name == parent.Name.Split(".").Last())
                    name = "_" + name;
                else
                    throw new ArgumentException(String.Format("Variable '{0}' from '{1}' is not a compatible C# identifier name\n\tAll variable names must conform to C# Language Specifications (refer to this StackOverflow answer: https://stackoverflow.com/a/950651/4036588)\n", name, parent.msgFileLocation.Path));
            }

            for (int i = 2; i < s.Length; i++)
                otherstuff += " " + s[i];

            if (otherstuff.Contains('='))
                isconst = true;

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

                Const = isconst;
                bool wantsconstructor = !KnownStuff.IsPrimitiveType(this);
                if (otherstuff.Contains("="))
                {
                    string[] chunks = otherstuff.Split('=');
                    ConstValue = chunks[chunks.Length - 1].Trim();
                    if (type.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        otherstuff = chunks[0] + " = \"" + chunks[1].Trim() + "\"";
                        wantsconstructor = false;
                    }
                }
                string prefix = "", suffix = "";
                if (isconst)
                {
                    // why can't strings be constants?

                    //if (!type.Equals("string", StringComparison.OrdinalIgnoreCase))
                    //{
                        if (KnownStuff.IsPrimitiveType(this))
                            prefix = "const ";
                        else
                            prefix = "static readonly ";
                        wantsconstructor = false;
                    //}
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
                else
                {
                    if (type == "string")
                        suffix = " = \"\"";
                    else if (wantsconstructor)
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
            bool isconst = false;
            Type = REALTYPE;
            string name = backup[1];
            string otherstuff = "";
            if (name.Contains('='))
            {
                string[] parts = name.Split('=');
                isconst = true;
                name = parts[0];
                otherstuff = " = " + parts[1];
            }

            if (name == parent.Name.Split(".").Last() || !MsgFileLocation.IsValidCSharpIdentifier(name) && name.Length > 0)
            {
                if (IsCSharpKeyword(name))
                {
                    name = "@" + name;
                }
                else if (MsgFileLocation.IsValidCSharpIdentifier(name) && name == parent.Name.Split(".").Last())
                    name = "_" + name;
                else
                    throw new ArgumentException(String.Format("Variable '{0}' from '{1}' is not a compatible C# identifier name\n\tAll variable names must conform to C# Language Specifications (refer to this StackOverflow answer: https://stackoverflow.com/a/950651/4036588)\n", name, parent.msgFileLocation.Path));
            }

            for (int i = 2; i < backup.Length; i++)
                otherstuff += " " + backup[i];
            if (otherstuff.Contains('='))
                isconst = true;
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
                Const = isconst;
                bool wantsconstructor = false;
                if (otherstuff.Contains("="))
                {
                    string[] chunks = otherstuff.Split('=');
                    ConstValue = chunks[chunks.Length - 1].Trim();
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
                if (isconst)
                {
                    // why can't strings be constants?

                    //if (!Type.Equals("string", StringComparison.OrdinalIgnoreCase))
                    //{
                        prefix = "const ";
                    //}
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
