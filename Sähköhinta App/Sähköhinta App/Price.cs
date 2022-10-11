using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sähköhinta_App
{
    public class Price
    {
        //[JsonProperty("id")]
        public string _id { get; set; }

        //[JsonProperty("date")]

        public DateTime date { get; set; }

        //[JsonProperty("value")]
        public double value { get; set; }

        //[JsonProperty("_v")]
        public int __v { get; set; }
    }

    public class RootObject
    {
        public List<Price> prices { get; set; }
    }
}
