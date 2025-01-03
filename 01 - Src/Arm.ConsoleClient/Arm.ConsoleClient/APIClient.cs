using Arm.ConsoleClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Arm.ConsoleClient
{
    public class APIClient
    {
        public static async Task<List<LibroAPI>> ObtenerLibrosDesdeAPIAsync(int lastId, DateTime lastStamp)
        {
            HttpClient client = new();
            int qty = int.MaxValue;

            string lastStampStr = lastStamp.ToString("yyyy-MM-ddTHH:mm:ss");
            string url = $"http://gestionwn3.ddns.net:8282/api/Libros/ObtenerResumenes?ultimostamp={lastStampStr}&ultimoId={lastId}&cantidad={qty}";

            HttpResponseMessage response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<LibroAPI>>(jsonResponse) ?? new List<LibroAPI>();
            }

            throw new Exception("No se pudo utilizar la API");
        }
    }
}
