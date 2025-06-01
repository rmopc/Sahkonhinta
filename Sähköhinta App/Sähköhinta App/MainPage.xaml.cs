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
        bool isChartVisible = false;
        bool isShowingTomorrow = false;
        dynamic currentPriceDataToday = null;
        dynamic currentPriceDataTomorrow = null;

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
            statusField.IsVisible = false;

            // Delay the data fetch to happen after page is displayed
            Device.BeginInvokeOnMainThread(async () => {
                await Task.Delay(100); // Small delay to ensure UI is ready
                await FetchJsonDataAsync();
            });
        }

        async Task FetchJsonDataAsync()
        {
            try
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30); // Increase timeout

                    var uri = new Uri("https://sahkotin.fi/prices");

                    HttpResponseMessage response = await httpClient.GetAsync(uri);
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();

                    // Check if response is reasonably sized
                    if (string.IsNullOrEmpty(responseBody) || responseBody.Length > 10000000) // 10MB limit
                    {
                        await DisplayAlert("Virhe datassa", "Palvelimen vastaus on virheellinen tai liian suuri.", "OK");
                        return;
                    }

                    try
                    {
                        // Use try-catch specifically for JSON parsing
                        jsonObject = JObject.Parse(responseBody);
                        if (jsonObject != null && jsonObject["prices"] != null)
                        {
                            UpdateUIWithData();
                        }
                        else
                        {
                            await DisplayAlert("Virhe datassa", "Palvelin palautti virheellisen datan formaatin.", "OK");
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        // Log the first part of the response for debugging
                        string truncatedResponse = responseBody.Length <= 100
                            ? responseBody
                            : responseBody.Substring(0, 100) + "...";

                        Console.WriteLine($"JSON Error: {jsonEx.Message}");
                        Console.WriteLine($"Response start: {truncatedResponse}");

                        await DisplayAlert("Virhe datan käsittelyssä",
                                          "Palvelimen vastausta ei voitu käsitellä: " + jsonEx.Message,
                                          "OK");
                    }
                }
            }
            catch (Exception e)
            {
                await DisplayAlert("Virhe", e.Message, "OK");
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
            pricelist = pricelist.Where(x => TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone) >= startDateTime &&
                                                                    TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone) <= endDateTime).ToList();            

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

            // Kesä-/talviaika
            bool isDstToday = localTimeZone.IsDaylightSavingTime(startDateTime);

            //Luodaan muuttuja joka sisältää kaikki tämän vuodokauden tunnit
            var hours = isDstToday
                ? Enumerable.Range(-3, 24).Select(i => startDateTime.AddHours(i)) // DST logic
                : Enumerable.Range(-2, 24).Select(i => startDateTime.AddHours(i)); // Standard time logic

            // Kaikki kuluvan vuorokauden tunnit
            var rowsToday = pricedata.Where(x => hours.Contains(x.date))
                                    .Select(x => new
                                    {
                                        date = TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone),
                                        value = x.value / 10 * taxPercentage + spotProvision
                                    });

            priceListView.ItemsSource = rowsToday;
            currentPriceDataToday = rowsToday;

            ///////////////////////////////////////////////////
            /////           HUOMISEN HINNAT                       /////
            //////////////////////////////////////////////////

            //Tarkistetaan jos huomisen hinnat näkyy
            if (date.ToShortDateString().Contains(today.AddDays(1).ToShortDateString()))
            {
                pricesTomorrowButton.IsEnabled = true;

               // Kesä-/talviaika
                bool isDstTomorrow = localTimeZone.IsDaylightSavingTime(startDateTimeTomorrow);
                
                // Luodaan muuttuja joka sisältää kaikki huomisen vuodokauden tunnit
                var hoursTomorrow = isDstTomorrow
                    ? Enumerable.Range(-3, 24).Select(i => startDateTimeTomorrow.AddHours(i)) // DST logic
                    : Enumerable.Range(-2, 24).Select(i => startDateTimeTomorrow.AddHours(i)); // Standard time logic

                // Kaikki huomisen tunnit
                var rowsTomorrow = pricedata.Where(x => hoursTomorrow.Contains(x.date))
                                        .Select(x => new
                                        {
                                            date = TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone),
                                            value = x.value / 10 * taxPercentage + spotProvision
                                        });

                priceListViewTomorrow.ItemsSource = rowsTomorrow;
                currentPriceDataTomorrow = rowsTomorrow;
            }       

            //Haetaan kaikki huomisen hinnat listalle laskentaa varten
            pricelistTomorrow = pricelistTomorrow.Where(x => TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone) >= startDateTimeTomorrow &&
                                                                                                           TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone) <= endDateTimeTomorrow).ToList();

            //Huomisen ylin, alin ja keskihinta sekä niiden asetus tekstikenttiin
            var dailyMaxTomorrow = pricelistTomorrow.Max(x => x.value);
            highPriceTomorrow.Text = (dailyMaxTomorrow / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            var dailyMinTomorrow = pricelistTomorrow.Min(x => x.value);
            lowPriceTomorrow.Text = (dailyMinTomorrow / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            double dailyAvgTomorrow = pricelistTomorrow.Average(x => x.value);
            avgPriceTomorrow.Text = (dailyAvgTomorrow / 10 * taxPercentage + spotProvision).ToString("F") + " c/kWh";

            // Draw chart if visible
            if (isChartVisible && currentPriceDataToday != null)
            {
                DrawChart(isShowingTomorrow && currentPriceDataTomorrow != null ? currentPriceDataTomorrow : currentPriceDataToday);
            }
        }

        private void pricesTomorrowButton_Clicked(object sender, EventArgs e)
        {            
            isShowingTomorrow = true;
            priceFieldLabel.Text = "HINNAT HUOMENNA";

            // Hide today's data
            priceListView.IsVisible = false;
            
            // Show tomorrow's data
            priceListViewTomorrowFrame.IsVisible = true;
            tomorrowAvgStack.IsVisible = true;
            countedPricesTomorrow.IsVisible = true;

            // Update button styles
            pricesTodayButton.Style = (Style)Resources["SecondaryButtonStyle"];
            pricesTomorrowButton.Style = (Style)Resources["ModernButtonStyle"];

            // Update chart if visible
            if (isChartVisible)
            {
                UpdateUIWithData();
            }
        }

        private void pricesTodayButton_Clicked(object sender, EventArgs e)
        {
            isShowingTomorrow = false;
            priceFieldLabel.Text = "HINNAT TÄNÄÄN";

            // Show today's data
            priceListView.IsVisible = true;
            
            // Hide tomorrow's data
            priceListViewTomorrowFrame.IsVisible = false;
            tomorrowAvgStack.IsVisible = false;
            countedPricesTomorrow.IsVisible = false;

            // Update button styles
            pricesTodayButton.Style = (Style)Resources["ModernButtonStyle"];
            pricesTomorrowButton.Style = (Style)Resources["SecondaryButtonStyle"];

            // Update chart if visible
            if (isChartVisible)
            {
                UpdateUIWithData();
            }
        }

        private void reloadButton_Clicked(object sender, EventArgs e)
        {
            priceFieldNow.Text = "Päivitetään...";
            //priceFieldToday.Text = "";
            FetchJsonDataAsync();
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

            if (buttonText == "0%")
            {
                taxPercentage = 1;
            }
            else if (buttonText == "10%")
            {
                taxPercentage = 1.10;
            }
            else if (buttonText == "24%")
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

        private void toggleChartButton_Clicked(object sender, EventArgs e)
        {
            isChartVisible = !isChartVisible;
            chartFrame.IsVisible = isChartVisible;
            toggleChartButton.Text = isChartVisible ? "Piilota kaavio" : "Näytä kaavio";
            
            if (isChartVisible)
            {
                UpdateUIWithData();
            }
        }

        private void DrawChart(dynamic priceData)
        {
            chartContainer.Children.Clear();

            var priceList = new List<dynamic>(priceData);
            if (priceList.Count == 0) return;

            double maxPrice = priceList.Max(p => (double)p.value);
            double minPrice = priceList.Min(p => (double)p.value);
            double priceRange = maxPrice - minPrice;
            if (priceRange == 0) priceRange = 1;

            double chartWidth = chartContainer.WidthRequest > 0 ? chartContainer.WidthRequest : 350;
            double chartHeight = chartContainer.HeightRequest;
            double barWidth = chartWidth / priceList.Count;
            double padding = 10;

            // Draw bars
            for (int i = 0; i < priceList.Count; i++)
            {
                double price = (double)priceList[i].value;
                double barHeight = ((price - minPrice) / priceRange) * (chartHeight - padding * 2);
                
                // Calculate color based on price
                Color barColor = GetPriceColor(price, minPrice, maxPrice);

                var bar = new BoxView
                {
                    Color = barColor,
                    WidthRequest = barWidth * 0.8,
                    HeightRequest = barHeight,
                    VerticalOptions = LayoutOptions.End
                };

                AbsoluteLayout.SetLayoutBounds(bar, new Rectangle(
                    i * barWidth + barWidth * 0.1, 
                    chartHeight - barHeight - padding, 
                    barWidth * 0.8, 
                    barHeight
                ));
                AbsoluteLayout.SetLayoutFlags(bar, AbsoluteLayoutFlags.None);
                chartContainer.Children.Add(bar);

                // Add hour label
                var hour = ((DateTime)priceList[i].date).Hour;
                var label = new Label
                {
                    Text = hour.ToString(),
                    FontSize = 10,
                    HorizontalTextAlignment = TextAlignment.Center,
                    TextColor = Application.Current.RequestedTheme == OSAppTheme.Dark ? Color.White : Color.Black
                };

                AbsoluteLayout.SetLayoutBounds(label, new Rectangle(
                    i * barWidth, 
                    chartHeight - 8, 
                    barWidth, 
                    15
                ));
                AbsoluteLayout.SetLayoutFlags(label, AbsoluteLayoutFlags.None);
                chartContainer.Children.Add(label);
            }
        }

        private Color GetPriceColor(double price, double min, double max)
        {
            double range = max - min;
            double position = (price - min) / range;
            
            if (position < 0.33)
                return Color.FromHex("#27AE60"); // Green
            else if (position < 0.66)
                return Color.FromHex("#F39C12"); // Orange
            else
                return Color.FromHex("#E74C3C"); // Red
        }
    }
}
