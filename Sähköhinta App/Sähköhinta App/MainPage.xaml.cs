using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace Sähköhinta_App
{

    
    public partial class MainPage : ContentPage
    {

        List<Price> pricelist = new List<Price>();  

        //DateTime today = DateTime.Today.ToLocalTime(); //Lokaali aikavyöhyke. addhours toimii tässä               
        //DateTime today = DateTime.Today.ToLocalTime().AddHours(15); //Lokaali aikavyöhyke, säädettävä tunteja               
        DateTime today = DateTime.Today;
        DateTime yesterday = DateTime.Today.ToLocalTime().AddDays(-1);

        //String nowTime = today.ToShortDateString();

        DateTime today2 = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day); //tässä toimii nyt kuluva päivä, mutta heti vuorokauden alusta
        String today3 = DateTime.Now.ToString("MM/dd/yyyy");
        String dateHour = DateTime.Now.ToString("dd/MM/yyyy HH:mm");     

        public MainPage()
        {
            InitializeComponent();
            GetJsonAsync();
            //Task.Run(() => GetJsonAsync());
            //Task.Run(async () => { await GetJsonAsync(); });
            statusField.Text = "Nyt on: " + today;
        }

       async void GetJsonAsync()
        {
            var uri = new Uri("https://pakastin.fi/hinnat/prices");
            //var uri = new Uri("https://api.jsonbin.io/v3/qs/6321dfb4a1610e63862adec3");
            HttpClient httpClient = new HttpClient();
            var response = await httpClient.GetAsync(uri);

            if (response.IsSuccessStatusCode)
            {
                //statusField.Text = "Onnistui koodilla: " + response;
                var content = await response.Content.ReadAsStringAsync();
                string json = content.ToString();
                var jsonObject = JObject.Parse(json);
                
                //var id = jsonObject["_id"]; // onko turha?
                var prices = jsonObject["prices"];
                var jsonArray = JArray.Parse(prices.ToString());

                StringBuilder sb = new StringBuilder();
                StringBuilder sb2 = new StringBuilder();

                //LISTA OMA CLASS
                //foreach (var item in jsonArray)
                //{
                //    Price p = new Price();
                //    string date = item["date"].ToString();
                //    string price = item["value"].ToString();
                //    p.date = DateTime.Parse(date);
                //    p.value = int.Parse(price);
                //    pricelist.Add(p);
                //}

                //STRINGBUILDER
                foreach (var item in jsonArray)
                {
                    string date = item["date"].ToString();
                    //string trimmedDate = date.Substring(date.IndexOf(' ') + 1);   //siivotaan päivämäärä pois trimmaamalla kaikki ennen välilyöntiä, tarviiko tätä
                    //string displayDate = DateTime.Parse(trimmedDate).ToString("dd/MM/yyyy HH:mm"); //muutetaan päivämäärä string-muotoon
                    string price = item["value"].ToString();
                    //double price2 = int.Parse(price) / 1000; //jatka tästä!

                    //tämänhetkinen kellonaika
                    if (date.Contains(today.ToString())) //tähän voi määritellä Datetime-arvoja tai hipsuissa kenoviivoin päivämäärän, tutkittava lisää. 
                    {
                        //Jos tämän lisää ifin ulkopuolelle, antaa koko listan
                        sb.Append(DateTime.Parse(date).ToString("HH:mm") + ", hinta: " + price + " €/MWh" + "\n");
                    }

                    //kaikki tämän vuodokauden rivit
                        //if (date.Contains("09/14/2022"))
                        if (date.Contains(today.ToShortDateString()))
                        {
                            string startTime = DateTime.Parse(date).AddHours(1).ToString("HH:mm"); //koska JSON-datassa CET-ajat, lisätään yksi tunti
                            string endTime = DateTime.Parse(date).AddHours(2).ToString("HH:mm"); //päättymisaika on 1h alkamisajasta

                            sb2.Append("Klo " + startTime + "-" + endTime + ", hinta: " + price + " €/MWh" + "\n");
                        }
                }

                //JATKOA OMA CLASS
                //var eilen = pricelist.Where(p => p.date == DateTime.Today);

                //List<Price> eiliset = pricelist.FindAll(p => p.date == DateTime.Today);

                //priceListView.ItemsSource = eiliset;
                //priceFieldToday.Text = eilen.ToString();


                //JATKOA STRINGBUILDER
                priceFieldNow.Text = "Hinta nyt: " + "\n" + sb.ToString();
                priceFieldToday.Text = "Hinnat tänään: " + "\n" + sb2.ToString();
                //testField.Text = "Nyt on: " + DateTime.Now.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                statusField.Text = "Tiedot haettu onnistuneesti";
            }
            else
            {
                await DisplayAlert("Virhe!", "Json-data ei ole saatavilla, tai siihen ei saatu yhteyttä", "OK");
                statusField.Text = "Virhe, ei yhteyttä?";
            }
        }

        //private async void checkPrices_Clicked(object sender, EventArgs e)
        //{
        //    await GetJsonAsync();
        //}


        //public List<Price> GetPrices()
        //{
        //    var priceList = new List<Price>();

        //    using (var httpClient = new HttpClient())
        //    {
        //        var uri = new Uri("https://pakastin.fi/hinnat/prices");
        //        //HttpClient httpClient = new HttpClient();

        //        var response = httpClient.GetAsync(uri).Result;
        //        var responseContent = response.Content;
        //        var responseString = responseContent.ReadAsStringAsync().Result;

        //        dynamic prices = JArray.Parse(responseString) as JArray;

        //        foreach (var pr in prices)
        //        {
        //            Price price = pr.ToObject<Price>();
        //            priceList.Add(price);
        //        }
        //    }

        //    return priceList;

    }
}
