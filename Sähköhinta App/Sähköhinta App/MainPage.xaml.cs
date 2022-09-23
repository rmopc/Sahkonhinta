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

        // voiko nää tehdä selkeämmin?
        StringBuilder sb = new StringBuilder();
        StringBuilder sb2 = new StringBuilder();
        StringBuilder sb3 = new StringBuilder();

        //DateTime today = DateTime.Today.ToLocalTime(); //Lokaali aikavyöhyke. addhours toimii tässä               
        //DateTime today = DateTime.Today.ToLocalTime().AddHours(15); //Lokaali aikavyöhyke, säädettävä tunteja               
        DateTime today = DateTime.Today;
        DateTime yesterday = DateTime.Today.AddDays(-1);           

        String todayHour = DateTime.Now.ToString("M/d/yyyy h");
        String todayHour2 = DateTime.Now.ToFormat24h();

        public MainPage()
        {           
            InitializeComponent();
            GetJsonAsync();
            //GetJsonAsyncModel();
            statusField.IsVisible = false;    
        }

        //Metodi jossa data luodaan stringbuilderiin
        public async void GetJsonAsync()
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
               
                var prices = jsonObject["prices"];
                var jsonArray = JArray.Parse(prices.ToString());

                foreach (var item in jsonArray)
                {
                    string date = item["date"].ToString();
                    //string trimmedDate = date.Substring(date.IndexOf(' ') + 1);   //siivotaan päivämäärä pois trimmaamalla kaikki ennen välilyöntiä, tarviiko tätä
                    //string displayDate = DateTime.Parse(trimmedDate).ToString("dd/MM/yyyy HH:mm"); //muutetaan päivämäärä string-muotoon
                    string price = item["value"].ToString();
                    double price2 = double.Parse(price) / 10;

                    //tämänhetkinen kellonaika
                    if (date.ToString().Contains(todayHour2))
                    {
                        //sb.Append(DateTime.Parse(date).ToString("HH:mm") + ",  " + price2.ToString("F") + " c/kWh" + "\n");
                        sb.Append(todayHour2 + ",  " + price2.ToString("F") + " c/kWh" + "\n");
                        //sb.Append("Testataan: " + todayHour2);
                    }

                    //kaikki tämän vuodokauden rivit                    
                    if (date.Contains(today.ToShortDateString()))
                    {
                        string startTime = DateTime.Parse(date).AddHours(1).ToString("HH:mm"); //koska JSON-datassa CET-ajat, lisätään yksi tunti
                        string endTime = DateTime.Parse(date).AddHours(2).ToString("HH:mm"); //päättymisaika on 1h alkamisajasta

                        sb2.Append("Klo " + startTime + "-" + endTime + ", hinta: " + price2.ToString("F") + " c/kWh" + "\n"); //muutetaan hinta string-muotoon ja pakotetaan 2 desimaalia
                    }

                    if (date.Contains(today.AddDays(1).ToShortDateString()))
                    {
                        pricesTomorrow.IsVisible = true;
                        string startTime = DateTime.Parse(date).AddHours(1).ToString("HH:mm"); //koska JSON-datassa CET-ajat, lisätään yksi tunti
                        string endTime = DateTime.Parse(date).AddHours(2).ToString("HH:mm"); //päättymisaika on 1h alkamisajasta

                        sb3.Append("Klo " + startTime + "-" + endTime + ", hinta: " + price2.ToString("F") + " c/kWh" + "\n"); //muutetaan hinta string-muotoon ja pakotetaan 2 desimaalia                        
                    }
                }

                priceFieldNow.Text = "Hinta nyt: " + "\n" + sb.ToString();
                priceFieldToday.Text =  sb2.ToString() + "\n" ;
                //statusField.Text = "Tiedot haettu onnistuneesti";
                //statusField.IsVisible = false;
            }
            else
            {
                await DisplayAlert("Virhe!", "Json-data ei ole saatavilla, tai siihen ei saatu yhteyttä", "OK");
                statusField.IsVisible = true;
                statusField.Text = "Virhe, ei yhteyttä?";
            }
        }

        //Metodi jossa data haetaan omaan modeliin
        async void GetJsonAsyncModel()
        {
            var uri = new Uri("https://pakastin.fi/hinnat/prices");
            //var uri = new Uri("https://api.jsonbin.io/v3/qs/632432bfa1610e63862d34d7");
            HttpClient httpClient = new HttpClient();
            var response = await httpClient.GetAsync(uri);

            if (response.IsSuccessStatusCode)
            {
                //statusField.Text = "Onnistui koodilla: " + response;
                var content = await response.Content.ReadAsStringAsync();
                string json = content.ToString();
                var jsonObject = JObject.Parse(json);
                
                var prices = jsonObject["prices"];
                var jsonArray = JArray.Parse(prices.ToString());

                foreach (var item in jsonArray)
                {
                    Price p = new Price();
                    string date = item["date"].ToString();
                    string price = item["value"].ToString();
                    //string trimmedDate = date.Substring(date.IndexOf(' ') + 1);
                    //string displayDate = DateTime.Parse(trimmedDate).ToString("dd/MM/yyyy HH:mm"); //muutetaan päivämäärä string-muotoon
                    p.date = DateTime.Parse(date);
                    p.value = double.Parse(price); // tähän tyssää jos int, "Input string was not in a correct format"
                    pricelist.Add(p);
                }

                //LINQ-kysely
                var eilen = pricelist.Where(p => p.date.ToString().Contains(today.ToShortDateString()));
                foreach (var dailyprice in eilen)
                {
                    //priceListView.ItemsSource = dailyprice.ToString();
                    priceFieldToday.Text = dailyprice.ToString();
                }

                //Find
                //List<Price> eiliset = pricelist.FindAll(p => p.date.ToString().Contains(DateTime.Today.ToString()));
                //foreach (var dailyprice in eiliset)
                //{
                //    //priceListView.ItemsSource = dailyprice.ToString();
                //    //priceFieldToday.Text = dailyprice.ToString(); // palauttaa app nimen ja listan nimen? tutkittava
                //}
                //priceListView.ItemsSource = eilen.ToString();
                //priceFieldToday.Text = eiliset.ToString();

                statusField.Text = "Tiedot haettu onnistuneesti";
            }
            else
            {
                await DisplayAlert("Virhe!", "Json-data ei ole saatavilla, tai siihen ei saatu yhteyttä", "OK");
                statusField.Text = "Virhe, ei yhteyttä?";
            }
        }

        private void pricesTomorrow_Clicked(object sender, EventArgs e)
        {
            pricesToday.IsVisible = true;
            pricesTomorrow.IsVisible = false;
            priceFieldLabel.Text = "Hinnat huomenna (ALV 0%)";
            priceFieldToday.Text = sb3.ToString() + "\n"; 
        }

        private void pricesToday_Clicked(object sender, EventArgs e)
        {
            pricesTomorrow.IsVisible = true;
            pricesToday.IsVisible = false;   
            priceFieldLabel.Text = "Hinnat tänään (ALV 0%)";
            priceFieldToday.Text = sb2.ToString() + "\n";
        }

        private void reloadButton_Clicked(object sender, EventArgs e)
        {
            priceFieldNow.Text = "Päivitetään...";
            priceFieldToday.Text = "";
            sb.Clear();
            sb2.Clear();
            sb3.Clear();
            GetJsonAsync();
        }
    }
}
