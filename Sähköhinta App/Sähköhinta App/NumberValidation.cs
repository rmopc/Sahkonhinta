using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xamarin.Forms;

namespace Sähköhinta_App
{
    public class NumberValidation : Behavior<Entry>
    {
        private async void OnEntryUnfocused(object sender, FocusEventArgs e)
        {
            double parsedValue;
            if (!double.TryParse(((Entry)sender).Text, out parsedValue))
            {
                ((Entry)sender).Text = "";
                await Application.Current.MainPage.DisplayAlert("Error", "Invalid input, please enter a number with two decimals", "OK");
            }
            else
            {
                if (!Regex.IsMatch(((Entry)sender).Text, @"^[0-9]+(\.[0-9]{1,2})?$"))
                {
                    ((Entry)sender).Text = "";
                    await Application.Current.MainPage.DisplayAlert("Error", "Invalid input, please enter a number with two decimals", "OK");
                }
            }
        }

    }
}
