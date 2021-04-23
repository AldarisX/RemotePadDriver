using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotePadDriver
{
    class Util
    {
        public static long GetTime()
        {
            //return (DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000;
            return DateTime.Now.ToUniversalTime().Ticks / 100;
        }
    }
}
