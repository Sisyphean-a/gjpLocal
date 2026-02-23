using SwcsScanner.Api.Data;

namespace SwcsScanner.Api.Services;

public sealed record ProductLookupMatch(DbProductLookupRow Row, string MatchedBy);
