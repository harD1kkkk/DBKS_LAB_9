using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using LibraryLabs.Shared;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Lab_9;

public sealed class MongoLibraryStore : ILibraryStore
{
    private readonly IMongoDatabase _database;

    public MongoLibraryStore(string connectionString)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(12);
        settings.ConnectTimeout = TimeSpan.FromSeconds(10);
        var client = new MongoClient(settings);
        _database = client.GetDatabase("LibraryLab9");
    }

    private IMongoCollection<BsonDocument> Collection(string name) =>
        _database.GetCollection<BsonDocument>(name);

    public async Task InitializeAsync()
    {
        await _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));

        var empty = Builders<BsonDocument>.Filter.Empty;
        if (await Collection("Readers").CountDocumentsAsync(empty) != 0 ||
            await Collection("Books").CountDocumentsAsync(empty) != 0 ||
            await Collection("Loans").CountDocumentsAsync(empty) != 0)
            return;

        var reader1 = ObjectId.GenerateNewId();
        var reader2 = ObjectId.GenerateNewId();
        var book1 = ObjectId.GenerateNewId();
        var book2 = ObjectId.GenerateNewId();

        await Collection("Readers").InsertManyAsync(new[]
        {
            new BsonDocument
            {
                ["_id"] = new BsonObjectId(reader1),
                ["FullName"] = "Іван Петренко",
                ["TicketNumber"] = "R-001",
                ["BirthYear"] = 2002,
                ["RegistrationDate"] = UtcDate(2026, 9, 10),
                ["ReaderType"] = "Студент"
            },
            new BsonDocument
            {
                ["_id"] = new BsonObjectId(reader2),
                ["FullName"] = "Олена Ковальчук",
                ["TicketNumber"] = "R-002",
                ["BirthYear"] = 1998,
                ["RegistrationDate"] = UtcDate(2026, 9, 12),
                ["ReaderType"] = "Викладач"
            }
        });

        await Collection("Books").InsertManyAsync(new[]
        {
            new BsonDocument
            {
                ["_id"] = new BsonObjectId(book1),
                ["Title"] = "Кобзар",
                ["Author"] = "Тарас Шевченко",
                ["PublishYear"] = 1840,
                ["Price"] = 250.0,
                ["Genre"] = "Поезія",
                ["IsNewEdition"] = false,
                ["Annotation"] = "Збірка поетичних творів.",
                ["IsAvailable"] = true
            },
            new BsonDocument
            {
                ["_id"] = new BsonObjectId(book2),
                ["Title"] = "Тигролови",
                ["Author"] = "Іван Багряний",
                ["PublishYear"] = 1944,
                ["Price"] = 320.0,
                ["Genre"] = "Роман",
                ["IsNewEdition"] = true,
                ["Annotation"] = "Пригодницький роман.",
                ["IsAvailable"] = false
            }
        });

        await Collection("Loans").InsertOneAsync(new BsonDocument
        {
            ["ReaderId"] = new BsonObjectId(reader1),
            ["BookId"] = new BsonObjectId(book2),
            ["IssueDate"] = UtcDate(2026, 10, 20),
            ["ReturnDate"] = BsonNull.Value,
            ["LibrarianName"] = "Марія Мельник"
        });
    }

    public async Task<DataTable> GetAsync(EntitySpec entity, string search)
    {
        var table = new DataTable(entity.Name);
        table.Columns.Add("Id", typeof(string));
        foreach (FieldSpec field in entity.Fields)
            table.Columns.Add(field.Name, typeof(string));

        FilterDefinition<BsonDocument> filter = string.IsNullOrWhiteSpace(search)
            ? Builders<BsonDocument>.Filter.Empty
            : Builders<BsonDocument>.Filter.Or(
                entity.SearchFields.Select(name =>
                    Builders<BsonDocument>.Filter.Regex(name,
                        new BsonRegularExpression(Regex.Escape(search.Trim()), "i"))));

        var documents = await Collection(entity.Name).Find(filter).ToListAsync();
        foreach (var document in documents)
        {
            var row = table.NewRow();
            row["Id"] = document["_id"].AsObjectId.ToString();
            foreach (FieldSpec field in entity.Fields)
            {
                row[field.Name] = document.TryGetValue(field.Name, out BsonValue? value)
                    ? FormatValue(value)
                    : "";
            }
            table.Rows.Add(row);
        }
        return table;
    }

    public async Task AddAsync(EntitySpec entity, IReadOnlyDictionary<string, object?> values)
    {
        var document = ToDocument(entity, values);
        await ValidateDataAsync(entity, document, null);
        await Collection(entity.Name).InsertOneAsync(document);
    }

    public async Task UpdateAsync(EntitySpec entity, string id, IReadOnlyDictionary<string, object?> values)
    {
        var objectId = ParseId(id);
        var document = ToDocument(entity, values);
        await ValidateDataAsync(entity, document, objectId);
        var result = await Collection(entity.Name).ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", objectId), document);
        if (result.MatchedCount == 0)
            throw new InvalidOperationException("Вибраний запис не знайдено в базі.");
    }

    public async Task DeleteAsync(EntitySpec entity, string id)
    {
        var objectId = ParseId(id);
        if (entity.Name is "Readers" or "Books")
        {
            string foreignKey = entity.Name == "Readers" ? "ReaderId" : "BookId";
            long references = await Collection("Loans").CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Eq(foreignKey, objectId));
            if (references != 0)
                throw new InvalidOperationException(
                    "Спочатку видаліть пов'язані записи у вкладці «Видача книг».");
        }

        var result = await Collection(entity.Name).DeleteOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", objectId));
        if (result.DeletedCount == 0)
            throw new InvalidOperationException("Вибраний запис не знайдено в базі.");
    }

    private async Task ValidateDataAsync(EntitySpec entity, BsonDocument data, ObjectId? currentId)
    {
        if (entity.Name == "Readers")
        {
            var filter = Builders<BsonDocument>.Filter.Eq("TicketNumber", data["TicketNumber"]);
            if (currentId.HasValue)
                filter &= Builders<BsonDocument>.Filter.Ne("_id", currentId.Value);
            if (await Collection("Readers").CountDocumentsAsync(filter) != 0)
                throw new ArgumentException("Такий номер читацького квитка вже існує.");
        }

        if (entity.Name != "Loans") return;

        foreach (var reference in new[] { (Field: "ReaderId", Collection: "Readers"),
                                         (Field: "BookId", Collection: "Books") })
        {
            var id = data[reference.Field].AsObjectId;
            var found = await Collection(reference.Collection).Find(
                Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync();
            if (found is null)
                throw new ArgumentException($"Документ з ID «{id}» відсутній у {reference.Collection}.");
        }
    }

    private static BsonDocument ToDocument(EntitySpec entity, IReadOnlyDictionary<string, object?> values)
    {
        var document = new BsonDocument();
        foreach (FieldSpec field in entity.Fields)
        {
            values.TryGetValue(field.Name, out object? value);
            if (value is null)
            {
                document[field.Name] = BsonNull.Value;
                continue;
            }

            if (entity.Name == "Loans" && (field.Name is "ReaderId" or "BookId"))
            {
                document[field.Name] = new BsonObjectId(ParseId(Convert.ToString(value)!));
                continue;
            }

            document[field.Name] = field.Kind switch
            {
                FieldKind.Integer => new BsonInt32(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
                FieldKind.Money => new BsonDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
                FieldKind.Boolean => new BsonBoolean(Convert.ToBoolean(value, CultureInfo.InvariantCulture)),
                FieldKind.Date => new BsonDateTime(DateTime.SpecifyKind(
                    DateTime.ParseExact((string)value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateTimeKind.Utc)),
                _ => new BsonString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
            };
        }
        return document;
    }

    private static string FormatValue(BsonValue? value)
    {
        if (value is null || value.IsBsonNull) return "";
        return value.BsonType switch
        {
            BsonType.DateTime => value.AsBsonDateTime.ToUniversalTime()
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BsonType.Boolean => value.AsBoolean ? "True" : "False",
            BsonType.ObjectId => value.AsObjectId.ToString(),
            BsonType.Double => value.AsDouble.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static ObjectId ParseId(string value)
    {
        if (!ObjectId.TryParse(value, out var id))
            throw new ArgumentException("Некоректний ID документа MongoDB.");
        return id;
    }

    private static BsonDateTime UtcDate(int year, int month, int day) =>
        new(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));
}
