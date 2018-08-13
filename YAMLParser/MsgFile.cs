using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using YAMLParser;

namespace FauxMessages
{
    public enum ServiceMessageType
    {
        Not,
        Request,
        Response
    }

    public class MsgFile
    {
        private const string stfmat = "\tname: {0}\n\t\ttype: {1}\n\t\ttrostype: {2}\n\t\tisliteral: {3}\n\t\tisconst: {4}\n\t\tconstvalue: {5}\n\t\tisarray: {6}\n\t\tlength: {7}\n\t\tismeta: {8}";

        public class ResolvedMsg
        {
            public string OtherType;
            public MsgFile Definer;
        }

        internal MsgFileLocation msgFileLocation;

        public static Dictionary<string, Dictionary<string, List<ResolvedMsg>>> resolver = new Dictionary<string, Dictionary<string, List<ResolvedMsg>>>();

        private string GUTS;
        public string GeneratedDictHelper;
        public bool HasHeader;
        public string Name;
        public string Namespace = "Messages";
        public ActionMessageType ActionMessageType { get; set; } = ActionMessageType.NoAction;
        public List<SingleType> Stuff = new List<SingleType>();
        public string backhalf;
        public string classname;
        private List<string> def = new List<string>();
        private List<string> lines;
        string extraindent;
        public string Package;

        public string Definition
        {
            get { return !def.Any() ? "" : def.Aggregate((old, next) => "" + old + "\n" + next); }
        }

        public string dimensions = "";
        public string fronthalf;
        private string memoizedcontent;
        public bool meta;
        public ServiceMessageType serviceMessageType = ServiceMessageType.Not;

        public MsgFile(MsgFileLocation filename)
            : this(filename, "")
        {
        }

        public MsgFile(MsgFileLocation filename, string extraindent)
        {
            if (filename == null)
                throw new ArgumentNullException(nameof(filename));

            if (!filename.Path.Contains(".msg"))
                throw new ArgumentException($"'{filename}' is not a valid .msg file name.", nameof(filename));

            this.msgFileLocation = filename;
            this.extraindent = extraindent;

            if (resolver == null)
                resolver = new Dictionary<string, Dictionary<string, List<ResolvedMsg>>>();

            classname = filename.basename;
            Package = filename.package;

            //Parse for the Namespace
            Namespace += "." + filename.package;
            Name = filename.package + "." + classname;
            Namespace = Namespace.Trim('.');

            if (!resolver.Keys.Contains(Package))
                resolver.Add(Package, new Dictionary<string, List<ResolvedMsg>>());
            if (!resolver[Package].ContainsKey(classname))
                resolver[Package].Add(classname, new List<ResolvedMsg> { new ResolvedMsg { OtherType = Namespace + "." + classname, Definer = this } });
            else
                resolver[Package][classname].Add(new ResolvedMsg { OtherType = Namespace + "." + classname, Definer = this });

            var lines = new List<string>(File.ReadAllLines(filename.Path));
            lines = lines.Where(st => (!st.Contains('#') || st.Split('#')[0].Length != 0)).ToList();
            for (int i = 0; i < lines.Count; i++)
                lines[i] = lines[i].Split('#')[0].Trim();

            this.lines = lines.Where(s => s.Trim().Length > 0).ToList();
        }

        public MsgFile(MsgFileLocation filename, bool isRequest, List<string> lines)
            : this(filename, isRequest, lines, string.Empty)
        {
        }

        //specifically for SRV halves
        public MsgFile(MsgFileLocation filename, bool isRequest, List<string> lines, string extraIndent)
        {
            this.msgFileLocation = filename;
            this.extraindent = extraIndent;
            this.lines = lines;

            if (resolver == null)
                resolver = new Dictionary<string, Dictionary<string, List<ResolvedMsg>>>();

            serviceMessageType = isRequest ? ServiceMessageType.Request : ServiceMessageType.Response;
            // Parse The file name to get the classname
            classname = filename.basename;
            // Parse for the Namespace
            Namespace += "." + filename.package;
            Name = filename.package + "." + classname;
            classname += (isRequest ? "Request" : "Response");
            Namespace = Namespace.Trim('.');
            Package = filename.package;
            if (!resolver.Keys.Contains(Package))
                resolver.Add(Package, new Dictionary<string, List<ResolvedMsg>>());
            if (!resolver[Package].ContainsKey(classname))
                resolver[Package].Add(classname, new List<ResolvedMsg> { new ResolvedMsg { OtherType = Namespace + "." + classname, Definer = this } });
            else
                resolver[Package][classname].Add(new ResolvedMsg { OtherType = Namespace + "." + classname, Definer = this });
        }


