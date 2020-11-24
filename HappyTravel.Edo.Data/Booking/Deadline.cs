using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace HappyTravel.Edo.Data.Booking
{
    public class Deadline
    {
        // EF constructor
        private Deadline() {}

        [JsonConstructor]
        public Deadline(DateTime? date, List<CancellationPolicy> policies, List<string> remarks = null)
        {
            Date = date;
            Policies = policies ?? new List<CancellationPolicy>();
            Remarks = remarks ?? new List<string>(0);
        }
        
        public DateTime? Date { get; set; }
        public List<CancellationPolicy> Policies { get; set; }
        public List<string> Remarks { get; set; }
    }
}