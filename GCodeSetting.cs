using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CncControlApp
{


    public class GCodeSetting
    {
        public int Id { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }
        public string KnownMeaning { get; set; }
    }

}
