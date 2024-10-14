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
using System.Text.RegularExpressions;
using Microcharts;
using SkiaSharp;

namespace Sahkonhinta_App
{
    public partial class MainPage : TabbedPage
    {
        DateTime today = DateTime.Today;

        // Haetaan oikea aikavyöhyke, käytetään listaamaan vuorokauden tunteja alempana
        TimeZoneInfo localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

        double taxPercentage;
        double spotProvision;
        JObject jsonObject = null;

        List<Price> pricelist;
        List<Price> pricelistTomorrow;

        private double _startScale = 1;
        private double _currentScale = 1;

        public MainPage()
        {           
            InitializeComponent();

            // Ladataan veroprosentti
            if (Preferences.ContainsKey("TaxPercentage"))
            {
                taxPercentage = Preferences.Get("TaxPercentage", 1.10);
            }
            else
            {
                // Asetetaan oletus-veroprosentti, mikäli sitä ei ole tallennettu tai jos se on tyhjä
                taxPercentage = 1.0;
            }

            //Ladataan spot-provisio
            if (Preferences.ContainsKey("SpotProvision"))
            {
                spotProvision = Preferences.Get("SpotProvision", 0.0);
            }
            else
            {
                //Asetetaan oletus-spot-provisio, mikäli sitä ei ole tallennettu tai jos se on tyhjä
                spotProvision = 0.0;
            }

            //Asetetaan spot-provision syöttökenttään placeholder-arvo, joka on sama kuin aiemmin syötetty arvo
            spotInputField.Placeholder = spotProvision.ToString("0.00");

            UpdateTaxLabel();
            FetchJsonData();
            statusField.IsVisible = false;
        }

        async void FetchJsonData()
        {
            HttpClient httpClient = new HttpClient();
            var uri = new Uri("https://sahkotin.fi/prices");

            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(uri);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                jsonObject = JObject.Parse(responseBody);
                UpdateUIWithData();
            }

            catch (HttpRequestException e)
            {
                await DisplayAlert("Virhe yhteydessä", e.Message.ToString(), "OK");
            }
        }

        void UpdateUIWithData()
        {
            if (jsonObject == null)
                return;

            var prices = jsonObject["prices"];
            var jsonArray = JArray.Parse(prices.ToString());

            DateTime startDateTime = DateTime.Today; //Tänään klo 00:00:00                     
            DateTime endDateTime = DateTime.Today.AddDays(1).AddTicks(-1); //Tänään klo 23:59:59            
            DateTime startDateTimeTomorrow = DateTime.Today.AddDays(1); //Huomenna klo 00:00:00
            DateTime endDateTimeTomorrow = DateTime.Today.AddDays(2).AddTicks(-1); //Huomenna klo 23:59:59
            DateTime date = DateTime.MinValue;

            string todayHour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone).ToString("M/d/yyyy HH");

            List<Price> pricelist = JsonConvert.DeserializeObject<List<Price>>(jsonArray.ToString());
            List<Price> pricelistTomorrow = JsonConvert.DeserializeObject<List<Price>>(jsonArray.ToString());
            ObservableCollection<Price> pricedata = new ObservableCollection<Price>(pricelist);


            ///////////////////////////////////////////////////
            /////           KULUVAN PÄIVÄN HINNAT           /////
            //////////////////////////////////////////////////

            //Haetaan kaikki kuluvan päivän hinnat listalle laskentaa varten
            pricelist = jsonArray.Where(x => TimeZoneInfo.ConvertTimeFromUtc(DateTime.Parse(x["date"].ToString()), localTimeZone) >= startDateTime &&
                                                                      TimeZoneInfo.ConvertTimeFromUtc(DateTime.Parse(x["date"].ToString()), localTimeZone) <= endDateTime)
                                 .Select(x => new Price
                                 {
                                     date = DateTime.Parse(x["date"].ToString()),
                                     value = (double)x["value"]
                                 })
                                 .ToList();

