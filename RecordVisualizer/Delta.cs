using System;
using System.Collections.Generic;

namespace RecordVisualizer
{
    using Pixel = ValueTuple<short, short, byte>;

    class Delta
    {
        public DateTime DateTime { get; set; }
        public List<Pixel> Pixels { get; set; }
    }
}
