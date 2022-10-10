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

    
    public partial class MainPage :TabbedPage
    {        
        // voiko nää tehdä selkeämmin?
        StringBuilder sb = new StringBuilder();
        StringBuilder sb2 = new StringBuilder();
        StringBuilder sb3 = new StringBuilder();

        //DateTime today = DateTime.Today.ToLocalTime(); //Lokaali aikavyöhyke. addhours toimii tässä               
        //DateTime today = DateTime.Today.ToLocalTime().AddHours(15); //Lokaali aikavyöhyke, säädettävä tunteja               
        DateTime today = DateTime.Today;
        DateTime yesterday = DateTime.Today.AddDays(-1);           

        String todayHour = DateTime.Now.ToString("M/d/yyyy HH");
        String todayHourCorrected = DateTime.Now.AddHours(3).ToString("M/d/yyyy HH");
        String todayHour2 = DateTime.Now.ToFormat24h();
        
        public MainPage()
        {           
            InitializeComponent();
            //GetJsonAsync();
            //GetJsonAsyncModel();
            GetJsonAsyncOC();
            //statusField.IsVisible = false;
            //Console.WriteLine(pricelistName); //tämä listan testausta varten
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
                    string displayDate = DateTime.Parse(date).ToString("M/d/yyyy HH"); //muutetaan päivämäärä toiseen, yhtenäisempään string-muotoon
                    string price = item["value"].ToString();
                    double price2 = double.Parse(price) / 10;

                    if (taxSwitch.IsToggled)
                    {
                        price2 = price2 * 1.24;
                    }

                    //tämänhetkinen kellonaika
                    if (displayDate.ToString().Contains(todayHour))
                    {
                        sb.Append("Klo " + DateTime.Parse(date).ToString("HH:mm") + " -  " + price2.ToString("F") + " c/kWh" + "\n");
                        //sb.Append(todayHourCorrected + " " + price2.ToString("F") + " c/kWh" + "\n");
                        //sb.Append("Testataan: " + todayHour2);
                        //sb.Append(DateTime.Parse(date).ToString());
                    }

                    //kaikki tämän vuodokauden rivit                    
                    if (date.Contains(today.ToShortDateString()))
                    {
                        string startTime = DateTime.Parse(date).AddHours(3).ToString("HH:mm"); //koska JSON-datassa CET-ajat, lisätään tunteja
                        string endTime = DateTime.Parse(date).AddHours(4).ToString("HH:mm"); //päättymisaika on 1h alkamisajasta

                        sb2.Append("Klo " + startTime + "-" + endTime + ", hinta: " + price2.ToString("F") + " c/kWh" + "\n"); //muutetaan hinta string-muotoon ja pakotetaan 2 desimaalia
                        //sb2.Append(date + " " +  price2.ToString("F") + " c/kWh" + "\n");
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
                priceFieldToday.Text = sb2.ToString() + "\n";
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
        //async void GetJsonAsyncModel()
        //{
        //    var uri = new Uri("https://pakastin.fi/hinnat/prices");            
        //    HttpClient httpClient = new HttpClient();
        //    var response = await httpClient.GetAsync(uri);

        //    if (response.IsSuccessStatusCode)
        //    {
        //        //statusField.Text = "Onnistui koodilla: " + response;
        //        var content = await response.Content.ReadAsStringAsync(); // kokeile Simon getstringasync
        //        string json = content.ToString();                
        //        var jsonObject = JObject.Parse(json);

        //        //Deserialisoinnin testailua
        //        //var data = JsonConvert.DeserializeObject<Price>(json);
        //        //if (data.ToString().Contains("2022"))
        //        //{
        //        //    priceFieldToday.Text = data.ToString();
        //        //}

        //        //IList testi
        //        //var templist = jsonObject["prices"].ToObject<IList<Price>>();            

        //        var prices = jsonObject["prices"];
        //        var jsonArray = JArray.Parse(prices.ToString());

        //        foreach (var item in jsonArray)
        //        {
        //            Price p = new Price();
        //            string date = item["date"].ToString();
        //            string price = item["value"].ToString();
        //            //string trimmedDate = date.Substring(date.IndexOf(' ') + 1);
        //            //string displayDate = DateTime.Parse(trimmedDate).ToString("dd/MM/yyyy HH:mm"); //muutetaan päivämäärä string-muotoon
        //            p.date = DateTime.Parse(date);
        //            p.value = double.Parse(price); // tähän tyssää jos int, "Input string was not in a correct format"
        //            pricelist.Add(p);
        //        }

        //        //LINQ-kysely
        //        var today = pricelist.Where(p => p.date.ToString().Contains(todayHour));
        //        foreach (var dailyprice in today)
        //        {
        //            priceListView.ItemsSource = dailyprice.ToString();
        //            priceFieldToday.Text = pricelist.ToString();
        //        }

        //        //Find
        //        //List<Price> tänään = pricelist.FindAll(p => p.date.ToString().Contains(today.ToString()));
        //        //foreach (var dailyprice in tänään)
        //        //{
        //        //    priceListView.ItemsSource = tänään.ToString();
        //        //    priceFieldToday.Text = dailyprice.ToString(); // palauttaa app nimen ja listan nimen? tutkittava
        //        //}
        //        //priceFieldToday.Text = tänään.ToString();

        //        //statusField.Text = "Tiedot haettu onnistuneesti";
        //    }
        //    else
        //    {
        //        await DisplayAlert("Virhe!", "Json-data ei ole saatavilla, tai siihen ei saatu yhteyttä", "OK");
        //        statusField.Text = "Virhe, ei yhteyttä?";
        //    }
        //}

        async void GetJsonAsyncOC()
        {
            HttpClient httpClient = new HttpClient();
            
            var uri = new Uri("https://pakastin.fi/hinnat/prices");            
            string json = await httpClient.GetStringAsync(uri);

            var jsonObject = JObject.Parse(json);
            var prices = jsonObject["prices"];
            var jsonArray = JArray.Parse(prices.ToString());

            //siirrä päivämäärät ylös?
            DateTime startDateTime = DateTime.Today; //Today at 00:00:00
            DateTime endDateTime = DateTime.Today.AddDays(1).AddTicks(-1); //Today at 23:59:59

            List<Price> prixe = JsonConvert.DeserializeObject<List<Price>>(jsonArray.ToString());
            ObservableCollection<Price> dataa = new ObservableCollection<Price>(prixe);

            //prixe = prixe.OrderBy(x => x.value).ToList(); //tutki tätä jos haluaa järjestää listaa ennenkuin näyttää sen
            //prixe = prixe.Where(x => x.date.Month == DateTime.Today.Month).ToList(); //tutki tätä lisää!
            //prixe = prixe.Where(x => x.date.Month == 9).ToList(); // tietty kuukausi
            //prixe = prixe.Where(x => x.date.ToString() == DateTime.Now.ToString("M/d/yyyy HH")).ToList();
            prixe = prixe.Where(x => x.date >=startDateTime && x.date<= endDateTime).ToList(); //TÄLLÄ TOIMII NYT KOKO KULUVAN VUOROKAUDEN TUNNIT

            //Console.WriteLine("TIPPING TIME " + prixe);

            //double uselessTotal = prixe.Sum(x => x.value);
            //Console.WriteLine($"The total number of paid kilowatts is {uselessTotal}");

            //double monthlyAvg = 0;
            //monthlyAvg = prixe.Where(x => x.date.Month == DateTime.Today.Month).Average(x => x.value);
            //Console.WriteLine($"This month's average is: {monthlyAvg}");

            //double dailyAvg = 0;
            //dailyAvg = prixe.Where(x => x.date.Day == DateTime.Today.Day).Average(x => x.value);
            //Console.WriteLine($"Today's average is: {dailyAvg}");

            //TÄMÄ TÄMÄ TÄMÄ
            //TÄLLÄ SAA LISTATTUA AINAKIN KONSOLIIN MITÄ HALUAA MODAA TÄTÄ JA TÄN ALTA KAIKKI FUCK YEAH
            foreach (var price in prixe)
            {
                string date = price.date.ToString("M/d/yyyy HH"); //muutetaan päivämäärä toiseen, yhtenäisempään string-muotoon
                Double value = price.value;

                Console.WriteLine(date + " " + value);
                priceFieldToday.Text = date + " " + value + "\n"; //tämä antaa kuitenkin vain vikan arvon?
                priceListView.ItemsSource = dataa.Where(x => x.date >= startDateTime && x.date <= endDateTime); //antaa nyt listalle koko vuorokauden tunnit :)
            }

            //VANHEMPAA TESTAILUA, pidetään toistaiseksi tallessa
            //JObject price = JObject.Parse(json); //TÄSTÄ OTA TÄSTÄ OTA TÄSTÄ OTA TÄSTÄ OTA TÄSTÄ OTA TÄSTÄ OTA TÄSTÄ OTA TÄSTÄ OTA TÄSTÄ OTA TÄSTÄ OTA 
            //List <JToken> tokens = price.Children().ToList()
            //Console.WriteLine(price.ToString()); //tämä toimii nyt, eli listaa kaikki!

            //var plöö = price["prices"][0]; //tällä antaa ekan ilmentymän
            //Console.WriteLine(plöö);

            //var blaa = price["prices"]; // tällä antaa myös kaikki ilmentymät
            //Console.WriteLine(blaa);

            //foreach(var item in blaa)
            //{
            //    DateTime date = (DateTime)item["date"];
            //    string displayDate = date.ToString("M/d/yyyy HH"); //muutetaan päivämäärä toiseen, yhtenäisempään string-muotoon
            //    double prices = (double)item["value"]; // tähän double ja tsek desimaalit

            //    if (displayDate.ToString().Contains(today.ToShortDateString()))
            //    {
            //        Console.WriteLine(displayDate.ToString() + " " +  prices);
            //        priceListView.ItemsSource = displayDate.ToString() + " " + prices; //tulostaa kirjaimen per rivi ja vain viimeisimmän esiintymän?
            //    }
            //}           
        }

        private void pricesTomorrow_Clicked(object sender, EventArgs e)
        {
            pricesToday.IsVisible = true;
            pricesTomorrow.IsVisible = false;
            if (taxSwitch.IsToggled)
            {
                priceFieldLabel.Text = "Hinnat huomenna (ALV 24%)";
            }
            else
            {
                priceFieldLabel.Text = "Hinnat huomenna (ALV 0%)";
            }
            priceFieldToday.Text = sb3.ToString() + "\n"; 
        }

        private void pricesToday_Clicked(object sender, EventArgs e)
        {
            pricesTomorrow.IsVisible = true;
            pricesToday.IsVisible = false;
            if (taxSwitch.IsToggled)

            {
                priceFieldLabel.Text = "Hinnat tänään (ALV 24%)";
            }
            else
            {
                priceFieldLabel.Text = "Hinnat tänään (ALV 0%)";
            }
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
            //GetJsonAsyncModel();
        }
    }


}
