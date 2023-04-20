using CurrencyConverter.Model;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;

namespace CurrencyConverter.Controllers
{
    [Route("[controller]/[Action]")]
    [ApiController]
    public class Currencies : ControllerBase
    {

        private readonly string _connection;
        private readonly string _apiconnect;
        private readonly string _errlogfile;
        private readonly Int16 _secureGap;
        private readonly string _apikey;

        public Currencies(IConfiguration config)
        {
            _connection = config.GetSection("ConnectionStrings").GetSection("DefaultConnection").Value.ToString();
            _apiconnect = config.GetSection("ConnectionStrings").GetSection("APIConnect").Value.ToString();
            _errlogfile = config.GetSection("ConnectionStrings").GetSection("errorlog").Value.ToString();
            _secureGap = Convert.ToInt16(config.GetSection("ConnectionStrings").GetSection("SecureGap").Value); 
            _apikey = config.GetSection("ConnectionStrings").GetSection("apikey").Value.ToString();
        }
        //+---------------------------------------------------------------------------+
        //| Function Get of Currency                                                  |
        //| Detail Get Currency Path from External Api                                |
        //|        and Check data insert/update in Store_DB name : sp_GetCurrencyBase |
        //|        and Save Data in Store_DB name : sp_CurrencyRate                   |      
        //|       ** if error create and save _errlogfile                             |
        //+---------------------------------------------------------------------------+
        [HttpGet]
        public async Task<IActionResult> GetCurrency()
        {
            string[] currencies = GetCurrencies();
            if (currencies != null)
            {
                
                using (var handler = new HttpClientHandler())
                {
                    using (var client = new HttpClient(handler))
                    {
                        client.BaseAddress = new Uri(_apiconnect);
                        foreach (string fromCurrency in currencies)
                        {
                           string query = $"latest?base={fromCurrency}&symbols={string.Join(",", currencies.Where(c => c != fromCurrency))}&apikey={_apikey}";
                            try
                            {
                                HttpResponseMessage response = await client.GetAsync(query);

                                if (response.IsSuccessStatusCode)
                                {
                                    string json = await response.Content.ReadAsStringAsync();
                                    dynamic exchangeRates = JsonConvert.DeserializeObject(json);
                                    List<Exchange> rates = new List<Exchange>();
                                    foreach (var rate in exchangeRates.rates)
                                    {
                                        rates.Add(new Exchange
                                        {
                                            FromCurrency = fromCurrency,
                                            ToCurrency = rate.Name,
                                            Rate = rate.Value + (rate.Value * (_secureGap/100))
                                        });
                                    }
                                    SaveRates(rates);
                                }
                            }
                            catch (Exception ex)
                            {
                                using (StreamWriter writer = new StreamWriter(_errlogfile, true))
                                {
                                    writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss") + ": " + _apiconnect + ex.ToString());
                                }
                                throw;
                            }
                        }
                    }
                }
            }
            return Ok();
        }

        //+---------------------------------------------------------------------+
        //| Function Check Data CurrencyBase  Store_DB name : sp_GetCurrencyBase| 
        //| Return  Array() CurrencyBase DataModel                              |
        //|   ** if error create and save _errlogfile                           |
        //+---------------------------------------------------------------------+
        private string[] GetCurrencies()
        {
            var currencies = new List<string>();

            using (var conn = new SqlConnection(_connection))
            {
                using (var cmd = new SqlCommand("sp_GetCurrencyBase", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    try
                    {
                        conn.Open();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                currencies.Add(reader.GetString(0));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        using (StreamWriter writer = new StreamWriter(_errlogfile, true))
                        {
                            writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss") + ": " + ex.ToString());
                        }
                        throw;
                    }
                    finally
                    {
                        conn.Close();
                    }
                }
            }
            return currencies.ToArray();
        }

        //+---------------------------------------------------------------------------+
        //| Function Save Data CurrencyExchangeRate to Store_DB name : sp_CurrencyRate| 
        //|   ** if error create and save _errlogfile                                 |
        //+---------------------------------------------------------------------------+
        private void SaveRates(List<Exchange> rates)
        {
            using (SqlConnection conn = new SqlConnection(_connection))
            {
                foreach (var rate in rates)
                {
                    using (SqlCommand cmd = new SqlCommand("sp_CurrencyRate", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add("@CurrencyBase", SqlDbType.VarChar).Value = rate.FromCurrency;
                        cmd.Parameters.Add("@CounterCurrency", SqlDbType.VarChar).Value = rate.ToCurrency;
                        cmd.Parameters.Add("@ExchangeRate", SqlDbType.Decimal).Value = rate.Rate;

                        try
                        {
                            conn.Open();
                            cmd.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            using (StreamWriter writer = new StreamWriter(_errlogfile, true))
                            {
                                writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss") + ": " + ex.ToString());
                            }
                            throw;
                        }
                        finally
                        {
                            conn.Close();
                        }
                    }
                }
            }
        }
    }
}
