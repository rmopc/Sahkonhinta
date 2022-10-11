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
using System.Collections.ObjectModel;
using System.Reflection;

namespace Sähköhinta_App
{
    public partial class MainPage : TabbedPage
    {
        StringBuilder sb = new StringBuilder();
       
        DateTime today = DateTime.Today;
        DateTime yesterday = DateTime.Today.AddDays(-1);           

        String todayHour = DateTime.Now.AddHours(-4).ToString("M/d/yyyy HH"); //muunnetaan CET-ajasta Suomen aikaan
                
        public MainPage()
        {           
            InitializeComponent();
            GetJsonAsyncOC();
            statusField.IsVisible = false;            
        }  
        
        async void GetJsonAsyncOC()
        {
            HttpClient httpClient = new HttpClient();
            
            var uri = new Uri("https://pakastin.fi/hinnat/prices");            
            string json = await httpClient.GetStringAsync(uri);

            var jsonObject = JObject.Parse(json);
            var prices = jsonObject["prices"];
            var jsonArray = JArray.Parse(prices.ToString());
            
            DateTime startDateTime = DateTime.Today; //Tänään klo 00:00:00
            DateTime endDateTime = DateTime.Today.AddDays(1).AddTicks(-1); //Tänään klo 23:59:59

            DateTime startDateTimeTomorrow = DateTime.Today.AddDays(1); //Huomenna klo 00:00:00
            DateTime endDateTimeTomorrow = DateTime.Today.AddDays(2).AddTicks(-1); //Huomenna klo 23:59:59

            List<Price> pricelist = JsonConvert.DeserializeObject<List<Price>>(jsonArray.ToString());
            ObservableCollection<Price> dataa = new ObservableCollection<Price>(pricelist);            

            //haetaan päivän hinnat
            pricelist = pricelist.Where(x => x.date >=startDateTime && x.date<= endDateTime).ToList();

            //haetaan huomisen hinnat
            var pricelistTomorrow = pricelist.Where(x => x.date >= startDateTimeTomorrow && x.date <= endDateTimeTomorrow).ToList();

            //Vuorokauden korkein hinta
            var dailyMax = pricelist.Max(x => x.value);
            highPrice.Text = (dailyMax/10).ToString() + " c/kWh";

            //Vuorokauden alin hinta
            var dailyMin = pricelist.Min(x => x.value);
            lowPrice.Text = (dailyMin/10).ToString() + " c/kWh";

            //Vuorokauden keskihinta                                                                                                                                                            
            double dailyAvg = 0;
            dailyAvg = pricelist.Where(x => x.date >= startDateTime && x.date <= endDateTime).Average(x => x.value);
            Console.WriteLine($"Today's average is: {dailyAvg}");
            avgPrice.Text = (dailyAvg/10).ToString("F") + " c/kWh";

            //Tämänhetkinen hinta
            foreach (var item in jsonArray)
            {
                string date = item["date"].ToString();
                string displayDate = DateTime.Parse(date).ToString("M/d/yyyy HH"); //muutetaan päivämäärä toiseen, yhtenäisempään string-muotoon
                string price = item["value"].ToString();
                double price2 = double.Parse(price);

                if (displayDate.ToString().Contains(todayHour))
                {
                    //ALV 24% asettaminen tämänhetkiseen hintaan
                    if (taxSwitch.IsToggled)
                    {
                        double price24 = price2 * 1.24;
                        priceFieldNow.Text = "Hinta nyt: " + (price24 / 10).ToString("F") + " c/kWh";
                    }
                    else
                    {
                        priceFieldNow.Text = "Hinta nyt: " + (price2 / 10).ToString("F") + " c/kWh";
                    }
                }

                //Tarkistetaan samalla jos huomisen hinnat näkyy
                if (date.Contains(today.AddDays(1).ToShortDateString()))
                {
                    pricesTomorrowButton.IsVisible = true;
                    priceListViewTomorrow.ItemsSource = dataa.Where(x => x.date >= startDateTimeTomorrow && x.date <= endDateTimeTomorrow);
                }
            }
            

            //ALV 24% asettaminen muihin hintoihin
            if (taxSwitch.IsToggled)
            {                
                var rows24 = from row in dataa.Where(x => x.date >= startDateTime && x.date <= endDateTime)
                           select row;

                foreach (var row in rows24)
                {
                    row.value = row.value * 1.24;
                }

                var dailyMax24 = pricelist.Max(x => x.value);
                highPrice.Text = dailyMax24.ToString("F") + " c/kWh";

                var dailyMin24 = pricelist.Min(x => x.value);
                lowPrice.Text = dailyMin24.ToString("F") + " c/kWh";

                double dailyAvg24 = 0;
                dailyAvg24 = pricelist.Where(x => x.date >= startDateTime && x.date <= endDateTime).Average(x => x.value);
                Console.WriteLine($"Today's average is: {dailyAvg24}");
                avgPrice.Text = dailyAvg24.ToString("F") + " c/kWh";
            }

            //Kaikki tämän päivän hinnat
            var rows = from row in dataa.Where(x => x.date >= startDateTime && x.date <= endDateTime)
                       select row;

            foreach (var row in rows)
            {
                row.value = row.value/10;
            }
            priceListView.ItemsSource = dataa.Where(x => x.date >= startDateTime && x.date <= endDateTime);

        }

        private void pricesTomorrowButton_Clicked(object sender, EventArgs e)
        {
            pricesTodayButton.IsVisible = true;
            pricesTomorrowButton.IsVisible = false;
            if (taxSwitch.IsToggled)
            {
                priceFieldLabel.Text = "Hinnat huomenna";
            }
            else
            {
                priceFieldLabel.Text = "Hinnat huomenna";
            }
            priceListView.IsVisible= false;
            priceListViewTomorrow.IsVisible = true;
        }

        private void pricesTodayButton_Clicked(object sender, EventArgs e)
        {
            pricesTomorrowButton.IsVisible = true;
            pricesTodayButton.IsVisible = false;
            if (taxSwitch.IsToggled)

            {
                priceFieldLabel.Text = "Hinnat tänään";
            }
            else
            {
                priceFieldLabel.Text = "Hinnat tänään";
            }
            priceListView.IsVisible = true;
            priceListViewTomorrow.IsVisible = false;
        }

        private void reloadButton_Clicked(object sender, EventArgs e)
        {
            priceFieldNow.Text = "Päivitetään...";
            //priceFieldToday.Text = "";
            sb.Clear(); //poistoon?
            GetJsonAsyncOC();  
        }
    }
}
