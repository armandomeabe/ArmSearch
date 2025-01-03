using System;
using System.Collections.Generic;
using Arm.ConsoleClient.Models;
using Microsoft.Data.Sqlite;

namespace Arm.ConsoleClient
{
    public class SQLiteHelper
    {
        public static List<LibroAPI> GetLibrosMasCercanos(SqliteConnection connection, double[] preguntaEmbedding, int topN = 20)
        {
            var librosCercanos = new List<(LibroAPI libro, double similitud)>();

            var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = @"
        SELECT l.id, l.codigo, l.titulo, l.autor, l.resumen, l.stamp, e.embedding
        FROM libros l
        INNER JOIN embeddings e ON l.id = e.libro_id;
    ";

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                var libro = new LibroAPI
                {
                    Id = reader.GetInt32(0),
                    Codigo = reader.GetString(1),
                    Titulo = reader.GetString(2),
                    Autor = reader.GetString(3),
                    Resumen = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Stamp = reader.GetDateTime(5)
                };

                // Recuperar el embedding desde la base de datos
                var embeddingBytes = (byte[])reader["embedding"];
                var libroEmbedding = Enumerable.Range(0, embeddingBytes.Length / sizeof(double))
                                               .Select(i => BitConverter.ToDouble(embeddingBytes, i * sizeof(double)))
                                               .ToArray();

                // Calcular similitud (producto punto)
                double similitud = libroEmbedding.Zip(preguntaEmbedding, (x, y) => x * y).Sum();
                librosCercanos.Add((libro, similitud));
            }

            // Ordenar por similitud descendente y tomar los N más cercanos
            return librosCercanos.OrderByDescending(l => l.similitud)
                                 .Take(topN)
                                 .Select(l => l.libro)
                                 .ToList();
        }

        public static void InitializeDatabase(string databasePath)
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();

            // Cargar la extensión sqlite-vec
            var loadExtensionCommand = connection.CreateCommand();
            //loadExtensionCommand.CommandText = "SELECT load_extension('sqlite-vec');";
            //loadExtensionCommand.ExecuteNonQuery();

            // Crear las tablas necesarias
            var createTablesCommand = connection.CreateCommand();
            createTablesCommand.CommandText = @"
        CREATE TABLE IF NOT EXISTS libros (
            id INTEGER PRIMARY KEY,
            codigo TEXT NOT NULL,
            titulo TEXT NOT NULL,
            autor TEXT NOT NULL,
            resumen TEXT,
            stamp DATETIME NOT NULL
        );

        CREATE TABLE IF NOT EXISTS embeddings (
            libro_id INTEGER,
            embedding BLOB NOT NULL,
            FOREIGN KEY (libro_id) REFERENCES libros(id)
        );
        ";
            createTablesCommand.ExecuteNonQuery();

