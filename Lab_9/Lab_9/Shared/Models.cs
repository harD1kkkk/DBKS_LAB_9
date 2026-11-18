using System.Data;
using System.Threading.Tasks;

namespace LibraryLabs.Shared;

public enum FieldKind { Text, Integer, Money, Date, Boolean }

public sealed record FieldSpec(string Name, string Label, FieldKind Kind = FieldKind.Text, bool Required = true);

public sealed record EntitySpec(
    string Name,
    string Title,
    FieldSpec[] Fields,
    string[] SearchFields);

public static class LibrarySchema
{
    public static readonly EntitySpec Readers = new(
        "Readers", "Читачі",
        [
            new("FullName", "ПІБ"),
            new("TicketNumber", "Номер квитка"),
            new("BirthYear", "Рік народження", FieldKind.Integer, false),
            new("RegistrationDate", "Дата запису", FieldKind.Date),
            new("ReaderType", "Тип читача")
        ],
        ["FullName", "TicketNumber"]);

    public static readonly EntitySpec Books = new(
        "Books", "Книги",
        [
            new("Title", "Назва"),
            new("Author", "Автор"),
            new("PublishYear", "Рік видання", FieldKind.Integer, false),
            new("Price", "Ціна", FieldKind.Money),
            new("Genre", "Жанр", FieldKind.Text, false),
            new("IsNewEdition", "Нове видання", FieldKind.Boolean),
            new("Annotation", "Коротка анотація"),
            new("IsAvailable", "Є в наявності", FieldKind.Boolean)
        ],
        ["Title", "Author", "Genre"]);

    public static readonly EntitySpec Loans = new(
        "Loans", "Видача книг",
        [
            new("ReaderId", "ID читача"),
            new("BookId", "ID книги"),
            new("IssueDate", "Дата видачі", FieldKind.Date),
            new("ReturnDate", "Дата повернення", FieldKind.Date, false),
            new("LibrarianName", "Працівник бібліотеки")
        ],
        ["ReaderId", "BookId"]);

    public static readonly EntitySpec[] All = [Readers, Books, Loans];
}

public interface ILibraryStore
{
    Task InitializeAsync();
    Task<DataTable> GetAsync(EntitySpec entity, string search);
    Task AddAsync(EntitySpec entity, IReadOnlyDictionary<string, object?> data);
    Task UpdateAsync(EntitySpec entity, string id, IReadOnlyDictionary<string, object?> data);
    Task DeleteAsync(EntitySpec entity, string id);
}
