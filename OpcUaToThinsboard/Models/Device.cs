using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpcUaToThinsboard.Models
{
    internal class Device
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string Token { get; set; }
        public Subscriptions? Subscriptions { get; set; }
        public List<History> Histories { get; set; } = [];
    }
}
