using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpcUaToThinsboard.Models
{
    internal class Subscriptions
    {
        public int Interval { get; set; } = 10000;
        public List<Tag> Tags { get; set; } = [];
    }
}
