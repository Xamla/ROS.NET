using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FauxMessages
{
    public class MsgFieldInfo
    {
        public string StaticVal;
        public bool IsArray;
        public bool IsStatic;
        public bool IsPrimitive;
        public bool IsComplexType;
        public int Length = -1;
        public string Name;
        public string Type;

        public MsgFieldInfo(string name, bool isPrimitive, string type, bool isStatic, string StaticVal, bool isArray,
            string lengths, bool complexType)
        {
            Name = name;
            IsArray = isArray;
            Type = type;
            IsPrimitive = isPrimitive;
            IsComplexType = complexType;
            IsStatic = isStatic;
            StaticVal = StaticVal;
            if (!string.IsNullOrWhiteSpace(lengths))
            {
                Length = int.Parse(lengths);
            }
        }
    }
}
