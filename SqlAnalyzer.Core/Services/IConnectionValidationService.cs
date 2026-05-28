using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.Core.Services;

public interface IConnectionValidationService
{
    Task<string> ValidateConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
}
