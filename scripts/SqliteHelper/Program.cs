using Microsoft.Data.Sqlite;

// Usage: dotnet run --project SqliteHelper -- <dbPath> [keys|query <sql>]
if (args.Length < 2) { Console.Error.WriteLine("Usage: SqliteHelper <dbPath> keys|query <sql>"); return 1; }

var dbPath = args[0];
var mode = args[1];

using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
conn.Open();

if (mode == "keys")
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT key FROM ItemTable ORDER BY key";
    using var reader = cmd.ExecuteReader();
    while (reader.Read()) Console.WriteLine(reader.GetString(0));
}
else if (mode == "query" && args.Length >= 3)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = args[2];
    // Bind remaining args as @p1, @p2, etc
    for (int i = 3; i < args.Length; i++)
        cmd.Parameters.AddWithValue($"@p{i - 2}", args[i]);
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var cols = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            cols[i] = reader.IsDBNull(i) ? "" : reader.GetString(i);
        Console.WriteLine(string.Join("|||", cols));
    }
}
return 0;
