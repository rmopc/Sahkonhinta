﻿using System;
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

        string sb3 { get; set; }

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
            //GetJsonAsyncModel();
            statusField.Text = "Nyt on: " + today;
        }

        //Metodi jossa data luodaan stringbuilderiin
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
                StringBuilder sb3 = new StringBuilder();

                foreach (var item in jsonArray)
                {
                    string date = item["date"].ToString();
                    //string trimmedDate = date.Substring(date.IndexOf(' ') + 1);   //siivotaan päivämäärä pois trimmaamalla kaikki ennen välilyöntiä, tarviiko tätä
                    //string displayDate = DateTime.Parse(trimmedDate).ToString("dd/MM/yyyy HH:mm"); //muutetaan päivämäärä string-muotoon
                    string price = item["value"].ToString();
                    double price2 = double.Parse(price) / 10;

                    //tämänhetkinen kellonaika
                    if (date.Contains(today.ToString())) //tähän voi määritellä Datetime-arvoja tai hipsuissa kenoviivoin päivämäärän, tutkittava lisää. 
                    {
                        //Jos tämän lisää ifin ulkopuolelle, antaa koko listan
                        sb.Append(DateTime.Parse(date).ToString("HH:mm") + ", hinta: " + price2.ToString("F") + " c/kWh" + "\n");
                    }

                    //kaikki tämän vuodokauden rivit
                        //if (date.Contains("09/14/2022"))
                        if (date.Contains(today.ToShortDateString()))
                        {
                            string startTime = DateTime.Parse(date).AddHours(1).ToString("HH:mm"); //koska JSON-datassa CET-ajat, lisätään yksi tunti
                            string endTime = DateTime.Parse(date).AddHours(2).ToString("HH:mm"); //päättymisaika on 1h alkamisajasta

                            sb2.Append("Klo " + startTime + "-" + endTime + ", hinta: " + price2.ToString("F") + " c/kWh" + "\n"); //muutetaan hinta string-muotoon ja pakotetaan 2 desimaalia
                        }

                    if (date.Contains(today.AddDays(1).ToShortDateString()))
                    {
                        pricesTomorrow.IsEnabled = true;
                        string startTime = DateTime.Parse(date).AddHours(1).ToString("HH:mm"); //koska JSON-datassa CET-ajat, lisätään yksi tunti
                        string endTime = DateTime.Parse(date).AddHours(2).ToString("HH:mm"); //päättymisaika on 1h alkamisajasta

                        sb3.Append("Klo " + startTime + "-" + endTime + ", hinta: " + price2.ToString("F") + " c/kWh" + "\n"); //muutetaan hinta string-muotoon ja pakotetaan 2 desimaalia                        
                    }
                }

                priceFieldNow.Text = "Hinta nyt: " + "\n" + sb.ToString();
                priceFieldToday.Text =  sb2.ToString() + "\n" ;
                statusField.Text = "Tiedot haettu onnistuneesti";
            }
            else
            {
                await DisplayAlert("Virhe!", "Json-data ei ole saatavilla, tai siihen ei saatu yhteyttä", "OK");
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

        private void pricesTomorrow_Clicked( object sender, EventArgs e)
        {
            priceFieldLabel.Text = "Hinnat huomenna (ALV 0%)";
            priceFieldToday.Text = sb3.ToString() + "\n";
        }
    }
}