            //Vuorokauden ylin, alin ja keskihinta sekä niiden asetus tekstikenttiin
            var dailyMax = pricelist.Max(x => x.value);
            highPrice.Text = (dailyMax / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            var dailyMin = pricelist.Min(x => x.value);
            lowPrice.Text = (dailyMin / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            double dailyAvg = pricelist.Average(x => x.value);
            avgPrice.Text = (dailyAvg / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            //Haetaan kuluvan tunnin hinta
            foreach (var item in jsonArray)
            {
                if (DateTime.TryParse(item["date"].ToString(), out date))
                {
                    date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                    DateTime localDate = TimeZoneInfo.ConvertTimeFromUtc(date, localTimeZone);

                    // Tämänhetkinen, eli kuluvan tunnin hinta
                    if (localDate.ToString("M/d/yyyy HH") == todayHour)
                    {
                        string price = item["value"].ToString();
                        double price2 = double.Parse(price);
                        priceFieldNow.Text = "Hinta nyt: " + (price2 / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";
                    }
                }
            }

            //Luodaan muuttuja joka sisältää kaikki tämän vuodokauden tunnit
            //Tätä säädetty siirryttäessä kesäaikaan keväällä 2024, otettava tarkasteluun talviajan myötä
            //Samalla kommentoitu allaolevia rivejä pois, sillä tuntien manuaalista säätöä ei enää tehdä
            //Vaan aikavyöhyke määritellään jo rivillä 20
            var hours = Enumerable.Range(-3, 24).Select(i => startDateTime.AddHours(i));

            ////Koska aikaa säädetään kahdella tunnilla eteenpäin, luodaan lisäksi muutuja joka sisältää tämän vuorokauden kaksi ensimmäistä tuntia            
            //var hoursFirstTwo = Enumerable.Range(-2, 2).Select(i => startDateTime.AddHours(i));

            ////Liitetään kummankin muuttujan tunnit yhteen            
            //hours = hours.Union(hoursFirstTwo);

            // Kaikki kuluvan vuorokauden tunnit
            var rowsToday = pricedata.Where(x => hours.Contains(x.date))
                                    .Select(x => new
                                    {
                                        date = TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone),
                                        value = x.value / 10 * taxPercentage + spotProvision
                                    });

            priceListView.ItemsSource = rowsToday;


            ///////////////////////////////////////////////////
            /////           HUOMISEN HINNAT                       /////
            //////////////////////////////////////////////////

            //Tarkistetaan jos huomisen hinnat näkyy
            if (date.ToShortDateString().Contains(today.AddDays(1).ToShortDateString()))
            {
                pricesTomorrowButton.IsEnabled = true;

                //Luodaan muuttuja joka sisältää kaikki huomisen vuorokauden tunnit
                //Tätä säädetty siirryttäessä kesäaikaan keväällä 2024, otettava tarkasteluun talviajan myötä
                //Samalla kommentoitu allaolevia rivejä pois, sillä tuntien manuaalista säätöä ei enää tehdä
                var hoursTomorrow = Enumerable.Range(-3, 24).Select(i => startDateTimeTomorrow.AddHours(i));

                ////Koska aikaa säädetään kahdella tunnilla eteenpäin, luodaan lisäksi muutuja joka sisältää vuorokauden kaksi ensimmäistä tuntia            
                //var hoursFirstTwoTomorrow = Enumerable.Range(-2, 2).Select(i => startDateTimeTomorrow.AddHours(i));

                ////Liitetään kummankin muuttujan tunnit yhteen            
                //hoursTomorrow = hoursTomorrow.Union(hoursFirstTwoTomorrow);

                // Kaikki huomisen tunnit
                var rowsTomorrow = pricedata.Where(x => hoursTomorrow.Contains(x.date))
                                        .Select(x => new
                                        {
                                            date = TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone),
                                            value = x.value / 10 * taxPercentage + spotProvision
                                        });

                priceListViewTomorrow.ItemsSource = rowsTomorrow;
            }

            //Haetaan kaikki huomisen hinnat listalle laskentaa varten
            pricelistTomorrow = jsonArray.Where(x => TimeZoneInfo.ConvertTimeFromUtc(DateTime.Parse(x["date"].ToString()), localTimeZone) >= startDateTimeTomorrow &&
                                                     TimeZoneInfo.ConvertTimeFromUtc(DateTime.Parse(x["date"].ToString()), localTimeZone) <= endDateTimeTomorrow)
                                         .Select(x => new Price
                                         {
                                             date = DateTime.Parse(x["date"].ToString()),
                                             value = (double)x["value"]
                                         })
                                         .ToList();

            //Huomisen ylin, alin ja keskihinta sekä niiden asetus tekstikenttiin
            var dailyMaxTomorrow = pricelistTomorrow.Max(x => x.value);
            highPriceTomorrow.Text = (dailyMaxTomorrow / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            var dailyMinTomorrow = pricelistTomorrow.Min(x => x.value);
            lowPriceTomorrow.Text = (dailyMinTomorrow / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            double dailyAvgTomorrow = pricelistTomorrow.Average(x => x.value);
            avgPriceTomorrow.Text = (dailyAvgTomorrow / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            //Taulukon/graafin muodostus
            var chart = GenerateDailyPriceChart(pricelist);
            dailyPriceChart.Chart = chart;
        }

        private Chart GenerateDailyPriceChart(List<Price> prices)
        {
            var entries = new List<ChartEntry>();
            var colors = new[]
            {
                SKColor.Parse("#266489"),
                SKColor.Parse("#68B9C0"),
                SKColor.Parse("#90D585"),
                SKColor.Parse("#F3C151"),
                SKColor.Parse("#F37F64"),
                SKColor.Parse("#424856"),
                SKColor.Parse("#8F97A4"),
                SKColor.Parse("#DAC096"),
                SKColor.Parse("#76846E"),
                SKColor.Parse("#DABFAF"),
                SKColor.Parse("#A65B69"),
                SKColor.Parse("#97A69D")
            };

            if (prices == null || prices.Count == 0)
            {
                return new LineChart
                {
                    Entries = new List<ChartEntry>(),
                    LabelTextSize = 20f,
                    LabelOrientation = Orientation.Horizontal,
                    ValueLabelOrientation = Orientation.Horizontal,
                    BackgroundColor = SKColors.Transparent
                };
            }

            for (int i = 0; i < prices.Count; i++)
            {
                var price = prices[i];
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(price.date, localTimeZone);
                entries.Add(new ChartEntry((float)(price.value / 10 * taxPercentage + spotProvision))
                {
                    Label = i % 2 == 0 ? localTime.ToString("HH") : "",
                    ValueLabel = i % 2 == 0 ? ((float)(price.value / 10 * taxPercentage + spotProvision)).ToString("F1") : "",
                    Color = colors[i % colors.Length]
                });
            }

            return new LineChart
            {
                Entries = entries,
                LabelTextSize = 40f, // Increased for better visibility when zoomed
                LabelOrientation = Orientation.Horizontal,
                ValueLabelOrientation = Orientation.Horizontal,
                //BackgroundColor = SKColors.Transparent,
                LineSize = 8f, // Increase line thickness for better visibility when zoomed
                PointSize = 20f // Increase point size for better visibility when zoomed
            };
        }

        private void pricesTomorrowButton_Clicked(object sender, EventArgs e)
        {
            var chartTomorrow = GenerateDailyPriceChart(pricelistTomorrow);
            dailyPriceChart.Chart = chartTomorrow;

            priceFieldLabel.Text = "Hinnat huomenna";

            avgLabel.IsVisible = false;
            avgPrice.IsVisible = false;
            countedPricesToday.IsVisible = false;
            priceListView.IsVisible= false;
            pricesTomorrowButton.IsEnabled=false;

            avgLabelTomorrow.IsVisible=true;
            avgPriceTomorrow.IsVisible=true;
            countedPricesTomorrow.IsVisible=true;
            priceListViewTomorrow.IsVisible = true;
            pricesTodayButton.IsEnabled = true;
        }

        private void pricesTodayButton_Clicked(object sender, EventArgs e)
        {
            var chartToday = GenerateDailyPriceChart(pricelist);
            dailyPriceChart.Chart = chartToday;

            priceFieldLabel.Text = "Hinnat tänään";

            avgLabel.IsVisible = true;
            avgPrice.IsVisible = true;
            countedPricesToday.IsVisible = true;
            priceListView.IsVisible = true;
            pricesTomorrowButton.IsEnabled = true;

            avgLabelTomorrow.IsVisible = false;
            avgPriceTomorrow.IsVisible = false;
            countedPricesTomorrow.IsVisible = false;
            priceListViewTomorrow.IsVisible = false;
            pricesTodayButton.IsEnabled=false;
        }

        private void reloadButton_Clicked(object sender, EventArgs e)
        {
            priceFieldNow.Text = "Päivitetään...";
            //priceFieldToday.Text = "";
            FetchJsonData();
        }

        private Task Wait()
        {
            return Task.Delay(500);
        }

        private void UpdateTaxLabel()
        {
            if (spotProvision != 0)
            {
                taxLabel.Text = "Kaikki hinnat alv " + (taxPercentage - 1) * 100 + "%, sis. spot-provision " + spotProvision + " c/kWh";
            }
            else
            {
                taxLabel.Text = "Kaikki hinnat alv " + (taxPercentage - 1) * 100 + "%";
            }
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
            }
            else if (buttonText == "ALV 10%")
            {
                taxPercentage = 1.10;
            }
            else if (buttonText == "ALV 24%")
            {
                taxPercentage = 1.24;
            }

            // Tallennetaan asetettu veroprosentti, jotta se säilyy vaikka sovellus suljetaan 
            Preferences.Set("TaxPercentage", taxPercentage);

            UpdateUIWithData();
            await Wait();
            settingsStatus.IsVisible = false;
            UpdateTaxLabel();
            CurrentPage = Children.First(x => x.Title == "HINNAT");
        }

        private async void OnEntryUnfocused(object sender, FocusEventArgs e)
        {
            settingsStatus.IsVisible = true;
            double parsedValue;
            if (!double.TryParse(((Entry)sender).Text, out parsedValue))
            {
                ((Entry)sender).Text = "";
                await Application.Current.MainPage.DisplayAlert("Virhe", "Anna numeroarvot kahden desimaalin tarkkuudella", "OK");
            }
            else
            {
                if (!Regex.IsMatch(((Entry)sender).Text, @"[0-9]+(\.[0-9]*)?$"))
                {
                    ((Entry)sender).Text = "";
                    await Application.Current.MainPage.DisplayAlert("Virhe", "Anna numeroarvot kahden desimaalin tarkkuudella", "OK");
                }
                else
                {
                    spotProvision = double.Parse(((Entry)sender).Text);

                    // Tallennetaan asetettu spot-provisio, jotta se säilyy vaikka sovellus suljetaan 
                    Preferences.Set("SpotProvision", spotProvision);

                    UpdateUIWithData();
                    await Wait();
                    settingsStatus.IsVisible = false;
                    UpdateTaxLabel();
                    CurrentPage = Children.First(x => x.Title == "HINNAT");
                }
            }
        }

        private void OnChartPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            switch (e.Status)
            {
                case GestureStatus.Started:
                    // Store the current scale when the gesture begins
                    _currentScale = dailyPriceChart.Scale;
                    _startScale = _currentScale;
                    break;

                case GestureStatus.Running:
                    // Calculate the new scale based on the pinch gesture
                    _currentScale += (e.Scale - 1) * _startScale;
                    _currentScale = Math.Max(1, _currentScale); // Ensure we don't zoom out too far

                    // Apply the new scale
                    dailyPriceChart.Scale = _currentScale;

                    // Adjust the ChartView size based on the new scale
                    dailyPriceChart.WidthRequest = 1000 * _currentScale;
                    dailyPriceChart.HeightRequest = 400 * _currentScale;
                    break;

                case GestureStatus.Completed:
                    // The gesture has completed, you can add any finalization logic here
                    break;
            }
        }
    }
}
