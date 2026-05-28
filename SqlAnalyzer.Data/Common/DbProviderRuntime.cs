using System.Data.Common;
using System.Globalization;
using System.Reflection;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.Data.Common;

internal sealed class DbProviderRuntime
{
    private readonly IDatabaseProviderCatalog _providerCatalog;
    public DbProviderRuntime(IDatabaseProviderCatalog providerCatalog)
    {
        _providerCatalog = providerCatalog;
    }
    public DatabaseProviderDefinition GetProvider(ConnectionProfile profile)
    {
        return _providerCatalog.Find(profile.ProviderName)
            ?? throw new InvalidOperationException($"Provider '{profile.ProviderName}' is not registered.");
    }
    public string BuildConnectionString(DatabaseProviderDefinition provider, ConnectionProfile profile)
    {
        bool templateUsesDedicatedPort = (provider.ConnectionTemplate ?? string.Empty)
            .Contains("{port}", StringComparison.OrdinalIgnoreCase);
        string serverValue = BuildServerValue(provider, profile, templateUsesDedicatedPort);
        if (string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            serverValue = BuildOracleDataSource(profile);
        }

        string portValue = profile.Port > 0
            ? profile.Port.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        string connectionString = (provider.ConnectionTemplate ?? string.Empty)
            .Replace("{server}", serverValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{port}", portValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{database}", profile.Database ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", profile.UserName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{password}", profile.Password ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(profile.AdvancedOptions))
        {
            string advanced = profile.AdvancedOptions.Trim().TrimStart(';');
            if (advanced.Length > 0)
            {
                if (!connectionString.EndsWith(';'))
                {
                    connectionString += ";";
                }

                connectionString += advanced;
            }
        }

        if (string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(profile.AuthenticationMode, "SYSDBA", StringComparison.OrdinalIgnoreCase))
            {
                connectionString += ";DBA Privilege=SYSDBA";
            }
            else if (string.Equals(profile.AuthenticationMode, "SYSOPER", StringComparison.OrdinalIgnoreCase))
            {
                connectionString += ";DBA Privilege=SYSOPER";
            }
        }
        else if (string.Equals(provider.Name, "SqlServer", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(profile.AuthenticationMode, "IntegratedSecurity", StringComparison.OrdinalIgnoreCase))
        {
            connectionString += ";Integrated Security=True";
        }

        return connectionString;
    }
    private static string BuildServerValue(DatabaseProviderDefinition provider, ConnectionProfile profile, bool templateUsesDedicatedPort)
    {
        string server = profile.Server ?? string.Empty;
        if (string.IsNullOrWhiteSpace(server))
        {
            return server;
        }

        if (string.Equals(provider.Name, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return server;
        }

        if (templateUsesDedicatedPort ||
            profile.Port <= 0 ||
            server.Contains(":", StringComparison.Ordinal) ||
            server.Contains(",", StringComparison.Ordinal) ||
            server.Contains(";", StringComparison.Ordinal))
        {
            // 用户手写过端口或实例名时，不再帮他追加端口。
            return server;
        }

        return $"{server}:{profile.Port}";
    }
    private static string BuildOracleDataSource(ConnectionProfile profile)
    {
        if (string.Equals(profile.OracleConnectionMode, "Tns", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(profile.OracleTnsName)
                ? (profile.Server ?? string.Empty)
                : profile.OracleTnsName;
        }

        string host = string.IsNullOrWhiteSpace(profile.OracleHost) ? (profile.Server ?? string.Empty) : profile.OracleHost;
        string serviceName = string.IsNullOrWhiteSpace(profile.OracleServiceName) ? (profile.Database ?? string.Empty) : profile.OracleServiceName;
        int port = profile.OraclePort <= 0 ? 1521 : profile.OraclePort;

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return host;
        }

        if (host.Contains("/", StringComparison.Ordinal) || host.Contains("(", StringComparison.Ordinal) || host.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase))
        {
            // 已经是完整 Oracle 连接描述，直接交给驱动处理。
            return host;
        }

        if (host.Contains(":", StringComparison.Ordinal))
        {
            return $"{host}/{serviceName}";
        }

        return $"{host}:{port}/{serviceName}";
    }
    public DbProviderFactory ResolveFactory(DatabaseProviderDefinition provider, ConnectionProfile profile)
    {
        DbProviderFactory? registeredFactory = TryGetRegisteredFactory(provider);
        if (registeredFactory != null)
        {
            return registeredFactory;
        }

        LoadProviderAssemblies(provider, profile);

        registeredFactory = TryGetRegisteredFactory(provider);
        if (registeredFactory != null)
        {
            return registeredFactory;
        }

        Type? factoryType = null;
        string[] factoryTypeCandidates = BuildFactoryTypeCandidates(provider).ToArray();
        foreach (string candidate in factoryTypeCandidates)
        {
            factoryType = FindType(candidate);
            if (factoryType != null)
            {
                break;
            }
        }

        if (factoryType == null)
        {
            string displayName = string.Join(", ", factoryTypeCandidates);
            throw new InvalidOperationException($"Factory type '{displayName}' could not be loaded.");
        }

        FieldInfo? instanceField = factoryType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        if (instanceField?.GetValue(null) is DbProviderFactory fieldFactory)
        {
            return fieldFactory;
        }

        PropertyInfo? instanceProperty = factoryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        if (instanceProperty?.GetValue(null) is DbProviderFactory propertyFactory)
        {
            return propertyFactory;
        }

        if (Activator.CreateInstance(factoryType) is DbProviderFactory createdFactory)
        {
            return createdFactory;
        }

        throw new InvalidOperationException($"Unable to create provider factory '{provider.FactoryTypeName}'.");
    }
    private static DbProviderFactory? TryGetRegisteredFactory(DatabaseProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.InvariantName))
        {
            return null;
        }

        try
        {
            return DbProviderFactories.GetFactory(provider.InvariantName);
        }
        catch
        {
            return null;
        }
    }
    private static void LoadProviderAssemblies(DatabaseProviderDefinition provider, ConnectionProfile profile)
    {
        ApplyNativeDependencyPath(provider, profile);

        foreach (string path in ResolveManagedAssemblyPaths(provider, profile))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (File.Exists(path))
            {
                string fullPath = Path.GetFullPath(path);
                AssemblyName name = AssemblyName.GetAssemblyName(fullPath);
                if (AppDomain.CurrentDomain.GetAssemblies().Any(item =>
                        string.Equals(item.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Assembly.LoadFrom(fullPath);
                continue;
            }

            string assemblyName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                continue;
            }

            if (AppDomain.CurrentDomain.GetAssemblies().Any(item =>
                    string.Equals(item.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                Assembly.Load(assemblyName);
            }
            catch (Exception ex)
            {
                if (path.Contains('\\') || path.Contains('/') || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FileNotFoundException($"Database driver library not found: {path}", ex);
                }
            }
        }
    }
    private static void ApplyNativeDependencyPath(DatabaseProviderDefinition provider, ConnectionProfile profile)
    {
        List<string> candidatePaths = [];
        if (!string.IsNullOrWhiteSpace(profile.NativeLibraryPath))
        {
            candidatePaths.Add(profile.NativeLibraryPath);
        }

        if (!string.IsNullOrWhiteSpace(provider.DefaultNativeDependency))
        {
            string nativeDependency = provider.DefaultNativeDependency;
            if (Path.IsPathRooted(nativeDependency) && File.Exists(nativeDependency))
            {
                candidatePaths.Add(Path.GetDirectoryName(nativeDependency) ?? string.Empty);
            }
        }

        foreach (string candidatePath in candidatePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string normalized = candidatePath;
            if (File.Exists(normalized))
            {
                normalized = Path.GetDirectoryName(normalized) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
            {
                continue;
            }

            PrependEnvironmentPath("PATH", normalized);
            if (OperatingSystem.IsLinux())
            {
                PrependEnvironmentPath("LD_LIBRARY_PATH", normalized);
            }

            if (OperatingSystem.IsMacOS())
            {
                PrependEnvironmentPath("DYLD_LIBRARY_PATH", normalized);
            }
        }
    }
    private static void PrependEnvironmentPath(string variableName, string path)
    {
        string current = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
        string separator = Path.PathSeparator.ToString();
        string[] parts = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string updated = string.IsNullOrWhiteSpace(current) ? path : path + separator + current;
        Environment.SetEnvironmentVariable(variableName, updated);
    }
    private static IEnumerable<string> ResolveManagedAssemblyPaths(DatabaseProviderDefinition provider, ConnectionProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ManagedDriverPath))
        {
            yield return profile.ManagedDriverPath;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(provider.DefaultManagedDriver))
        {
            yield return provider.DefaultManagedDriver;
        }
    }
    private static IEnumerable<string> BuildFactoryTypeCandidates(DatabaseProviderDefinition provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.FactoryTypeName))
        {
            yield return provider.FactoryTypeName;
        }

        foreach (string alias in provider.FactoryTypeAliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return alias;
            }
        }
    }
    private static Type? FindType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        Type? type = Type.GetType(typeName, false);
        if (type != null)
        {
            return type;
        }

        string[] parts = typeName.Split(',');
        if (parts.Length > 1)
        {
            string assemblyName = parts[1].Trim();
            try
            {
                Assembly.Load(assemblyName);
                type = Type.GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }
            catch
            {
            }
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(parts[0].Trim(), false) ?? assembly.GetType(typeName, false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}
