using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using Arm.ConsoleClient.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace Arm.ConsoleClient
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            await Chat();

            Console.WriteLine("Operación completada.");
        }

        private static async Task Chat()
        {
            IChatClient? chatClient = null;

            // Configura el cliente para Ollama
            Console.WriteLine("Configuring Ollama Chat Client...");

            // Pide el nombre del modelo al usuario
            Console.Write("Enter your Ollama model name: ");
            string ollamaModelName = "HammerAI/neuraldaredevil-abliterated";

            // Crea el cliente de Ollama
            chatClient = new OllamaChatClient(new Uri("http://localhost:11434/"), ollamaModelName);

            if (chatClient is null)
            {
                Console.WriteLine("Failed to configure the chat client.");
                return;
            }

            Console.WriteLine("Ollama Chat Client configured successfully.");

            // Lista para almacenar los mensajes del chat
            List<ChatMessage> chatMessages = new List<ChatMessage>();

            // Bucle principal para interactuar con el modelo
            while (true)
            {
                Console.Write("You: ");
                string userMessage = Console.ReadLine();

                if (string.Equals(userMessage, "exit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Exiting chat...");
                    break;
                }

                chatMessages.Clear();

                // Agrega el mensaje del usuario a la lista
                chatMessages.Add(new ChatMessage(ChatRole.User, $"Por favor extrae exclusviamente las palabras clave principales y nada mas del siguiente mensaje separadas por coma: '{userMessage}'. Importante: Por lo menos 20 palabras clave! Aunque debas usar sinónimos."));

                Console.WriteLine("AI:");

                // Inicializa una variable para almacenar la respuesta completa
                string aiResponse = string.Empty;

                // Llama a CompleteStreamingAsync y procesa las respuestas en streaming
                await foreach (var update in chatClient.CompleteStreamingAsync(chatMessages))
                {
                    //if (!string.IsNullOrEmpty(update))
                    {
                        aiResponse += update;
                        Console.Write(update);
                    }
                }

                Separator();

                var relatedBooks = await GetRelatedBooks(aiResponse);
                var relatedBooksStr = string.Empty;
                foreach (var book in relatedBooks)
                {
                    //Console.WriteLine($"{book.Titulo}");
                    relatedBooksStr += book;
                }

                Separator();
            }
        }

        private static void Separator()
        {
            Console.WriteLine();
            Console.WriteLine("/#/#/#/#/#/#/#/#/#/#/#/#/#/#");
            Console.WriteLine();
        }

        private static async Task<List<LibroAPI>> GetRelatedBooks(string input)
        {
            // Inicializar la base de datos
            const string databasePath = "libros.db";

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();

            //Console.WriteLine("Bienvenido. Escribe una pregunta:");
            //string pregunta = Console.ReadLine();

            // Generar embedding de la pregunta
            IEmbeddingGenerator<string, Embedding<float>> generator =
                new OllamaEmbeddingGenerator(new Uri("http://localhost:11434/"), "nomic-embed-text");

            var preguntaEmbedding = (await generator.GenerateAsync(new List<string> { $"search_query: {input}" }))[0].Vector
                .ToArray()
                .Select(v => (double)v)
                .ToArray();

            // Buscar los libros más cercanos
            Console.WriteLine("Buscando los libros más cercanos...");
            var librosCercanos = SQLiteHelper.GetLibrosMasCercanos(connection, preguntaEmbedding, 20);

            Console.WriteLine($"Se encontraron {librosCercanos.Count} libros:");
            foreach (var libro in librosCercanos)
            {
                Console.WriteLine($"- {libro.Titulo} ({libro.Autor})");
            }

            return librosCercanos;
        }

        private async Task ObtenerLibrosYGuardarEnBD(SqliteConnection connection)
        {
            // Misc
            var lastId = 0; // Último ID obtenido (puedes actualizar esto según tu lógica)
            var lastStamp = DateTime.MinValue; // Última fecha de actualización (puedes ajustar según tu lógica)
            const string databasePath = "libros.db";

            // Obtener libros desde la API y guardarlos en la BD
            Console.WriteLine("Cargando libros desde la API...");
            var libros = await APIClient.ObtenerLibrosDesdeAPIAsync(lastId, lastStamp);
            Console.WriteLine($"Obtenidos {libros.Count} libros.");
            int counter = 0;
            foreach (var libro in libros)
            {
                SQLiteHelper.InsertLibro(connection, libro);
                Console.WriteLine($"Insertando: #{counter++} - {libro.Titulo}");
            }
        }

        private async Task RecalculateEmbeddings(string databasePath)
        {
            // Guardar los libros y sus embeddings
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();

            var librosdb = SQLiteHelper.GetAllLibros(connection);//.Take(1);
            var embeddings = new ConcurrentDictionary<string, double[]>();
            IEmbeddingGenerator<string, Embedding<float>> generator =
                new OllamaEmbeddingGenerator(new Uri("http://localhost:11434/"), "nomic-embed-text");
            //new OllamaEmbeddingGenerator(new Uri("http://localhost:11434/"), "jina/jina-embeddings-v2-base-es");

            //SQLiteHelper.DeleteAllEmbeddings(connection);

            // Límite de concurrencia
            SemaphoreSlim semaphore = new SemaphoreSlim(10);

            int counter = 0; // Contador para los libros procesados

            var tasks = librosdb.Select(async libro =>
            {
                await semaphore.WaitAsync(); // Adquirir un espacio en el semáforo
                try
                {
                    var embedding = (await generator.GenerateAsync(new List<string> { $"clasification: {libro.ToString()}" }))[0].Vector
                        .ToArray()
                        .Select(v => (double)v)
                        .ToArray();

                    // Convertir embedding a un arreglo de bytes
                    var embeddingBytes = embedding.SelectMany(BitConverter.GetBytes).ToArray();

                    // Agregar el embedding a la lista del libro
                    libro.Embeddings.Add(embeddingBytes);

                    // Insertar el libro con sus embeddings
                    SQLiteHelper.InsertEmbeddingsToLibro(connection, libro);

                    // Incrementar y mostrar el contador
                    int currentCount = Interlocked.Increment(ref counter);
                    Console.WriteLine($"Procesado libro #{currentCount}: {libro.Titulo} con {libro.Embeddings.Count} embeddings.");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error al procesar {libro.Titulo}: {e.Message}");
                }
                finally
                {
                    semaphore.Release(); // Liberar el espacio en el semáforo
                }
            });
            await Task.WhenAll(tasks);
        }
    }

}
