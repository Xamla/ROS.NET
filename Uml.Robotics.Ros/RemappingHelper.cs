using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

namespace Uml.Robotics.Ros
{
    public static class RemappingHelper
    {
        public static bool GetRemappings(ref string[] args, out IDictionary<string, string> remapping)
        {
            remapping = new Dictionary<string, string>();
            List<string> toremove = new List<string>();
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Contains(":="))
                    {
                        string[] chunks = args[i].Split(new string[] { ":=" }, 2, StringSplitOptions.None);
                        chunks[1] = chunks[1].Trim();
                        chunks[0] = chunks[0].Trim();
                        remapping.Add(chunks[0], chunks[1]);
                        switch (chunks[0])
                        {
                            // if already defined, then it was defined by the program, so leave it
                            case "__master":
                                if (string.IsNullOrEmpty(ROS.ROS_MASTER_URI))
                                    ROS.ROS_MASTER_URI = chunks[1].Trim();
                                break;
                            case "__hostname":
                                if (string.IsNullOrEmpty(ROS.ROS_HOSTNAME))
                                    ROS.ROS_HOSTNAME = chunks[1].Trim();
                                break;
                        }
                        toremove.Add(args[i]);
                    }
                    args = args.Except(toremove).ToArray();
                }
            }

            // If ROS.ROS_MASTER_URI was not explicitely set by the program calling Init, and was not passed in as a remapping argument, then try to find it in ENV.
            if (string.IsNullOrEmpty(ROS.ROS_MASTER_URI))
            {
                IDictionary _vars;

                //check user env first, then machine if user doesn't have uri defined.
                if ((_vars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User)).Contains("ROS_MASTER_URI")
                    || (_vars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine)).Contains("ROS_MASTER_URI"))
                    ROS.ROS_MASTER_URI = (string)_vars["ROS_MASTER_URI"];
            }

            // If ROS.ROS_HOSTNAME was not explicitely set by the program calling Init, check the environment.
            if (string.IsNullOrEmpty(ROS.ROS_HOSTNAME))
            {
                IDictionary _vars;

                //check user env first, then machine if user doesn't have uri defined.
                if ((_vars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User)).Contains("ROS_HOSTNAME")
                    || (_vars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine)).Contains("ROS_HOSTNAME"))
                    ROS.ROS_HOSTNAME = (string)_vars["ROS_HOSTNAME"];
            }

            // if it is defined now, then add to remapping, or replace remapping (in the case it was explicitly set by program AND was passed as remapping arg)
            if (!string.IsNullOrEmpty(ROS.ROS_MASTER_URI))
            {
                remapping["__master"] = ROS.ROS_MASTER_URI;
            }
            else
            {
                throw new RosException("Unknown ROS_MASTER_URI\n" +
                    "ROS_MASTER_URI needs to be defined for your program to function Either:\n" +
                    "set an environment variable called ROS_MASTER_URI,\n" +
                    "pass a __master remapping argument to your program,\n" +
                    "or set the URI explicitely in your program before calling Init."
                );
            }

            if (!string.IsNullOrEmpty(ROS.ROS_HOSTNAME))
            {
                // add __hostname to dictionary or set it to a new value if the key already exists
                remapping["__hostname"] = ROS.ROS_HOSTNAME;
            }

            if (!string.IsNullOrEmpty(ROS.ROS_IP))
            {
                // add __ip to dictionary or set it to a new value if the key already exists
                remapping["__ip"] = ROS.ROS_IP;
            }

            return true;
        }
    }
}
