using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Uml.Robotics.Ros;

namespace Messages.actionlib_msgs
{
    public class GoalStatusArray : RosMessage
    {

        public Messages.std_msgs.Header header = new Messages.std_msgs.Header();
        public Messages.actionlib_msgs.GoalStatus[] status_list;


        public override string MD5Sum() { return "8b2b82f13216d0a8ea88bd3af735e619"; }
        public override bool HasHeader() { return true; }
        public override bool IsMetaType() { return true; }
        public override string MessageDefinition() { return @"Header header
GoalStatus[] status_list"; }
        public override string MessageType { get { return "actionlib_msgs/GoalStatusArray"; } }
        public override bool IsServiceComponent() { return false; }

        public GoalStatusArray()
        {

        }

        public GoalStatusArray(byte[] serializedMessage)
        {
            Deserialize(serializedMessage);
        }

        public GoalStatusArray(byte[] serializedMessage, ref int currentIndex)
        {
            Deserialize(serializedMessage, ref currentIndex);
        }



        public override void Deserialize(byte[] serializedMessage, ref int currentIndex)
        {
            int arraylength = -1;
            bool hasmetacomponents = false;

            //header
            header = new Messages.std_msgs.Header(serializedMessage, ref currentIndex);
            //status_list
            hasmetacomponents |= true;
            arraylength = BitConverter.ToInt32(serializedMessage, currentIndex);
            currentIndex += Marshal.SizeOf(typeof(System.Int32));
            if (status_list == null)
                status_list = new Messages.actionlib_msgs.GoalStatus[arraylength];
            else
                Array.Resize(ref status_list, arraylength);
            for (int i = 0; i < status_list.Length; i++)
            {
                //status_list[i]
                status_list[i] = new Messages.actionlib_msgs.GoalStatus(serializedMessage, ref currentIndex);
            }
        }

        public override byte[] Serialize(bool partofsomethingelse)
        {
            bool hasmetacomponents = false;
            List<byte[]> pieces = new List<byte[]>();

            //header
            if (header == null)
                header = new Messages.std_msgs.Header();
            pieces.Add(header.Serialize(true));
            //status_list
            hasmetacomponents |= true;
            if (status_list == null)
                status_list = new Messages.actionlib_msgs.GoalStatus[0];
            pieces.Add(BitConverter.GetBytes(status_list.Length));
            for (int i = 0; i < status_list.Length; i++)
            {
                //status_list[i]
                if (status_list[i] == null)
                    status_list[i] = new Messages.actionlib_msgs.GoalStatus();
                pieces.Add(status_list[i].Serialize(true));
            }
            // combine every array in pieces into one array and return it
            int __a_b__f = pieces.Sum((__a_b__c) => __a_b__c.Length);
            int __a_b__e = 0;
            byte[] __a_b__d = new byte[__a_b__f];
            foreach (var __p__ in pieces)
            {
                Array.Copy(__p__, 0, __a_b__d, __a_b__e, __p__.Length);
                __a_b__e += __p__.Length;
            }
            return __a_b__d;
        }

        public override void Randomize()
        {
            int arraylength = -1;
            Random rand = new Random();

            //header
            header = new Messages.std_msgs.Header();
            header.Randomize();
            //status_list
            arraylength = rand.Next(10);
            if (status_list == null)
                status_list = new Messages.actionlib_msgs.GoalStatus[arraylength];
            else
                Array.Resize(ref status_list, arraylength);
            for (int i = 0; i < status_list.Length; i++)
            {
                //status_list[i]
                status_list[i] = new Messages.actionlib_msgs.GoalStatus();
                status_list[i].Randomize();
            }
        }

        public override bool Equals(RosMessage ____other)
        {
            if (____other == null)
                return false;
            bool ret = true;
            var other = ____other as Messages.actionlib_msgs.GoalStatusArray;
            if (other == null)
                return false;
            ret &= header.Equals(other.header);
            if (status_list.Length != other.status_list.Length)
                return false;
            for (int __i__ = 0; __i__ < status_list.Length; __i__++)
            {
                ret &= status_list[__i__].Equals(other.status_list[__i__]);
            }
            // for each SingleType st:
            //    ret &= {st.Name} == other.{st.Name};
            return ret;
        }
    }
}
