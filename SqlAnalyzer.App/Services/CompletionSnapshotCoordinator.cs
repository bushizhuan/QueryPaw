using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.App.Services;

public sealed class CompletionSnapshotCoordinator
{
    private readonly IDatabaseExplorerService _databaseExplorerService;
    private readonly TimeSpan _warmupTimeout;
    private readonly HashSet<string> _warmupKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _warmupGate = new();

    public CompletionSnapshotCoordinator(IDatabaseExplorerService databaseExplorerService, TimeSpan warmupTimeout)
    {
        _databaseExplorerService = databaseExplorerService;
        _warmupTimeout = warmupTimeout;
    }

    public IReadOnlyList<CompletionEntry> GetCachedEntries(
        ConnectionProfile documentConnection,
        string preferredSchema,
        string completionContext,
        string? preferredObject,
        IReadOnlyList<CompletionController.CompletionRelationReference> relationReferences)
    {
        // 输入时先用缓存顶上，慢查询留给后面的异步刷新。
        if (CompletionMetadataRules.IsRelationContext(completionContext))
        {
            return _databaseExplorerService.GetCachedRelationCompletionSnapshot(documentConnection, preferredSchema);
        }

        if (CompletionMetadataRules.ShouldUseObjectColumnSnapshot(completionContext, preferredObject))
        {
            return _databaseExplorerService.GetCachedObjectColumnCompletionSnapshot(documentConnection, preferredObject!, preferredSchema);
        }

        if (CompletionMetadataRules.ShouldUseRelationColumnSnapshots(completionContext, relationReferences))
        {
            return GetRelationColumnEntriesAsync(documentConnection, preferredSchema, relationReferences, loadFromCacheOnly: true, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        return Array.Empty<CompletionEntry>();
    }

    public async Task<IReadOnlyList<CompletionEntry>> LoadEntriesAsync(
        ConnectionProfile documentConnection,
        string preferredSchema,
        string completionContext,
        string? preferredObject,
        IReadOnlyList<CompletionController.CompletionRelationReference> relationReferences,
        CancellationToken cancellationToken)
    {
        if (CompletionMetadataRules.IsRelationContext(completionContext))
        {
            return await _databaseExplorerService.LoadRelationCompletionSnapshotAsync(documentConnection, preferredSchema, cancellationToken);
        }

        if (CompletionMetadataRules.ShouldUseObjectColumnSnapshot(completionContext, preferredObject))
        {
            return await _databaseExplorerService.LoadObjectColumnCompletionSnapshotAsync(documentConnection, preferredObject!, preferredSchema, cancellationToken);
        }

        if (CompletionMetadataRules.ShouldUseRelationColumnSnapshots(completionContext, relationReferences))
        {
            return await GetRelationColumnEntriesAsync(documentConnection, preferredSchema, relationReferences, loadFromCacheOnly: false, cancellationToken);
        }

        return Array.Empty<CompletionEntry>();
    }

    public void QueueStandardWarmups(ConnectionProfile? profile, string preferredSchema)
    {
        if (profile == null)
        {
            return;
        }

        string normalizedSchema = string.IsNullOrWhiteSpace(preferredSchema) ? string.Empty : preferredSchema;
        QueueWarmup(profile, normalizedSchema, "relation", null);
    }

    public void QueueContextWarmup(
        ConnectionProfile profile,
        string preferredSchema,
        string completionContext,
        string? preferredObject,
        IReadOnlyList<CompletionController.CompletionRelationReference> relationReferences)
    {
        if (CompletionMetadataRules.ShouldUseRelationColumnSnapshots(completionContext, relationReferences))
        {
            foreach (CompletionController.CompletionRelationReference relation in relationReferences)
            {
                string relationSchema = CompletionMetadataRules.ResolveRelationSchema(relation, preferredSchema);
                QueueWarmup(profile, relationSchema, completionContext, relation.TableName);
            }

            return;
        }

        if (CompletionMetadataRules.IsContextSpecificCompletion(completionContext))
        {
            QueueWarmup(profile, preferredSchema, completionContext, preferredObject);
        }
    }

    private async Task<IReadOnlyList<CompletionEntry>> GetRelationColumnEntriesAsync(
        ConnectionProfile documentConnection,
        string preferredSchema,
        IReadOnlyList<CompletionController.CompletionRelationReference> relationReferences,
        bool loadFromCacheOnly,
        CancellationToken cancellationToken)
    {
        if (relationReferences.Count == 0)
        {
            return Array.Empty<CompletionEntry>();
        }

        bool includeRelationPrefix = relationReferences.Count > 1;
        List<CompletionEntry> entries = [];
        foreach (CompletionController.CompletionRelationReference relation in relationReferences)
        {
            string relationSchema = CompletionMetadataRules.ResolveRelationSchema(relation, preferredSchema);
            IReadOnlyList<CompletionEntry> relationColumns = loadFromCacheOnly
                ? _databaseExplorerService.GetCachedObjectColumnCompletionSnapshot(documentConnection, relation.TableName, relationSchema)
                : await _databaseExplorerService.LoadObjectColumnCompletionSnapshotAsync(documentConnection, relation.TableName, relationSchema, cancellationToken);

            foreach (CompletionEntry column in relationColumns)
            {
                // 多表场景下保留表名前缀，避免同名字段挤在一起看不清。
                entries.Add(CompletionMetadataRules.CreateRelationScopedColumnEntry(column, relation, relationSchema, includeRelationPrefix));
            }
        }

        return entries;
    }

    private void QueueWarmup(ConnectionProfile? profile, string preferredSchema, string completionContext, string? preferredObject)
    {
        if (profile == null)
        {
            return;
        }

        ConnectionProfile profileClone = ConnectionProfileUtilities.Clone(profile);
        string cacheKey = $"{profileClone.Id}:{preferredSchema}:{completionContext}:{preferredObject ?? string.Empty}";
        lock (_warmupGate)
        {
            if (!_warmupKeys.Add(cacheKey))
            {
                // 同一个连接和上下文只预热一次，省下数据库压力。
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                timeoutSource.CancelAfter(_warmupTimeout);
                if (CompletionMetadataRules.IsRelationContext(completionContext))
                {
                    await _databaseExplorerService.LoadRelationCompletionSnapshotAsync(profileClone, preferredSchema, timeoutSource.Token);
                }
                else if (CompletionMetadataRules.ShouldUseObjectColumnSnapshot(completionContext, preferredObject))
                {
                    await _databaseExplorerService.LoadObjectColumnCompletionSnapshotAsync(profileClone, preferredObject!, preferredSchema, timeoutSource.Token);
                }
                else
                {
                    await _databaseExplorerService.LoadCompletionSnapshotAsync(profileClone, preferredSchema, timeoutSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Completion metadata warmup failed: " + ex.Message);
            }
            finally
            {
                lock (_warmupGate)
                {
                    _warmupKeys.Remove(cacheKey);
                }
            }
        });
    }
}
