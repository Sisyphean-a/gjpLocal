using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Data;

internal sealed class SwcsRepositoryContext
{
    public SwcsRepositoryContext(IConfiguration configuration, IOptions<SwcsOptions> options)
    {
        Options = options.Value;
        ConnectionString = configuration.GetConnectionString("SwcsReadonly")
                           ?? throw new InvalidOperationException("缺少连接字符串 ConnectionStrings:SwcsReadonly。");
        QuotedProductTable = SwcsSqlHelper.QuoteTableName(Options.ProductTable);
        QuotedBarcodeTable = string.IsNullOrWhiteSpace(Options.BarcodeTable) ? null : SwcsSqlHelper.QuoteTableName(Options.BarcodeTable);
        QuotedBarcodeColumn = string.IsNullOrWhiteSpace(Options.BarcodeColumn) ? null : SwcsSqlHelper.QuoteIdentifier(Options.BarcodeColumn);
        QuotedPriceTable = string.IsNullOrWhiteSpace(Options.PriceTable) ? null : SwcsSqlHelper.QuoteTableName(Options.PriceTable);
        QuotedPriceColumn = string.IsNullOrWhiteSpace(Options.PriceColumn) ? null : SwcsSqlHelper.QuoteIdentifier(Options.PriceColumn);
        ProductNameField = SwcsSqlHelper.QuoteIdentifier(Options.ProductNameField);
        PreferredPriceTypeId = string.IsNullOrWhiteSpace(Options.PriceTypeId) ? null : Options.PriceTypeId.Trim();
        UseUnitScopedBarcodePrice = SwcsSqlHelper.IsXwPtypePriceTable(Options.PriceTable);
        QueryTimeoutSeconds = Options.QueryTimeoutSeconds;
    }

    public SwcsOptions Options { get; }

    public string ConnectionString { get; }

    public string QuotedProductTable { get; }

    public string? QuotedBarcodeTable { get; }

    public string? QuotedBarcodeColumn { get; }

    public string? QuotedPriceTable { get; }

    public string? QuotedPriceColumn { get; }

    public string ProductNameField { get; }

    public string? PreferredPriceTypeId { get; }

    public bool UseUnitScopedBarcodePrice { get; }

    public int QueryTimeoutSeconds { get; }

    public SqlConnection CreateConnection()
    {
        return new SqlConnection(ConnectionString);
    }

    public CommandDefinition CreateCommand(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            sql,
            parameters,
            commandTimeout: QueryTimeoutSeconds,
            cancellationToken: cancellationToken);
    }
}
