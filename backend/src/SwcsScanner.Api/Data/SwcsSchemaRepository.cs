using Dapper;

namespace SwcsScanner.Api.Data;

public sealed class SwcsSchemaRepository : ISchemaRepository
{
    private const int DefaultSchemaCacheMinutes = 10;

    private readonly SwcsRepositoryContext _context;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);

    private SwcsSchemaSnapshot? _cachedSchema;
    private DateTimeOffset _schemaExpiresAtUtc = DateTimeOffset.MinValue;

    internal SwcsSchemaRepository(SwcsRepositoryContext context)
    {
        _context = context;
    }

    public async Task<SwcsSchemaSnapshot> GetSchemaSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_cachedSchema is not null && _schemaExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return _cachedSchema;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSchema is not null && _schemaExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return _cachedSchema;
            }

            await using var connection = _context.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            const string columnSql = """
                SELECT c.name
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.object_id = OBJECT_ID(@ObjectName) AND o.type = 'U';
                """;

            var columns = (await connection.QueryAsync<string>(_context.CreateCommand(
                    columnSql,
                    new { ObjectName = _context.Options.ProductTable },
                    cancellationToken)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            const string functionSql = """
                SELECT CASE
                    WHEN OBJECT_ID('dbo.fn_strunitptype', 'FN') IS NOT NULL THEN 1
                    ELSE 0
                END;
                """;

            var hasFunction = await connection.ExecuteScalarAsync<int>(_context.CreateCommand(
                functionSql,
                null,
                cancellationToken)) == 1;

            _cachedSchema = new SwcsSchemaSnapshot
            {
                Columns = columns,
                HasBarcodeFunction = hasFunction
            };

            var cacheMinutes = _context.Options.SchemaCacheMinutes <= 0
                ? DefaultSchemaCacheMinutes
                : _context.Options.SchemaCacheMinutes;

            _schemaExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(cacheMinutes);
            return _cachedSchema;
        }
        finally
        {
            _schemaLock.Release();
        }
    }
}
