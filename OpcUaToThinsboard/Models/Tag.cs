using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpcUaToThinsboard.Models
{
    public class Tag
    {
        public required string Name { get; set; }
        public required string NodeId { get; set; }
    }

}
