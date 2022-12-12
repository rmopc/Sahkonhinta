using System;
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
        //String todayHour = DateTime.Now.AddHours(-4).ToString("M/d/yyyy HH"); //muunnetaan CET-ajasta Suomen aikaan, ei toimi puhelimella enää oikein?
        String todayHour = DateTime.Now.ToString("M/d/yyyy HH"); //muunnetaan CET-ajasta Suomen aikaan

        double divider = 10;
        double taxPercentage = 1;
        //float tomorrowDivider = 10.0f;
                
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
            
            DateTime startDateTime = DateTime.Today; //Tänään klo 00:00:00
            DateTime endDateTime = DateTime.Today.AddDays(1).AddTicks(-1); //Tänään klo 23:59:59

            DateTime startDateTimeTomorrow = DateTime.Today.AddDays(1); //Huomenna klo 00:00:00
            DateTime endDateTimeTomorrow = DateTime.Today.AddDays(2).AddTicks(-1); //Huomenna klo 23:59:59

            List<Price> pricelist = JsonConvert.DeserializeObject<List<Price>>(jsonArray.ToString());
            ObservableCollection<Price> pricedata = new ObservableCollection<Price>(pricelist);            

            //haetaan päivän hinnat
            pricelist = pricelist.Where(x => x.date >=startDateTime && x.date<= endDateTime).ToList();

            //haetaan huomisen hinnat, ei käytössä vielä
            var pricelistTomorrow = pricelist.Where(x => x.date >= startDateTimeTomorrow && x.date <= endDateTimeTomorrow).ToList();

            //Vuorokauden korkein hinta
            var dailyMax = pricelist.Max(x => x.value);
            //var dailyMaxTomorrow = pricelistTomorrow.Max(x => x.value);
            highPrice.Text = (dailyMax/10 * taxPercentage).ToString("F") + " c/kWh";
            //if (isToday == false)
            //{
            //    highPrice.Text = (dailyMaxTomorrow / 10).ToString() + " c/kWh";
            //}            

            //Vuorokauden alin hinta
            var dailyMin = pricelist.Min(x => x.value);
            lowPrice.Text = (dailyMin/ 10 * taxPercentage).ToString("F") + " c/kWh";

            //Vuorokauden keskihinta                                                                                                                                                            
            double dailyAvg = 0;
            dailyAvg = pricelist.Where(x => x.date >= startDateTime && x.date <= endDateTime).Average(x => x.value);            
            avgPrice.Text = (dailyAvg/ 10 * taxPercentage).ToString("F") + " c/kWh";

            //Tämänhetkinen hinta
            foreach (var item in jsonArray)
            {
                string date = item["date"].ToString();
                string displayDate = DateTime.Parse(date).ToString("M/d/yyyy HH"); //muutetaan päivämäärä toiseen, yhtenäisempään string-muotoon
                string price = item["value"].ToString();
                double price2 = double.Parse(price);

                if (displayDate.ToString().Contains(todayHour))
                {
                    priceFieldNow.Text = "Hinta nyt: " + (price2 / 10 * taxPercentage).ToString("F") + " c/kWh";
                }

                //Tarkistetaan samalla jos huomisen hinnat näkyy
                if (date.Contains(today.AddDays(1).ToShortDateString()))
                {
                    pricesTomorrowButton.IsVisible = true;
                    var rowsTomorrow = from row in pricedata.Where(x => x.date >= startDateTimeTomorrow && x.date <= endDateTimeTomorrow)
                                       select row;

                    foreach (var row in rowsTomorrow)
                    {
                        decimal rowv = Convert.ToDecimal(row.value);
                        decimal result = rowv * 1 / 10;                        
                        row.value = Convert.ToDouble(result);
                    }

                    priceListViewTomorrow.ItemsSource = pricedata.Where(x => x.date >= startDateTimeTomorrow && x.date <= endDateTimeTomorrow);
                }
            }      

            //Kaikki tämän päivän hinnat
            var rows = from row in pricedata.Where(x => x.date >= startDateTime && x.date <= endDateTime)
                       select row;            

            foreach (var row in rows)
            {
                //decimal rowv = Convert.ToDecimal(row.value);
                //decimal result = rowv / 10;
                //row.value = (double)result;
                row.value = row.value / divider * taxPercentage;
            }
            priceListView.ItemsSource = pricedata.Where(x => x.date >= startDateTime && x.date <= endDateTime);

        }

        private void pricesTomorrowButton_Clicked(object sender, EventArgs e)
        {            
            pricesTodayButton.IsVisible = true;
            pricesTomorrowButton.IsVisible = false;
            priceFieldLabel.Text = "Hinnat huomenna";
            priceListView.IsVisible= false;
            priceListViewTomorrow.IsVisible = true;
        }

        private void pricesTodayButton_Clicked(object sender, EventArgs e)
        {
            pricesTomorrowButton.IsVisible = true;
            pricesTodayButton.IsVisible = false;
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
            listLabel.Text = "Hinnat alv 0%";
        }

        private void tax10_Clicked(object sender, EventArgs e)
        {
            
            taxPercentage = 1.10;
            tax00.IsEnabled = true;
            tax10.IsEnabled = false;
            tax24.IsEnabled = true;
            listLabel.Text = "Hinnat alv 10%";
        }

        private void tax24_Clicked(object sender, EventArgs e)
        {
            taxPercentage = 1.24;
            tax00.IsEnabled = true;
            tax10.IsEnabled = true;
            tax24.IsEnabled = false;
            listLabel.Text = "Hinnat alv 24%";
        }
    }
}
