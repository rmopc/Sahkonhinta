using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Forms;
using Newtonsoft.Json;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using Xamarin.Essentials;
using System.Threading.Tasks;

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

                        //Luodaan muuttuja joka sisältää kaikki huomisen vuorokaude tunnit           
                        var hoursTomorrow = Enumerable.Range(0, 23).Select(i => startDateTimeTomorrow.AddHours(i));

                        //Koska aikaa säädetään kahdella tunnilla eteenpäin, luodaan lisäksi muutuja joka sisältää vuorokauden kaksi ensimmäistä tuntia            
                        var hoursFirstTwoTomorrow = Enumerable.Range(-2, 2).Select(i => startDateTimeTomorrow.AddHours(i));

                        //Liitetään kummankin muuttujan tunnit yhteen            
                        hoursTomorrow = hoursTomorrow.Union(hoursFirstTwoTomorrow);

                        // Kaikki huomisen tunnit
                        var rowsTomorrow = pricedata.Where(x => hoursTomorrow.Contains(x.date))
                                                .Select(x => new
                                                {
                                                    date = x.date.AddHours(2),
                                                    value = x.value / 10 * taxPercentage
                                                });

                        priceListViewTomorrow.ItemsSource = rowsTomorrow;
                    }
                }
            }

            //Luodaan muuttuja joka sisältää kaikki vuodokauden tunnit           
            var hours = Enumerable.Range(0, 23).Select(i => startDateTime.AddHours(i));

            //Koska aikaa säädetään kahdella tunnilla eteenpäin, luodaan lisäksi muutuja joka sisältää vuorokauden kaksi ensimmäistä tuntia            
            var hoursFirstTwo = Enumerable.Range(-2, 2).Select(i => startDateTime.AddHours(i));

            //Liitetään kummankin muuttujan tunnit yhteen            
            hours = hours.Union(hoursFirstTwo);

            // Kaikki vuorokauden tunnit
            var rowsToday = pricedata.Where(x => hours.Contains(x.date))
                                    .Select(x => new
                                    {                                        
                                        date = x.date.AddHours(2),
                                        value = x.value / 10 * taxPercentage
                                    });

            priceListView.ItemsSource = rowsToday;
        }

        private void pricesTomorrowButton_Clicked(object sender, EventArgs e)
        {            
            priceFieldLabel.Text = "Hinnat huomenna";
            priceListView.IsVisible= false;
            priceListViewTomorrow.IsVisible = true;
        }

        private void pricesTodayButton_Clicked(object sender, EventArgs e)
        {
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

        private Task Wait()
        {
            return Task.Delay(500);
        }
        
        private async void tax_Clicked(object sender, EventArgs e)
        {
            Button button = (Button)sender;
            string buttonText = button.Text;
            settingsStatus.IsVisible = true;

            tax00.IsEnabled = true;
            tax10.IsEnabled = true;
            tax24.IsEnabled = true;
            button.IsEnabled = false;

            if (buttonText == "ALV 0%")
            {
                taxPercentage = 1;
                taxLabel.Text = "Kaikki hinnat alv 0%";
            }
            else if (buttonText == "ALV 10%")
            {
                taxPercentage = 1.10;
                taxLabel.Text = "Kaikki hinnat alv 10%";
            }
            else if (buttonText == "ALV 24%")
            {
                taxPercentage = 1.24;
                taxLabel.Text = "Kaikki hinnat alv 24%";
            }

            GetJsonAsyncOC();
            await Wait();
            settingsStatus.IsVisible = false;
            CurrentPage = Children.First(x => x.Title == "HINNAT");
        }
    }
}
