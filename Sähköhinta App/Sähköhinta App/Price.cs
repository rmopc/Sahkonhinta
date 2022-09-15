using System;
using System.Collections.Generic;
using System.Text;

namespace Sähköhinta_App
{
    public class Price
    {
        public string _id { get; set; }
        public DateTime date { get; set; }
        public double value { get; set; }
        public int __v { get; set; }
    }

    //public class Root
    //{
    //    public List<Price> prices { get; set; }
    //}
}