            Console.WriteLine("Base de datos inicializada correctamente con tablas separadas.");
        }

        public static void InsertLibro(SqliteConnection connection, LibroAPI libro)
        {
            using var transaction = connection.BeginTransaction();

            // Insertar metadatos en la tabla principal de libros
            var insertMetadataCommand = connection.CreateCommand();
            insertMetadataCommand.CommandText = @"
            INSERT INTO libros (id, codigo, titulo, autor, resumen, stamp)
            VALUES (@id, @codigo, @titulo, @autor, @resumen, @stamp);
            ";
            insertMetadataCommand.Parameters.AddWithValue("@id", libro.Id);
            insertMetadataCommand.Parameters.AddWithValue("@codigo", libro.Codigo);
            insertMetadataCommand.Parameters.AddWithValue("@titulo", libro.Titulo);
            insertMetadataCommand.Parameters.AddWithValue("@autor", libro.Autor);
            insertMetadataCommand.Parameters.AddWithValue("@resumen", libro.Resumen ?? (object)DBNull.Value);
            insertMetadataCommand.Parameters.AddWithValue("@stamp", libro.Stamp);
            insertMetadataCommand.ExecuteNonQuery();

            transaction.Commit();

            Console.WriteLine($"Libro con ID {libro.Id} insertado correctamente.");
        }

        public static void InsertEmbeddingsToLibro(SqliteConnection connection, LibroAPI libro)
        {
            using var transaction = connection.BeginTransaction();

            //        // Insertar metadatos en la tabla principal de libros
            //        var insertMetadataCommand = connection.CreateCommand();
            //        insertMetadataCommand.CommandText = @"
            //INSERT INTO libros (id, codigo, titulo, autor, resumen, stamp)
            //VALUES (@id, @codigo, @titulo, @autor, @resumen, @stamp);
            //";
            //        insertMetadataCommand.Parameters.AddWithValue("@id", libro.Id);
            //        insertMetadataCommand.Parameters.AddWithValue("@codigo", libro.Codigo);
            //        insertMetadataCommand.Parameters.AddWithValue("@titulo", libro.Titulo);
            //        insertMetadataCommand.Parameters.AddWithValue("@autor", libro.Autor);
            //        insertMetadataCommand.Parameters.AddWithValue("@resumen", libro.Resumen ?? (object)DBNull.Value);
            //        insertMetadataCommand.Parameters.AddWithValue("@stamp", libro.Stamp);
            //        insertMetadataCommand.ExecuteNonQuery();

            // Insertar embeddings en la tabla embeddings
            if (libro.Embeddings != null && libro.Embeddings.Any())
            {
                foreach (var embedding in libro.Embeddings)
                {
                    var insertEmbeddingCommand = connection.CreateCommand();
                    insertEmbeddingCommand.CommandText = @"
            INSERT INTO embeddings (libro_id, embedding)
            VALUES (@libro_id, @embedding);
            ";
                    insertEmbeddingCommand.Parameters.AddWithValue("@libro_id", libro.Id);
                    insertEmbeddingCommand.Parameters.AddWithValue("@embedding", embedding);
                    insertEmbeddingCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();

            Console.WriteLine($"Libro con ID {libro.Id} y {libro.Embeddings?.Count ?? 0} embeddings insertados correctamente.");
        }

        public static void DeleteAllEmbeddings(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();

            try
            {
                var deleteCommand = connection.CreateCommand();
                deleteCommand.CommandText = "DELETE FROM embeddings";
                deleteCommand.ExecuteNonQuery();

                transaction.Commit();
                Console.WriteLine("Todos los embeddings han sido eliminados correctamente.");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"Error al eliminar los embeddings: {ex.Message}");
            }
        }


        public static List<LibroAPI> GetLibrosConEmbeddings(SqliteConnection connection)
        {
            var libros = new List<LibroAPI>();

            var selectLibrosCommand = connection.CreateCommand();
            selectLibrosCommand.CommandText = "SELECT * FROM libros";
            using var reader = selectLibrosCommand.ExecuteReader();
            while (reader.Read())
            {
                var libro = new LibroAPI
                {
                    Id = reader.GetInt32(0),
                    Codigo = reader.GetString(1),
                    Titulo = reader.GetString(2),
                    Autor = reader.GetString(3),
                    Resumen = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Stamp = reader.GetDateTime(5)
                };
                libros.Add(libro);
            }

            foreach (var libro in libros)
            {
                var selectEmbeddingsCommand = connection.CreateCommand();
                selectEmbeddingsCommand.CommandText = "SELECT embedding FROM embeddings WHERE libro_id = @libro_id";
                selectEmbeddingsCommand.Parameters.AddWithValue("@libro_id", libro.Id);

                using var embeddingReader = selectEmbeddingsCommand.ExecuteReader();
                while (embeddingReader.Read())
                {
                    libro.Embeddings.Add((byte[])embeddingReader["embedding"]);
                }
            }

            return libros;
        }

        public static List<LibroAPI> GetLibros(SqliteConnection connection)
        {
            var libros = new List<LibroAPI>();

            var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = "SELECT * FROM libros";
            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                var libro = new LibroAPI
                {
                    Id = reader.GetInt32(0),
                    Codigo = reader.GetString(1),
                    Titulo = reader.GetString(2),
                    Autor = reader.GetString(3),
                    Resumen = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Stamp = reader.GetDateTime(5),
                    //Embedding = (byte[])reader["embedding"]
                };
                libros.Add(libro);
            }

            return libros;
        }

        public static List<LibroAPI> GetAllLibros(SqliteConnection connection)
        {
            var libros = new List<LibroAPI>();

            var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = "SELECT * FROM libros";
            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                var libro = new LibroAPI
                {
                    Id = reader.GetInt32(0),
                    Codigo = reader.GetString(1),
                    Titulo = reader.GetString(2),
                    Autor = reader.GetString(3),
                    Resumen = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Stamp = reader.GetDateTime(5),
                };
                libros.Add(libro);
            }

            return libros;
        }

    }
}
