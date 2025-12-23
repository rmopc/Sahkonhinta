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
using Sahkonhinta_App.Services;

namespace Sahkonhinta_App
{
    public partial class MainPage : TabbedPage
    {
        DateTime today = DateTime.Today;

        // Haetaan oikea aikavyöhyke, käytetään listaamaan vuorokauden tunteja alempana
        TimeZoneInfo localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

        double taxPercentage;
        double spotProvision;
        DayPriceData todayData = null;
        DayPriceData tomorrowData = null;
        bool isChartVisible = false;
        bool isShowingTomorrow = false;
        bool isShowingFifteenMinutes = false; // Toggle for 15-minute vs hourly view
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

        async Task FetchJsonDataAsync(bool forceRefresh = false)
        {
            try
            {
                // Use the caching service to get price data
                (todayData, tomorrowData) = await PriceDataService.GetPriceDataAsync(forceRefresh);

                if (todayData != null && todayData.Prices != null && todayData.Prices.Any())
                {
                    UpdateUIWithData();
                }
                else
                {
                    await DisplayAlert("Virhe datassa", "Hintatietoja ei voitu hakea.", "OK");
                }
            }
            catch (Exception e)
            {
                await DisplayAlert("Virhe", e.Message, "OK");
            }
        }

        void UpdateUIWithData()
        {
            if (todayData == null || todayData.Prices == null || !todayData.Prices.Any())
                return;

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone);

            ///////////////////////////////////////////////////
            /////           TODAY'S PRICES (FULL 24H)       /////
            //////////////////////////////////////////////////

            // Select price list based on interval toggle (hourly average or 15-minute)
            var todayPrices = isShowingFifteenMinutes
                ? todayData.FifteenMinutePrices ?? todayData.HourlyPrices
                : todayData.HourlyPrices ?? todayData.FifteenMinutePrices;

            // Calculate statistics
            var dailyMax = todayPrices.Max(x => x.value);
            var dailyMin = todayPrices.Min(x => x.value);
            var dailyAvg = todayPrices.Average(x => x.value);

            highPrice.Text = $"{(dailyMax / 10 * taxPercentage + spotProvision):F} c/kWh";
            lowPrice.Text = $"{(dailyMin / 10 * taxPercentage + spotProvision):F} c/kWh";
            avgPrice.Text = $"{(dailyAvg / 10 * taxPercentage + spotProvision):F} c/kWh";

