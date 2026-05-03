using System.Text.Json;
using mastery_task.Models;
using Microsoft.Data.Sqlite;

namespace mastery_task.Data;

public class DocumentRepository
{
    private readonly string _connectionString;

    public DocumentRepository(IWebHostEnvironment env)
    {
        var dbPath = Path.Combine(env.ContentRootPath, "documents.db");
        _connectionString = $"Data Source={dbPath}";
        EnsureDatabase();
    }

    private void EnsureDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_file TEXT NOT NULL,
                document_type TEXT NULL,
                supplier_name TEXT NULL,
                document_number TEXT NULL,
                issue_date TEXT NULL,
                due_date TEXT NULL,
                currency TEXT NULL,
                subtotal REAL NULL,
                tax REAL NULL,
                total REAL NULL,
                line_items_json TEXT NOT NULL,
                issues_json TEXT NOT NULL,
                status TEXT NOT NULL,
                raw_text TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public List<DocumentRecord> GetAll()
    {
        var docs = new List<DocumentRecord>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM documents ORDER BY datetime(created_at_utc) DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            docs.Add(new DocumentRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                SourceFile = reader.GetString(reader.GetOrdinal("source_file")),
                DocumentType = reader["document_type"] as string,
                SupplierName = reader["supplier_name"] as string,
                DocumentNumber = reader["document_number"] as string,
                IssueDate = reader["issue_date"] as string,
                DueDate = reader["due_date"] as string,
                Currency = reader["currency"] as string,
                Subtotal = reader["subtotal"] is DBNull ? null : Convert.ToDecimal(reader["subtotal"]),
                Tax = reader["tax"] is DBNull ? null : Convert.ToDecimal(reader["tax"]),
                Total = reader["total"] is DBNull ? null : Convert.ToDecimal(reader["total"]),
                LineItems = JsonSerializer.Deserialize<List<LineItem>>(reader.GetString(reader.GetOrdinal("line_items_json"))) ?? [],
                Issues = JsonSerializer.Deserialize<List<ValidationIssue>>(reader.GetString(reader.GetOrdinal("issues_json"))) ?? [],
                Status = reader.GetString(reader.GetOrdinal("status")),
                RawText = reader.GetString(reader.GetOrdinal("raw_text")),
                CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at_utc"))),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at_utc")))
            });
        }

        return docs;
    }

    public DocumentRecord? GetById(int id) => GetAll().FirstOrDefault(d => d.Id == id);

    public HashSet<string> GetDocumentNumbers()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in GetAll())
        {
            if (!string.IsNullOrWhiteSpace(doc.DocumentNumber))
            {
                set.Add(doc.DocumentNumber);
            }
        }

        return set;
    }

    /// <summary>Document numbers from other rows (excludes current id) for duplicate checks after manual edit.</summary>
    public HashSet<string> GetDocumentNumbersExcluding(int excludeId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in GetAll())
        {
            if (doc.Id == excludeId)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(doc.DocumentNumber))
            {
                set.Add(doc.DocumentNumber);
            }
        }

        return set;
    }

    public void Insert(DocumentRecord doc)
    {
        var now = DateTime.UtcNow;
        doc.CreatedAtUtc = now;
        doc.UpdatedAtUtc = now;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (
                source_file, document_type, supplier_name, document_number, issue_date, due_date, currency,
                subtotal, tax, total, line_items_json, issues_json, status, raw_text, created_at_utc, updated_at_utc
            ) VALUES (
                $source_file, $document_type, $supplier_name, $document_number, $issue_date, $due_date, $currency,
                $subtotal, $tax, $total, $line_items_json, $issues_json, $status, $raw_text, $created_at_utc, $updated_at_utc
            );
            """;
        cmd.Parameters.AddWithValue("$source_file", doc.SourceFile);
        cmd.Parameters.AddWithValue("$document_type", (object?)doc.DocumentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$supplier_name", (object?)doc.SupplierName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$document_number", (object?)doc.DocumentNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$issue_date", (object?)doc.IssueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$due_date", (object?)doc.DueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$currency", (object?)doc.Currency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$subtotal", (object?)doc.Subtotal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tax", (object?)doc.Tax ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$total", (object?)doc.Total ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$line_items_json", JsonSerializer.Serialize(doc.LineItems));
        cmd.Parameters.AddWithValue("$issues_json", JsonSerializer.Serialize(doc.Issues));
        cmd.Parameters.AddWithValue("$status", doc.Status);
        cmd.Parameters.AddWithValue("$raw_text", doc.RawText);
        cmd.Parameters.AddWithValue("$created_at_utc", doc.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$updated_at_utc", doc.UpdatedAtUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public bool ExistsBySourceFile(string sourceFile)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM documents WHERE source_file = $source_file LIMIT 1);";
        cmd.Parameters.AddWithValue("$source_file", sourceFile);
        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result) == 1;
    }

    public void DeleteAll()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM documents;";
        cmd.ExecuteNonQuery();
    }

    public void UpdateReview(DocumentRecord doc)
    {
        doc.UpdatedAtUtc = DateTime.UtcNow;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE documents
            SET document_type = $document_type,
                supplier_name = $supplier_name,
                document_number = $document_number,
                issue_date = $issue_date,
                due_date = $due_date,
                currency = $currency,
                subtotal = $subtotal,
                tax = $tax,
                total = $total,
                line_items_json = $line_items_json,
                issues_json = $issues_json,
                raw_text = $raw_text,
                status = $status,
                updated_at_utc = $updated_at_utc
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", doc.Id);
        cmd.Parameters.AddWithValue("$document_type", (object?)doc.DocumentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$supplier_name", (object?)doc.SupplierName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$document_number", (object?)doc.DocumentNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$issue_date", (object?)doc.IssueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$due_date", (object?)doc.DueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$currency", (object?)doc.Currency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$subtotal", (object?)doc.Subtotal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tax", (object?)doc.Tax ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$total", (object?)doc.Total ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$line_items_json", JsonSerializer.Serialize(doc.LineItems));
        cmd.Parameters.AddWithValue("$issues_json", JsonSerializer.Serialize(doc.Issues));
        cmd.Parameters.AddWithValue("$raw_text", doc.RawText);
        cmd.Parameters.AddWithValue("$status", doc.Status);
        cmd.Parameters.AddWithValue("$updated_at_utc", doc.UpdatedAtUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
