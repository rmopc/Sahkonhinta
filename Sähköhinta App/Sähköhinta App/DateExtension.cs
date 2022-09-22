using System;
using System.Collections.Generic;
using System.Text;

namespace Sähköhinta_App
{
    public static class DateExtension
    {
        public static string ToFormat24h(this DateTime dt)
        {
            return dt.ToString("M/d/yyyy HH");
        }
    }
}
