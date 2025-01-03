namespace Arm.ConsoleClient.Models
{
    using System;
    using System.Text.Json.Serialization;

    public class LibroAPI
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("codigo")]
        public string Codigo { get; set; }

        [JsonPropertyName("titulo")]
        public string Titulo { get; set; }

        [JsonPropertyName("autor")]
        public string Autor { get; set; }

        [JsonPropertyName("resumen")]
        public string Resumen { get; set; }

        [JsonPropertyName("stamp")]
        public DateTime Stamp { get; set; }

        [JsonIgnore]
        public List<byte[]> Embeddings { get; set; } = [];

        public override string ToString()
        {
            return $"Titulo: {Titulo}, Autor: {Autor}, Resumen: {Resumen}";
        }
    }
}
