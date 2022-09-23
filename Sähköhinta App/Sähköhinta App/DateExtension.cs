using System;
using System.Collections.Generic;
using System.Text;

namespace Sähköhinta_App
{
    public static class DateExtension
    {
        public static string ToFormat24h(this DateTime dt)
        {
            return dt.ToString("M/d/yyyy H:00"); //tässä muodossa toimii nyt niin että antaa vain yhdelle tunnille hinnan, testattu aamupäivästä klo 10
        }
    }
}
