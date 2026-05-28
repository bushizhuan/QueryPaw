using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface IDatabaseProviderCatalog
{
    IReadOnlyList<DatabaseProviderDefinition> GetAll();
    DatabaseProviderDefinition? Find(string providerName);
}
