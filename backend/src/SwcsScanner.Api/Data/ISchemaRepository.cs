namespace SwcsScanner.Api.Data;

public interface ISchemaRepository
{
    Task<SwcsSchemaSnapshot> GetSchemaSnapshotAsync(CancellationToken cancellationToken);
}
