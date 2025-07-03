using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpcUaToThinsboard.Models
{
    internal class History
    {
        public required string Name { get; set; }
        public required string NodeId { get; set; }
        public string? CheckCron { get; set; }
        public string? HistoryType { get; set; } // daily, hourly, monthly
    }
}
