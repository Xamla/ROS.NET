using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

using Messages.std_msgs;
using Messages.geometry_msgs;
using Messages.nav_msgs;
using String=Messages.std_msgs.String;

namespace Messages.geometry_msgs
{

		public class PoseWithCovariance
		{
			public Pose pose;
			public double[] covariance = new double[36];
		}
}
