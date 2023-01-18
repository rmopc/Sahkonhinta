using System;
using System.Collections.Generic;
using System.Text;

namespace Sahkonhinta_App
{
    public static class DateExtension
    {
        //Toistaiseksi tälle ei ole käyttöä, mutta pidetään varmuuden vuoksi tallessa
        public static string ToFormat24h(this DateTime dt)
        {
            return dt.ToString("M/d/yyyy H:00"); //tässä muodossa antaa vain yhdelle tunnille hinnan, testattu aamupäivästä klo 10, ei toimi enää iltapäivästä
            //return dt.ToString("M/d/yyyy h:00");  //tässä muodossa antaa kaksi aikaa            
        }
    }
}
