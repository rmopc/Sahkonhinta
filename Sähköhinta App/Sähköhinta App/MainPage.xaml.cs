﻿using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Forms;
using Newtonsoft.Json;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using Xamarin.Essentials;

namespace Sähköhinta_App
{
    public partial class MainPage : TabbedPage
    {
        DateTime today = DateTime.Today;
        String todayHour = DateTime.Now.AddHours(-2).ToString("M/d/yyyy HH"); //muunnetaan CET-ajasta Suomen aikaan

        double taxPercentage = 1;
                
        public MainPage()
        {           
            InitializeComponent();
            GetJsonAsyncOC();
            statusField.IsVisible = false;
        }  
        
        async void GetJsonAsyncOC()
        {
            HttpClient httpClient = new HttpClient();
            
            var uri = new Uri("https://sahkotin.fi/prices");            
            string json = await httpClient.GetStringAsync(uri);

            var jsonObject = JObject.Parse(json);
            var prices = jsonObject["prices"];
            var jsonArray = JArray.Parse(prices.ToString());
            
            //DateTime startDateTime = DateTime.Today.AddHours(-2); //Tänään klo 00:00:00 mutta muutettuna Suomen aikaan
            DateTime startDateTime = DateTime.Today; //Tänään klo 00:00:00
            DateTime endDateTime = DateTime.Today.AddDays(1).AddTicks(-1); //Tänään klo 23:59:59

            DateTime startDateTimeTomorrow = DateTime.Today.AddDays(1); //Huomenna klo 00:00:00
            DateTime endDateTimeTomorrow = DateTime.Today.AddDays(2).AddTicks(-1); //Huomenna klo 23:59:59

            List<Price> pricelist = JsonConvert.DeserializeObject<List<Price>>(jsonArray.ToString());

            ObservableCollection<Price> pricedata = new ObservableCollection<Price>(pricelist);            

            //haetaan päivän hinnat jotta voidaan laskea keskiarvot ja näyttää ylin/alin
            pricelist = pricelist.Where(x => x.date >=startDateTime && x.date<= endDateTime).ToList();

            //haetaan huomisen hinnat jotta voidaan laskea keskiarvot ja näyttää ylin/alin
            var pricelistTomorrow = pricelist.Where(x => x.date >= startDateTimeTomorrow && x.date <= endDateTimeTomorrow).ToList();

            //Vuorokauden ylin, alin ja keskihinta sekä niiden asetus tekstikenttiin
            var dailyMax = pricelist.Max(x => x.value);
            highPrice.Text = (dailyMax / 10 * taxPercentage).ToString("F") + " c/kWh";

            var dailyMin = pricelist.Min(x => x.value);
            lowPrice.Text = (dailyMin / 10 * taxPercentage).ToString("F") + " c/kWh";
                                                                                                                                                  
            double dailyAvg = 0;
            dailyAvg = pricelist.Where(x => x.date >= startDateTime && x.date <= endDateTime).Average(x => x.value);
            avgPrice.Text = (dailyAvg / 10 * taxPercentage).ToString("F") + " c/kWh";

            //Huomisen ylin, alin ja keskihinta
            //var dailyMaxTomorrow = pricelistTomorrow.Max(x => x.value);
            //var dailyMinTomorrow = pricelistTomorrow.Min(x => x.value);

            //double dailyAvgTomorrow = 0;
            //dailyAvgTomorrow = pricelistTomorrow.Where(x => x.date >= startDateTimeTomorrow && x.date <= endDateTimeTomorrow).Average(x => x.value);


            foreach (var item in jsonArray)
            {                
                DateTime date;
                if (DateTime.TryParse(item["date"].ToString(), out date))
                {                    
                    string displayDate = date.ToString("M/d/yyyy HH");

                    //Tämänhetkinen hinta
                    if (displayDate.Contains(todayHour))
                    {
                        string price = item["value"].ToString();
                        double price2 = double.Parse(price);
                        priceFieldNow.Text = "Hinta nyt: " + (price2 / 10 * taxPercentage).ToString("F") + " c/kWh";
                    }

                    //Tarkistetaan samalla jos huomisen hinnat näkyy
                    if (date.ToShortDateString().Contains(today.AddDays(1).ToShortDateString()))
                    {
                        pricesTomorrowButton.IsEnabled = true;
                        
                        var rowsTomorrow = pricedata.Where(x => x.date >= startDateTimeTomorrow && x.date <= endDateTimeTomorrow)
                                                    .Select(x => new
                                                    {
                                                        x.date,
                                                        value = x.value / 10 * taxPercentage
                                                    });

                        priceListViewTomorrow.ItemsSource = rowsTomorrow;
                    }
                }
            }

            //Kaikki tämän päivän hinnat
            var rowsToday = pricedata.Where(x => x.date >= startDateTime && x.date <= endDateTime)
                                .Select(x => new
                                {
                                    x.date,
                                    value = x.value / 10 * taxPercentage
                                });

            priceListView.ItemsSource = rowsToday;
        }

        private void pricesTomorrowButton_Clicked(object sender, EventArgs e)
        {            
            //pricesTodayButton.IsVisible = true;
            //pricesTomorrowButton.IsVisible = false;
            priceFieldLabel.Text = "Hinnat huomenna";
            priceListView.IsVisible= false;
            priceListViewTomorrow.IsVisible = true;
        }

        private void pricesTodayButton_Clicked(object sender, EventArgs e)
        {
            //pricesTomorrowButton.IsVisible = true;
            //pricesTodayButton.IsVisible = false;
            priceFieldLabel.Text = "Hinnat tänään";
            priceListView.IsVisible = true;
            priceListViewTomorrow.IsVisible = false;
        }

        private void reloadButton_Clicked(object sender, EventArgs e)
        {
            priceFieldNow.Text = "Päivitetään...";
            //priceFieldToday.Text = "";
            GetJsonAsyncOC();  
        }

        private void tax00_Clicked(object sender, EventArgs e)
        {
            taxPercentage = 1;
            tax00.IsEnabled = false; 
            tax10.IsEnabled = true;
            tax24.IsEnabled = true;
            listLabel.Text = listLabelTomorrow.Text = "Hinnat alv 0%";
        }

        private void tax10_Clicked(object sender, EventArgs e)
        {
            
            taxPercentage = 1.10;
            tax00.IsEnabled = true;
            tax10.IsEnabled = false;
            tax24.IsEnabled = true;
            listLabel.Text = listLabelTomorrow.Text =  "Hinnat alv 10%";
        }

        private void tax24_Clicked(object sender, EventArgs e)
        {
            taxPercentage = 1.24;
            tax00.IsEnabled = true;
            tax10.IsEnabled = true;
            tax24.IsEnabled = false;
            listLabel.Text = listLabelTomorrow.Text = "Hinnat alv 24%";
        }
    }
}