        /// <summary>
        /// Create a non SRV MsgsFile from a list of strings. Use suffix to prepend a string to the classname.
        /// </summary>
        public MsgFile(MsgFileLocation filename, List<string> lines, string suffix)
        {
            if (filename == null)
            {
                throw new ArgumentNullException(nameof(filename));
            }

            this.msgFileLocation = filename;

            if (resolver == null)
                resolver = new Dictionary<string, Dictionary<string, List<ResolvedMsg>>>();

            classname = filename.basename + suffix;
            Package = filename.package;

            //Parse for the Namespace
            Namespace += "." + filename.package;
            Name = filename.package + "." + classname;
            Namespace = Namespace.Trim('.');

            if (!resolver.Keys.Contains(Package))
            {
                resolver.Add(Package, new Dictionary<string, List<ResolvedMsg>>());
            }
            if (!resolver[Package].ContainsKey(classname))
            {
                resolver[Package].Add(classname, new List<ResolvedMsg>());
            }
            resolver[Package][classname].Add(new ResolvedMsg { OtherType = Namespace + "." + classname, Definer = this });
            Debug.Assert(resolver[Package][classname].Count <= 1);

            this.lines = lines.Where(s => s.Trim().Length > 0).ToList();
        }

        public void ParseAndResolveTypes()
        {
            def = new List<string>();
            Stuff = new List<SingleType>();
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l))
                    continue;