            // Find current price
            Services.Price currentPrice = null;
            if (isShowingFifteenMinutes)
            {
                // For 15-minute intervals, find the closest time slot
                currentPrice = todayPrices
                    .OrderBy(x => Math.Abs((TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone) - nowLocal).TotalMinutes))
                    .FirstOrDefault();
            }
            else
            {
                // For hourly averages, find the current hour
                var currentHour = nowLocal.Hour;
                currentPrice = todayPrices.FirstOrDefault(x =>
                    TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone).Hour == currentHour);
            }

            if (currentPrice != null)
            {
                priceFieldNow.Text = $"Hinta nyt: {(currentPrice.value / 10 * taxPercentage + spotProvision):F} c/kWh";
            }
            else
            {
                priceFieldNow.Text = "Hinta nyt: - c/kWh";
            }

            // Prepare display data with converted timezone and applied tax
            var rowsToday = todayPrices.Select(x => new
            {
                date = TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone),
                value = x.value / 10 * taxPercentage + spotProvision
            }).OrderBy(x => x.date).ToList();

            priceListView.ItemsSource = rowsToday;
            currentPriceDataToday = rowsToday;

            ///////////////////////////////////////////////////
            /////           TOMORROW'S PRICES (FULL 24H)    /////
            //////////////////////////////////////////////////

            if (tomorrowData != null && (tomorrowData.HourlyPrices?.Any() == true || tomorrowData.FifteenMinutePrices?.Any() == true))
            {
                pricesTomorrowButton.IsEnabled = true;

                // Select price list based on interval toggle (hourly average or 15-minute)
                var tomorrowPrices = isShowingFifteenMinutes
                    ? tomorrowData.FifteenMinutePrices ?? tomorrowData.HourlyPrices
                    : tomorrowData.HourlyPrices ?? tomorrowData.FifteenMinutePrices;

                // Calculate tomorrow's statistics
                var dailyMaxTomorrow = tomorrowPrices.Max(x => x.value);
                var dailyMinTomorrow = tomorrowPrices.Min(x => x.value);
                var dailyAvgTomorrow = tomorrowPrices.Average(x => x.value);

                highPriceTomorrow.Text = $"{(dailyMaxTomorrow / 10 * taxPercentage + spotProvision):F} c/kWh";
                lowPriceTomorrow.Text = $"{(dailyMinTomorrow / 10 * taxPercentage + spotProvision):F} c/kWh";
                avgPriceTomorrow.Text = $"{(dailyAvgTomorrow / 10 * taxPercentage + spotProvision):F} c/kWh";

                // Prepare display data
                var rowsTomorrow = tomorrowPrices.Select(x => new
                {
                    date = TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone),
                    value = x.value / 10 * taxPercentage + spotProvision
                }).OrderBy(x => x.date).ToList();

                priceListViewTomorrow.ItemsSource = rowsTomorrow;
                currentPriceDataTomorrow = rowsTomorrow;
            }
            else
            {
                pricesTomorrowButton.IsEnabled = false;
            }

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
            priceListViewTomorrow.IsVisible = true;
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
            priceListViewTomorrow.IsVisible = false;
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
            FetchJsonDataAsync(forceRefresh: true);
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

        private void toggleIntervalButton_Clicked(object sender, EventArgs e)
        {
            isShowingFifteenMinutes = !isShowingFifteenMinutes;
            toggleIntervalButton.Text = isShowingFifteenMinutes ? "Näytä tuntihinnat" : "Näytä 15 min hinnat";

            // Refresh the UI with the new interval selection
            UpdateUIWithData();
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

            // Get actual container width, with a reasonable fallback
            double chartWidth = chartContainer.Width > 0 && chartContainer.Width < 10000
                ? chartContainer.Width
                : (Application.Current.MainPage?.Width ?? 360) - 70; // Account for frame margins/padding

            // Ensure we have a reasonable width
            if (chartWidth <= 0 || chartWidth > 10000)
            {
                chartWidth = 320;
            }

            double chartHeight = chartContainer.Height > 0 ? chartContainer.Height : chartContainer.HeightRequest;
            if (chartHeight <= 0)
            {
                chartHeight = 220;
            }

            const double axisLabelWidth = 40;
            const double rightPadding = 8;
            const double topPadding = 20;
            const double bottomPadding = 32;

            // Calculate drawable area - MUST stay within container bounds
            double drawableWidth = chartWidth - axisLabelWidth - rightPadding;
            double drawableHeight = chartHeight - topPadding - bottomPadding;

            // Calculate bar dimensions
            double slotWidth = drawableWidth / priceList.Count;
            double barWidth = Math.Max(2, Math.Min(slotWidth * 0.75, 18));
            double slotPadding = slotWidth - barWidth;
            double chartStartX = axisLabelWidth;

            bool isDarkTheme = Application.Current.RequestedTheme == OSAppTheme.Dark;
            Color primaryTextColor = isDarkTheme ? Color.White : Color.Black;
            Color secondaryTextColor = isDarkTheme ? Color.FromHex("#BDC3C7") : Color.FromHex("#7F8C8D");
            Color axisLineColor = isDarkTheme ? Color.FromHex("#4C5862") : Color.FromHex("#D8E0E6");

            // Horizontal guides (max / mid / min) - ensure they don't exceed drawable width
            AddHorizontalGuide(topPadding, drawableWidth, axisLineColor, 1);
            AddHorizontalGuide(topPadding + drawableHeight / 2, drawableWidth, axisLineColor, 0.8, 0.35);
            AddHorizontalGuide(topPadding + drawableHeight, drawableWidth, axisLineColor, 1);

            // Price scale labels on the left
            AddYAxisLabel(maxPrice, new Rectangle(0, topPadding - 10, axisLabelWidth - 4, 16));
            AddYAxisLabel((maxPrice + minPrice) / 2, new Rectangle(0, topPadding + drawableHeight / 2 - 8, axisLabelWidth - 4, 16));
            AddYAxisLabel(minPrice, new Rectangle(0, topPadding + drawableHeight - 10, axisLabelWidth - 4, 16));

            for (int i = 0; i < priceList.Count; i++)
            {
                double price = (double)priceList[i].value;
                double normalized = (price - minPrice) / priceRange;
                double barHeight = Math.Max(3, normalized * drawableHeight);

                Color barColor = GetPriceColor(price, minPrice, maxPrice);
                double slotOffset = chartStartX + i * slotWidth + slotPadding / 2;

                // Ensure bar doesn't exceed container bounds
                if (slotOffset + barWidth > chartWidth)
                {
                    barWidth = Math.Max(2, chartWidth - slotOffset - rightPadding);
                }

                var bar = new BoxView
                {
                    Color = barColor,
                    WidthRequest = barWidth,
                    HeightRequest = barHeight,
                    CornerRadius = 2
                };

                AbsoluteLayout.SetLayoutBounds(bar, new Rectangle(
                    slotOffset,
                    topPadding + drawableHeight - barHeight,
                    barWidth,
                    barHeight));
                AbsoluteLayout.SetLayoutFlags(bar, AbsoluteLayoutFlags.None);
                chartContainer.Children.Add(bar);

                // Show price label above taller bars
                if (barHeight >= 18 && slotWidth >= 12)
                {
                    double priceFont = slotWidth >= 16 ? 9 : 8;
                    var priceLabel = new Label
                    {
                        Text = $"{price:0.0}",
                        FontSize = priceFont,
                        HorizontalTextAlignment = TextAlignment.Center,
                        TextColor = primaryTextColor,
                        FontAttributes = FontAttributes.Bold
                    };

                    AbsoluteLayout.SetLayoutBounds(priceLabel, new Rectangle(
                        slotOffset,
                        topPadding + drawableHeight - barHeight - 14,
                        barWidth,
                        12));
                    AbsoluteLayout.SetLayoutFlags(priceLabel, AbsoluteLayoutFlags.None);
                    chartContainer.Children.Add(priceLabel);
                }

                // Time labels at bottom
                DateTime itemTime = (DateTime)priceList[i].date;
                string timeText = string.Empty;

                // For 15-minute intervals, show time in HH:mm format for selected entries
                // For hourly intervals, show hour only
                if (priceList.Count > 50) // 15-minute intervals (96 entries)
                {
                    // Show labels every 2 or 4 hours depending on space
                    if (slotWidth >= 14 && itemTime.Minute == 0 && itemTime.Hour % 2 == 0)
                    {
                        timeText = itemTime.ToString("HH:mm");
                    }
                    else if (slotWidth >= 8 && itemTime.Minute == 0 && itemTime.Hour % 4 == 0)
                    {
                        timeText = itemTime.ToString("HH:mm");
                    }
                }
                else // Hourly intervals (24 entries)
                {
                    int hour = itemTime.Hour;
                    // Show all hours if space allows, otherwise show every 2nd or 3rd
                    if (slotWidth >= 14)
                    {
                        timeText = hour.ToString();
                    }
                    else if (slotWidth >= 10 && i % 2 == 0)
                    {
                        timeText = hour.ToString();
                    }
                    else if (slotWidth >= 6 && i % 3 == 0)
                    {
                        timeText = hour.ToString();
                    }
                }

                if (!string.IsNullOrEmpty(timeText))
                {
                    var timeLabel = new Label
                    {
                        Text = timeText,
                        FontSize = slotWidth >= 14 ? 10 : 9,
                        HorizontalTextAlignment = TextAlignment.Center,
                        TextColor = secondaryTextColor
                    };

                    AbsoluteLayout.SetLayoutBounds(timeLabel, new Rectangle(
                        slotOffset - slotPadding / 2,
                        topPadding + drawableHeight + 4,
                        slotWidth,
                        14));
                    AbsoluteLayout.SetLayoutFlags(timeLabel, AbsoluteLayoutFlags.None);
                    chartContainer.Children.Add(timeLabel);
                }
            }

            // Unit label in top-left corner
            var unitLabel = new Label
            {
                Text = "c/kWh",
                FontSize = 9,
                TextColor = secondaryTextColor,
                HorizontalTextAlignment = TextAlignment.Start,
                VerticalTextAlignment = TextAlignment.Start
            };
            AbsoluteLayout.SetLayoutBounds(unitLabel, new Rectangle(2, 2, axisLabelWidth - 4, 16));
            AbsoluteLayout.SetLayoutFlags(unitLabel, AbsoluteLayoutFlags.None);
            chartContainer.Children.Add(unitLabel);

            void AddYAxisLabel(double value, Rectangle bounds)
            {
                var label = new Label
                {
                    Text = $"{value:0.0}",
                    FontSize = 9,
                    TextColor = secondaryTextColor,
                    HorizontalTextAlignment = TextAlignment.End,
                    VerticalTextAlignment = TextAlignment.Center
                };
                AbsoluteLayout.SetLayoutBounds(label, bounds);
                AbsoluteLayout.SetLayoutFlags(label, AbsoluteLayoutFlags.None);
                chartContainer.Children.Add(label);
            }

            void AddHorizontalGuide(double y, double width, Color color, double thickness = 1, double opacity = 0.25)
            {
                var line = new BoxView
                {
                    Color = color,
                    Opacity = opacity,
                    HeightRequest = thickness
                };
                // Ensure guide line stays within bounds
                AbsoluteLayout.SetLayoutBounds(line, new Rectangle(chartStartX, y, width, thickness));
                AbsoluteLayout.SetLayoutFlags(line, AbsoluteLayoutFlags.None);
                chartContainer.Children.Add(line);
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
