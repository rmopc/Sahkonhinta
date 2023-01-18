using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sahkonhinta_App
{
    public class Price
    {
        public string _id { get; set; }

        public DateTime date { get; set; }

        public double value { get; set; }

        public int __v { get; set; }
    }

    public class RootObject
    {
        public List<Price> prices { get; set; }
    }
}