                def.Add(l);
                SingleType test = KnownStuff.WhatItIs(this, l, extraindent);
                if (test != null)
                    Stuff.Add(test);
            }
        }

        public static void Resolve(MsgFile parent, SingleType st)
        {
            if (st.Type == null)
            {
                KnownStuff.WhatItIs(parent, st);
            }

            if (st.IsPrimitve)
            {
                return;
            }

            List<string> prefixes = new List<string>(new[] { "", "std_msgs", "geometry_msgs", "actionlib_msgs" });
            if (st.Type.Contains("/"))
            {
                string[] pieces = st.Type.Split('/');
                st.Package = pieces[0];
                st.Type = pieces[1];
            }

            prefixes[0] = !string.IsNullOrEmpty(st.Package) ? st.Package : parent.Package;
            foreach (string p in prefixes)
            {
                if (resolver.Keys.Contains(p))
                {
                    if (resolver[p].ContainsKey(st.Type))
                    {
                        if (resolver[p][st.Type].Count == 1)
                        {
                            st.Package = p;
                            st.Definer = resolver[p][st.Type][0].Definer;
                        }
                        else if (resolver[p][st.Type].Count > 1)
                            throw new ArgumentException($"Could not resolve: {st.Type}");
                    }
                }
            }
        }

        public void resolve(SingleType st)
        {
            Resolve(this, st);
        }

        public string GetSrvHalf()
        {
            string wholename = classname;
            if (wholename.Contains("Response"))
                wholename = wholename.Substring(0, wholename.LastIndexOf("Response"));
            else if (wholename.Contains("Request"))
                wholename = wholename.Substring(0, wholename.LastIndexOf("Request"));
            classname = classname.Replace(wholename, "");
            if (memoizedcontent == null)
            {
                memoizedcontent = "";
                for (int i = 0; i < Stuff.Count; i++)
                {
                    SingleType thisthing = Stuff[i];
                    if (thisthing.Type == "Header")
                    {
                        HasHeader = true;
                    }
                    /*else if (classname == "String")
                    {
                        thisthing.input = thisthing.input.Replace("String", "string");
                        thisthing.Type = thisthing.Type.Replace("String", "string");
                        thisthing.output = thisthing.output.Replace("String", "string");
                    }*/
                    else if (classname == "Time")
                    {
                        thisthing.input = thisthing.input.Replace("Time", "TimeData");
                        thisthing.Type = thisthing.Type.Replace("Time", "TimeData");
                        thisthing.output = thisthing.output.Replace("Time", "TimeData");
                    }
                    else if (classname == "Duration")
                    {
                        thisthing.input = thisthing.input.Replace("Duration", "TimeData");
                        thisthing.Type = thisthing.Type.Replace("Duration", "TimeData");
                        thisthing.output = thisthing.output.Replace("Duration", "TimeData");
                    }
                    meta |= thisthing.meta;
                    memoizedcontent += "\t" + thisthing.output + "\n";
                }
                /*if (classname.ToLower() == "string")
                {
                    memoizedcontent +=
                        "\n\n\t\t\t\t\tpublic String(string s){ data = s; }\n\t\t\t\t\tpublic String(){ data = \"\"; }\n\n";
                }
                else*/
                if (classname == "Time")
                {
                    memoizedcontent +=
                        "\n\n\t\t\t\t\tpublic Time(uint s, uint ns) : this(new TimeData{ sec=s, nsec = ns}){}\n\t\t\t\t\tpublic Time(TimeData s){ data = s; }\n\t\t\t\t\tpublic Time() : this(0,0){}\n\n";
                }
                else if (classname == "Duration")
                {
                    memoizedcontent +=
                        "\n\n\t\t\t\t\tpublic Duration(uint s, uint ns) : this(new TimeData{ sec=s, nsec = ns}){}\n\t\t\t\t\tpublic Duration(TimeData s){ data = s; }\n\t\t\t\t\tpublic Duration() : this(0,0){}\n\n";
                }
                while (memoizedcontent.Contains("DataData"))
                    memoizedcontent = memoizedcontent.Replace("DataData", "Data");
            }
            string ns = Namespace.Replace("Messages.", "");
            if (ns == "Messages")
                ns = "";
            GeneratedDictHelper = "";
            foreach (SingleType S in Stuff)
            {
                Resolve(this, S);
                GeneratedDictHelper += MessageFieldHelper.Generate(S);
            }
            GUTS = fronthalf + memoizedcontent + "\n" +
                   backhalf;
            return GUTS;
        }

        public string GenerateProperties()
        {
            if (memoizedcontent == null)
            {
                memoizedcontent = "";
                for (int i = 0; i < Stuff.Count; i++)
                {
                    SingleType thisthing = Stuff[i];
                    if (thisthing.Type == "Header")
                    {
                        HasHeader = true;
                    }
                    /*else if (classname == "String")
                    {
                        thisthing.input = thisthing.input.Replace("String", "string");
                        thisthing.Type = thisthing.Type.Replace("String", "string");
                        thisthing.output = thisthing.output.Replace("String", "string");
                    }*/
                    else if (classname == "Time")
                    {
                        thisthing.input = thisthing.input.Replace("Time", "TimeData");
                        thisthing.Type = thisthing.Type.Replace("Time", "TimeData");
                        thisthing.output = thisthing.output.Replace("Time", "TimeData");
                    }
                    else if (classname == "Duration")
                    {
                        thisthing.input = thisthing.input.Replace("Duration", "TimeData");
                        thisthing.Type = thisthing.Type.Replace("Duration", "TimeData");
                        thisthing.output = thisthing.output.Replace("Duration", "TimeData");
                    }
                    meta |= thisthing.meta;
                    memoizedcontent += "\t" + thisthing.output + "\n";
                }
                /*if (classname.ToLower() == "string")
                {
                    memoizedcontent +=
                        "\n\n\t\t\t\t\tpublic String(string s){ data = s; }\n\t\t\t\t\tpublic String(){ data = \"\"; }\n\n";
                }
                else*/
                if (classname == "Time")
                {
                    memoizedcontent +=
                        "\n\n\t\t\t\t\tpublic Time(uint s, uint ns) : this(new TimeData{ sec=s, nsec = ns}){}\n\t\t\t\t\tpublic Time(TimeData s){ data = s; }\n\t\t\t\t\tpublic Time() : this(0,0){}\n\n";
                }
                else if (classname == "Duration")
                {
                    memoizedcontent +=
                        "\n\n\t\t\t\t\tpublic Duration(uint s, uint ns) : this(new TimeData{ sec=s, nsec = ns}){}\n\t\t\t\t\tpublic Duration(TimeData s){ data = s; }\n\t\t\t\t\tpublic Duration() : this(0,0){}\n\n";
                }
                while (memoizedcontent.Contains("DataData"))
                    memoizedcontent = memoizedcontent.Replace("DataData", "Data");
            }
            string ns = Namespace.Replace("Messages.", "");
            if (ns == "Messages")
                ns = "";
            GeneratedDictHelper = "";
            foreach (SingleType S in Stuff)
            {
                Resolve(this, S);
                GeneratedDictHelper += MessageFieldHelper.Generate(S);
            }
            GUTS = fronthalf + memoizedcontent + "\n" +
                   backhalf;
            return GUTS;
        }

        public string GenFields()
        {
            string ret = "\n\t\t\t\t";
            for (int i = 0; i < Stuff.Count; i++)
            {
                Stuff[i].refinalize(this, Stuff[i].Type);
                ret += ((i > 0) ? "}, \n\t\t\t\t{" : "") + MessageFieldHelper.Generate(Stuff[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            if (fronthalf == null)
            {
                fronthalf = "";
                backhalf = "";
                string[] lines = Templates.MsgPlaceHolder.Split('\n');
                bool hitvariablehole = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("$$DOLLADOLLABILLS"))
                    {
                        hitvariablehole = true;
                        continue;
                    }
                    if (lines[i].Contains("namespace"))
                    {
                        fronthalf +=
                            "using Messages.std_msgs;\nusing String=System.String;\n\n"
                            ;
                        fronthalf += "namespace " + Namespace + "\n";
                        continue;
                    }
                    if (!hitvariablehole)
                        fronthalf += lines[i] + "\n";
                    else
                        backhalf += lines[i] + "\n";
                }
            }
            string GeneratedDeserializationCode = "", GeneratedSerializationCode = "", GeneratedRandomizationCode = "", GeneratedEqualityCode = "";
            if (memoizedcontent == null)
            {
                memoizedcontent = "";
                for (int i = 0; i < Stuff.Count; i++)
                {
                    SingleType thisthing = Stuff[i];
                    if (thisthing.Type == "Header")
                    {
                        HasHeader = true;
                    }
                    else if (classname == "Time")
                    {
                        thisthing.input = thisthing.input.Replace("Time", "TimeData");
                        thisthing.Type = thisthing.Type.Replace("Time", "TimeData");
                        thisthing.output = thisthing.output.Replace("Time", "TimeData");
                    }
                    else if (classname == "Duration")
                    {
                        thisthing.input = thisthing.input.Replace("Duration", "TimeData");
                        thisthing.Type = thisthing.Type.Replace("Duration", "TimeData");
                        thisthing.output = thisthing.output.Replace("Duration", "TimeData");
                    }
                    thisthing.input = thisthing.input.Replace("String", "string");
                    thisthing.Type = thisthing.Type.Replace("String", "string");
                    thisthing.output = thisthing.output.Replace("String", "string");
                    meta |= thisthing.meta;
                    memoizedcontent += "\t" + thisthing.output + "\n";
                }
                string ns = Namespace.Replace("Messages.", "");
                if (ns == "Messages")
                    ns = "";
                while (memoizedcontent.Contains("DataData"))
                    memoizedcontent = memoizedcontent.Replace("DataData", "Data");
                //if (GeneratedDictHelper == null)
                //    GeneratedDictHelper = TypeInfo.Generate(classname, ns, HasHeader, meta, def, Stuff);
                GeneratedDictHelper = GenFields();
                StringBuilder DEF = new StringBuilder();
                foreach (string s in def)
                    DEF.AppendLine(s);
            }
            GUTS = (serviceMessageType != ServiceMessageType.Response ? fronthalf : "") + "\n" + memoizedcontent + "\n" +
                   (serviceMessageType != ServiceMessageType.Request ? backhalf : "");
            if (classname.ToLower() == "string")
            {
                GUTS = GUTS.Replace("$NULLCONSTBODY", "if (data == null)\n\t\t\tdata = \"\";\n");
                GUTS = GUTS.Replace("$EXTRACONSTRUCTOR", "\n\t\tpublic $WHATAMI(string d)\n\t\t{\n\t\t\tdata = d;\n\t\t}\n");
            }
            else if (classname == "Time" || classname == "Duration")
            {
                GUTS = GUTS.Replace("$EXTRACONSTRUCTOR", "\n\t\tpublic $WHATAMI(TimeData d)\n\t\t{\n\t\t\tdata = d;\n\t\t}\n");
            }
            GUTS = GUTS.Replace("$WHATAMI", classname);
            GUTS = GUTS.Replace("$MYISMETA", meta.ToString().ToLower());
            GUTS = GUTS.Replace("$MYMSGTYPE", Namespace.Replace("Messages.", "") + "/" + classname);
            for (int i = 0; i < def.Count; i++)
            {
                while (def[i].Contains("\t"))
                    def[i] = def[i].Replace("\t", " ");
                while (def[i].Contains("\n\n"))
                    def[i] = def[i].Replace("\n\n", "\n");
                def[i] = def[i].Replace('\t', ' ');
                while (def[i].Contains("  "))
                    def[i] = def[i].Replace("  ", " ");
                def[i] = def[i].Replace(" = ", "=");
            }
            GUTS = GUTS.Replace("$MYMESSAGEDEFINITION", "@\"" + def.Aggregate("", (current, d) => current + (d + "\n")).Trim('\n').Replace("\"", "\"\"") + "\"");
            GUTS = GUTS.Replace("$MYHASHEADER", HasHeader.ToString().ToLower());
            GUTS = GUTS.Replace("$MYFIELDS", GeneratedDictHelper.Length > 5 ? "{{" + GeneratedDictHelper + "}}" : "()");
            GUTS = GUTS.Replace("$NULLCONSTBODY", "");
            GUTS = GUTS.Replace("$EXTRACONSTRUCTOR", "");
            string md5 = MD5.Sum(this);
            if (md5 == null)
                return null;

            GeneratedEqualityCode += string.Format(@"var other = ____other as Messages.{0};
            if (other == null)
                return false;", Name);

            for (int i = 0; i < Stuff.Count; i++)
            {
                GeneratedDeserializationCode += this.GenerateDeserializationCode(Stuff[i]);
                GeneratedSerializationCode += this.GenerateSerializationCode(Stuff[i]);
                GeneratedRandomizationCode += this.GenerateRandomizationCode(Stuff[i]);
                GeneratedEqualityCode += this.GenerateEqualityCode(Stuff[i]);
            }
            GUTS = GUTS.Replace("$SERIALIZATIONCODE", GeneratedSerializationCode);
            GUTS = GUTS.Replace("$DESERIALIZATIONCODE", GeneratedDeserializationCode);
            GUTS = GUTS.Replace("$RANDOMIZATIONCODE", GeneratedRandomizationCode);
            GUTS = GUTS.Replace("$EQUALITYCODE", GeneratedEqualityCode);
            GUTS = GUTS.Replace("$MYMD5SUM", md5);

            return GUTS;
        }

        /// <summary>
        /// How many 4-space "tabs" to prepend
        /// </summary>
        private const int LEADING_WHITESPACE = 3;

        private string GenerateSerializationForOne(string type, string name, SingleType st, int extraTabs = 0, bool special = false)
        {
            string leadingWhitespace = "";
            for (int i = 0; i < LEADING_WHITESPACE + extraTabs; i++)
                leadingWhitespace += "    ";
            type = KnownStuff.GetNamespacedType(st, type);
            if (type == "Time" || type == "Duration")
            {
                return string.Format(@"
{0}//{1}
{0}pieces.Add(BitConverter.GetBytes({1}.data.sec));
{0}pieces.Add(BitConverter.GetBytes({1}.data.nsec));", leadingWhitespace, name);
            }
            else if (type == "TimeData")
                return string.Format(@"
{0}//{1}
{0}pieces.Add(BitConverter.GetBytes({1}.sec));
{0}pieces.Add(BitConverter.GetBytes({1}.nsec));", leadingWhitespace, name);
            else if (type == "byte")
            {
                return string.Format(@"
{0}//{1}
{0}pieces.Add(new[] {{ (byte){1} }});", leadingWhitespace, name); ;
            }
            else if (type == "string")
            {
                return string.Format(@"
{0}//{1}
{0}if ({1} == null)
{0}    {1} = """";
{0}scratch1 = Encoding.ASCII.GetBytes((string){1});
{0}thischunk = new byte[scratch1.Length + 4];
{0}scratch2 = BitConverter.GetBytes(scratch1.Length);
{0}Array.Copy(scratch1, 0, thischunk, 4, scratch1.Length);
{0}Array.Copy(scratch2, thischunk, 4);
{0}pieces.Add(thischunk);", leadingWhitespace, name);
            }
            else if (type == "bool")
            {
                return string.Format(@"
{0}//{1}
{0}thischunk = new byte[1];
{0}thischunk[0] = (byte) ((bool){1} ? 1 : 0 );
{0}pieces.Add(thischunk);", leadingWhitespace, name);
            }
            else if (st.IsLiteral && !special)
            {
                return string.Format(@"
{0}//{1}
{0}scratch1 = new byte[Marshal.SizeOf(typeof({2}))];
{0}h = GCHandle.Alloc(scratch1, GCHandleType.Pinned);
{0}Marshal.StructureToPtr({1}, h.AddrOfPinnedObject(), false);
{0}h.Free();
{0}pieces.Add(scratch1);", leadingWhitespace, name, type);
            }
            else if (st.IsLiteral && special)
            {
                return string.Format(@"
// Start Xamla
{0}//{1}
{0}x__size = Marshal.SizeOf(typeof({2})) * {1}.Length;
{0}scratch1 = new byte[x__size];
{0}Buffer.BlockCopy({1}, 0, scratch1, 0, x__size);
{0}pieces.Add(scratch1);
// End Xamla
", leadingWhitespace, name.Substring(0, name.Length - 3), type);
            }
            else
            {
                return string.Format(@"
{0}//{1}
{0}if ({1} == null)
{0}    {1} = new {2}();
{0}pieces.Add({1}.Serialize(true));", leadingWhitespace, name, type);
            }
        }
        public string GenerateSerializationCode(SingleType st, int extraTabs = 0)
        {
            string leadingWhitespace = "";
            for (int i = 0; i < LEADING_WHITESPACE + extraTabs; i++)
                leadingWhitespace += "    ";
            if (st.Const)
                return "";
            if (!st.IsArray)
            {
                return GenerateSerializationForOne(st.Type, st.Name, st, extraTabs);
            }

            int arraylength = -1;
            string ret = string.Format(@"
{0}//{2}
{0}hasmetacomponents |= {1};", leadingWhitespace, st.meta.ToString().ToLower(), st.Name);
            string completetype = KnownStuff.GetNamespacedType(st);
            ret += string.Format(@"
{0}if ({1} == null)
{0}    {1} = new {2}[0];", leadingWhitespace, st.Name, completetype);
            if (string.IsNullOrEmpty(st.length) || !int.TryParse(st.length, out arraylength) || arraylength == -1)
            {
                ret += string.Format(@"
{0}pieces.Add(BitConverter.GetBytes({1}.Length));", leadingWhitespace, st.Name);
            }
            //special case arrays of bytes
            if (st.Type == "byte")
            {
                ret += string.Format(@"
{0}pieces.Add(({2}[]){1});", leadingWhitespace, st.Name, st.Type);
            }
            else
            {
                if (!st.IsPrimitve || (st.Type == "string") || (st.Type == "bool"))
                {
                    ret += string.Format(@"
{0}for (int i=0;i<{1}.Length; i++) {{{2}
{0}}}", leadingWhitespace, st.Name, GenerateSerializationForOne(st.Type, st.Name + "[i]", st, extraTabs + 1));
                }
                else
                {
                    ret += GenerateSerializationForOne(st.Type, st.Name + "[i]", st, extraTabs + 1, true);
                }
            }
            return ret;
        }

        public string GenerateDeserializationCode(SingleType st, int extraTabs = 0)
        {
            string leadingWhitespace = "";
            for (int i = 0; i < LEADING_WHITESPACE + extraTabs; i++)
                leadingWhitespace += "    ";
            // this happens  for each member of the outer message
            // after concluding, make sure part of the string is "currentIndex += <amount read while deserializing this thing>"
            // start of deserializing piece referred to by st is currentIndex (its value at time of call to this fn)"

            string pt = KnownStuff.GetNamespacedType(st);
            if (st.Const)
            {
                return "";
            }
            else if (!st.IsArray)
            {
                return GenerateDeserializationForOne(st.Type, st.Name, st, extraTabs);
            }

            string ret = string.Format(@"
{0}//{2}
{0}hasmetacomponents |= {1};", leadingWhitespace, st.meta.ToString().ToLower(), st.Name);
            int arraylength = -1;
            string arraylengthstr = "arraylength";

            //If the object is an array, send each object to be processed individually, then add them to the string

            //handle fixed length fields?
            if (!string.IsNullOrEmpty(st.length) && int.TryParse(st.length, out arraylength) && arraylength != -1)
            {
                arraylengthstr = "" + arraylength;
            }
            else
            {
                ret += string.Format(@"
{0}arraylength = BitConverter.ToInt32(serializedMessage, currentIndex);
{0}currentIndex += Marshal.SizeOf(typeof(System.Int32));", leadingWhitespace);
            }
            ret += string.Format(@"
{0}if ({1} == null)
{0}    {1} = new {2}[{3}];
{0}else
{0}    Array.Resize(ref {1}, {3});", leadingWhitespace, st.Name, pt, arraylengthstr);

            //special case arrays of bytes
            if (st.Type == "byte")
            {
                ret += string.Format(@"
{0}Array.Copy(serializedMessage, currentIndex, {1}, 0, {1}.Length);
{0}currentIndex += {1}.Length;", leadingWhitespace, st.Name);
            }
            else
            {
                if (!st.IsPrimitve || (st.Type == "string") || (st.Type == "bool"))
                {
                    ret += string.Format(@"
{0}for (int i=0;i<{1}.Length; i++) {{{2}
{0}}}", leadingWhitespace, st.Name, GenerateDeserializationForOne(pt, st.Name + "[i]", st, extraTabs + 1));
                }
                else
                {
                    ret += GenerateDeserializationForOne(pt, st.Name + "[i]", st, extraTabs + 1, true);
                }
            }
            return ret;
        }

        private string GenerateDeserializationForOne(string type, string name, SingleType st, int extraTabs = 0, bool special = false)
        {
            string leadingWhitespace = "";
            for (int i = 0; i < LEADING_WHITESPACE + extraTabs; i++)
                leadingWhitespace += "    ";
            string pt = KnownStuff.GetNamespacedType(st);
            if (type == "Time" || type == "Duration")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = new {2}(new TimeData(
{0}        BitConverter.ToUInt32(serializedMessage, currentIndex),
{0}        BitConverter.ToUInt32(serializedMessage, currentIndex+Marshal.SizeOf(typeof(System.Int32)))));
{0}currentIndex += 2*Marshal.SizeOf(typeof(System.Int32));", leadingWhitespace, name, pt);
            }
            else if (type == "TimeData")
                return string.Format(@"
{0}//{1}
{0}{1}.sec = BitConverter.ToUInt32(serializedMessage, currentIndex);
{0}currentIndex += Marshal.SizeOf(typeof(System.Int32));
{0}{1}.nsec  = BitConverter.ToUInt32(serializedMessage, currentIndex);
{0}currentIndex += Marshal.SizeOf(typeof(System.Int32));", leadingWhitespace, name);
            else if (type == "byte")
            {
                return string.Format(@"
{0}//{1}
{0}{1}=serializedMessage[currentIndex++];", leadingWhitespace, name);
            }
            else if (type == "string")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = """";
{0}piecesize = BitConverter.ToInt32(serializedMessage, currentIndex);
{0}currentIndex += 4;
{0}{1} = Encoding.ASCII.GetString(serializedMessage, currentIndex, piecesize);
{0}currentIndex += piecesize;", leadingWhitespace, name);
            }
            else if (type == "bool")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = serializedMessage[currentIndex++]==1;", leadingWhitespace, name);
            }
            else if (st.IsLiteral && !special)
            {
                string ret = string.Format(@"
{0}//{2}
{0}piecesize = Marshal.SizeOf(typeof({1}));
{0}h = IntPtr.Zero;
{0}if (serializedMessage.Length - currentIndex != 0)
{0}{{
{0}    h = Marshal.AllocHGlobal(piecesize);
{0}    Marshal.Copy(serializedMessage, currentIndex, h, piecesize);
{0}}}
{0}if (h == IntPtr.Zero) throw new Exception(""Memory allocation failed"");
{0}{2} = ({1})Marshal.PtrToStructure(h, typeof({1}));
{0}Marshal.FreeHGlobal(h);
{0}currentIndex+= piecesize;", leadingWhitespace, pt, name);

                return ret;
            }
            else if (st.IsLiteral && special)
            {
                string ret = string.Format(@"
// Start Xamla
{0}//{2}
{0}piecesize = Marshal.SizeOf(typeof({1})) * {2}.Length;
{0}if (currentIndex + piecesize > serializedMessage.Length) {{
{0}    throw new Exception(""Memory allocation failed: Ran out of bytes to read."");
{0}}}
{0}Buffer.BlockCopy(serializedMessage, currentIndex, {2}, 0, piecesize);
{0}currentIndex += piecesize;
// End Xamla
", leadingWhitespace, pt, name.Substring(0, name.Length - 3));

                return ret;
            }
            else
            {
                return string.Format(@"
{0}//{1}
{0}{1} = new {2}(serializedMessage, ref currentIndex);", leadingWhitespace, name, pt);
            }
        }

        public string GenerateRandomizationCode(SingleType st, int extraTabs = 0)
        {
            string leadingWhitespace = "";
            for (int i = 0; i < LEADING_WHITESPACE + extraTabs; i++)
                leadingWhitespace += "    ";

            // this happens  for each member of the outer message
            // after concluding, make sure part of the string is "currentIndex += <amount read while deserializing this thing>"
            // start of deserializing piece referred to by st is currentIndex (its value at time of call to this fn)"

            string pt = KnownStuff.GetNamespacedType(st);
            if (st.Const)
            {
                return "";
            }
            else if (!st.IsArray)
            {
                return GenerateRandomizationCodeForOne(st.Type, st.Name, st, extraTabs);
            }

            string ret = string.Format(@"
{0}//{1}", leadingWhitespace, st.Name);
            int arraylength = -1;
            string arraylengthstr = "arraylength";

            //If the object is an array, send each object to be processed individually, then add them to the string

            //handle fixed length fields?
            if (!string.IsNullOrEmpty(st.length) && int.TryParse(st.length, out arraylength) && arraylength != -1)
            {
                arraylengthstr = "" + arraylength;
            }
            else
            {
                ret += string.Format(@"
{0}arraylength = rand.Next(10);", leadingWhitespace);
            }
            ret += string.Format(@"
{0}if ({1} == null)
{0}    {1} = new {2}[{3}];
{0}else
{0}    Array.Resize(ref {1}, {3});", leadingWhitespace, st.Name, pt, arraylengthstr);

            ret += string.Format(@"
{0}for (int i=0;i<{1}.Length; i++) {{{2}
{0}}}", leadingWhitespace, st.Name, GenerateRandomizationCodeForOne(pt, st.Name + "[i]", st, extraTabs + 1));
            return ret;
        }

        private string GenerateRandomizationCodeForOne(string type, string name, SingleType st, int extraTabs = 0)
        {
            string leadingWhitespace = "";
            for (int i = 0; i < LEADING_WHITESPACE + extraTabs; i++)
                leadingWhitespace += "    ";
            string pt = KnownStuff.GetNamespacedType(st);
            if (type == "Time" || type == "Duration")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = new {2}(new TimeData(
{0}        Convert.ToUInt32(rand.Next()),
{0}        Convert.ToUInt32(rand.Next())));", leadingWhitespace, name, pt);
            }
            else if (type == "TimeData")
                return string.Format(@"
{0}//{1}
{0}{1}.sec = Convert.ToUInt32(rand.Next());
{0}{1}.nsec  = Convert.ToUInt32(rand.Next());", leadingWhitespace, name);
            else if (type == "byte")
            {
                return string.Format(@"
{0}//{1}
{0}myByte = new byte[1];
{0}rand.NextBytes(myByte);
{0}{1}= myByte[0];", leadingWhitespace, name);
            }
            else if (type == "string")
            {
                return string.Format(@"
{0}//{1}
{0}strlength = rand.Next(100) + 1;
{0}strbuf = new byte[strlength];
{0}rand.NextBytes(strbuf);  //fill the whole buffer with random bytes
{0}for (int __x__ = 0; __x__ < strlength; __x__++)
{0}    if (strbuf[__x__] == 0) //replace null chars with non-null random ones
{0}        strbuf[__x__] = (byte)(rand.Next(254) + 1);
{0}strbuf[strlength - 1] = 0; //null terminate
{0}{1} = Encoding.ASCII.GetString(strbuf);", leadingWhitespace, name);
            }
            else if (type == "bool")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = rand.Next(2) == 1;", leadingWhitespace, name);
            }
            else if (type == "int")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = rand.Next();", leadingWhitespace, name);
            }
            else if (type == "uint")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (uint)rand.Next();", leadingWhitespace, name);
            }
            else if (type == "double")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (rand.Next() + rand.NextDouble());", leadingWhitespace, name);
            }
            else if (type == "float" || type == "Float64" || type == "Single")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (float)(rand.Next() + rand.NextDouble());", leadingWhitespace, name);
            }
            else if (type == "Int16" || type == "Short" || type == "short")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (System.Int16)rand.Next(System.Int16.MaxValue + 1);", leadingWhitespace, name);
            }
            else if (type == "UInt16" || type == "ushort" || type == "UShort")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (System.UInt16)rand.Next(System.UInt16.MaxValue + 1);", leadingWhitespace, name);
            }
            else if (type == "SByte" || type == "sbyte")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (SByte)(rand.Next(255) - 127);", leadingWhitespace, name);
            }
            else if (type == "UInt64" || type == "ULong" || type == "ulong")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (System.UInt64)((uint)(rand.Next() << 32)) | (uint)rand.Next();", leadingWhitespace, name);
            }
            else if (type == "Int64" || type == "Long" || type == "long")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (System.Int64)(rand.Next() << 32) | rand.Next();", leadingWhitespace, name);
            }
            else if (type == "char")
            {
                return string.Format(@"
{0}//{1}
{0}{1} = (char)(byte)(rand.Next(254) + 1);", leadingWhitespace, name);
            }
            else if (st.IsLiteral)
            {
                throw new ArgumentException($"{st.Type} is not supported");
            }
            else
            {
                return string.Format(@"
{0}//{1}
{0}{1} = new {2}();
{0}{1}.Randomize();", leadingWhitespace, name, pt);
            }
        }

        public string GenerateEqualityCode(SingleType st, int extraTabs = 0)
        {
            string leadingWhitespace = "";
            for (int i = 0; i < LEADING_WHITESPACE + extraTabs; i++)
                leadingWhitespace += "    ";

            if (st.IsArray)
            {
                return string.Format(@"
{0}if ({1}.Length != other.{1}.Length)
{0}    return false;
{0}for (int __i__=0; __i__ < {1}.Length; __i__++)
{0}{{{2}
{0}}}", leadingWhitespace, st.Name, GenerateEqualityCodeForOne(st.Type, st.Name + "[__i__]", st, extraTabs + 1));
            }
            else
                return GenerateEqualityCodeForOne(st.Type, st.Name, st, extraTabs);
        }

        private string GenerateEqualityCodeForOne(string type, string name, SingleType st, int extraTabs = 0)
        {
            string leadingWhitespace = "";
            for (int i = 0; i < LEADING_WHITESPACE + extraTabs; i++)
                leadingWhitespace += "    ";
            if (type == "Time" || type == "Duration")
                return string.Format(@"
{0}ret &= {1}.data.Equals(other.{1}.data);", leadingWhitespace, name);
            else if (type == "TimeData")
                return string.Format(@"
{0}ret &= {1}.Equals(other.{1});", leadingWhitespace, name);
            else if (st.IsLiteral)
            {
                if (st.Const)
                    return "";
                return string.Format(@"
{0}ret &= {1} == other.{1};", leadingWhitespace, name);
            }
            else
                return string.Format(@"
{0}ret &= {1}.Equals(other.{1});", leadingWhitespace, name);
            // and that's it
        }

        public void Write(string outdir)
        {
            string[] chunks = Name.Split('.');
            for (int i = 0; i < chunks.Length - 1; i++)
                outdir = Path.Combine(outdir, chunks[i]);
            if (!Directory.Exists(outdir))
                Directory.CreateDirectory(outdir);

            string localcn = classname;
            if (serviceMessageType != ServiceMessageType.Not)
                localcn = classname.Replace("Request", "").Replace("Response", "");

            string contents = this.ToString();
            if (contents == null)
                return;
            if (serviceMessageType == ServiceMessageType.Response)
                File.AppendAllText(Path.Combine(outdir, localcn + ".cs"), contents.Replace("FauxMessages", "Messages"));
            else
                File.WriteAllText(Path.Combine(outdir, localcn + ".cs"), contents.Replace("FauxMessages", "Messages"));
        }
    }


    public enum ActionMessageType
    {
        NoAction,
        Goal,
        Result,
        Feedback
    }
}
