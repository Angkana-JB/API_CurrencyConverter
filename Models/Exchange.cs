﻿

namespace CurrencyConverter.Model
{
    public class Exchange
    {
        public string FromCurrency { get; set; }
        public string ToCurrency { get; set; }
        public decimal Rate { get; set; }
    }
}
