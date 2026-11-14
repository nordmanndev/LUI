using System.Reflection;
using Microsoft.Data.Sqlite;

Console.WriteLine("=== SqliteConnectionStringBuilder: oeffentliche Eigenschaften ===");
foreach (var property in typeof(SqliteConnectionStringBuilder).GetProperties(BindingFlags.Public | BindingFlags.Instance)
    .Where(p => p.CanWrite)
    .OrderBy(p => p.Name))
{
    Console.WriteLine($"  {property.PropertyType.Name,-28} {property.Name}");
}

Console.WriteLine();
Console.WriteLine("=== die geheimen Schluesselworte (private const) ===");
foreach (var field in typeof(SqliteConnectionStringBuilder)
    .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
    .OrderBy(f => (string?)f.GetValue(null)))
{
    Console.WriteLine($"  {(string?)field.GetValue(null)}");
}
