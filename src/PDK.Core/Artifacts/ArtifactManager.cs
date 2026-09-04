namespace PDK.Core.Artifacts;

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PDK.Core.Configuration;

/// <summary>
/// Manages artifact upload, download, and lifecycle operations.
/// </summary>
/// <remarks>
/// <para>
/// Storage layout: <c>&lt;basePath&gt;/run-&lt;runId&gt;/job-&lt;job&gt;/step-&lt;index&gt;-&lt;step&gt;/artifact-&lt;name&gt;/</c>
/// containing <c>artifact.metadata.json</c> plus exactly one content representation: an archive
/// (<c>artifact.zip</c> / <c>artifact.tar.gz</c>) when compression is enabled, otherwise the file
/// tree under <c>files/</c>.
/// </para>
/// <para>
/// The store root is always derived from <see cref="ArtifactContext.WorkspacePath"/> (or the current
/// working directory for the context-less overloads). <see cref="ArtifactContext.SourcePath"/> only
/// affects which files an upload selects.
/// </para>
/// </remarks>
public class ArtifactManager : IArtifactManager
{
    private readonly IConfiguration _configuration;
    private readonly IFileSelector _fileSelector;
    private readonly IArtifactCompressor _compressor;
    private readonly ILogger<ArtifactManager>? _logger;

    /// <summary>
    /// Name of the metadata file stored inside every artifact directory.
    /// </summary>
    public const string MetadataFileName = "artifact.metadata.json";

    /// <summary>
    /// Name of the directory holding the uncompressed content tree.
    /// </summary>
    public const string FilesDirectoryName = "files";

    /// <summary>
    /// Base name of the archive stored inside the artifact directory (extension depends on the compression).
    /// </summary>
    public const string ArchiveBaseName = "artifact";

    private const string DefaultBasePath = ".pdk/artifacts";
    private const string BasePathConfigKey = "artifacts.basePath";
    private const string RetentionConfigKey = "artifacts.retentionDays";
    private const string RunDirectoryPrefix = "run-";
    private const string JobDirectoryPrefix = "job-";
    private const string StepDirectoryPrefix = "step-";

    private static readonly string[] RunIdFormats = { "yyyyMMdd-HHmmss-fff", "yyyyMMdd-HHmmss" };

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactManager"/> class.
    /// </summary>
    /// <param name="configuration">The configuration provider.</param>
    /// <param name="fileSelector">The file selector for glob patterns.</param>
    /// <param name="compressor">The artifact compressor.</param>
    /// <param name="logger">Optional logger.</param>
    public ArtifactManager(
        IConfiguration configuration,
        IFileSelector fileSelector,
        IArtifactCompressor compressor,
        ILogger<ArtifactManager>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _fileSelector = fileSelector ?? throw new ArgumentNullException(nameof(fileSelector));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _logger = logger;
    }

    #region Upload

    /// <inheritdoc/>
    public async Task<UploadResult> UploadAsync(
        string artifactName,
        IEnumerable<string> patterns,
        ArtifactContext context,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(context);
        ArtifactNames.Validate(artifactName);
        options ??= ArtifactOptions.Default;

        var basePath = GetArtifactsBasePath(context.WorkspacePath);
        var artifactPath = context.GetArtifactPath(basePath, artifactName);
        var sourceDirectory = Path.GetFullPath(context.EffectiveSourcePath);

        var patternList = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        _logger?.LogDebug("Uploading artifact '{ArtifactName}' from {Source} to {Path}", artifactName, sourceDirectory, artifactPath);

        // Check if already exists
        if (!options.OverwriteExisting && Directory.Exists(artifactPath))
        {
            throw ArtifactException.AlreadyExists(artifactName);
        }

        var selection = SelectFiles(sourceDirectory, patternList);

        if (selection.Count == 0)
        {
            var warnings = HandleNoFilesFound(patternList, sourceDirectory, options.IfNoFilesFound);
            return new UploadResult
            {
                ArtifactName = artifactName,
                FileCount = 0,
                TotalSizeBytes = 0,
                StoragePath = artifactPath,
                RunId = context.RunId,
                Warnings = warnings
            };
        }

        // Replace any previous content of this artifact (including the legacy sibling archives).
        if (Directory.Exists(artifactPath))
        {
            Directory.Delete(artifactPath, recursive: true);
        }

        DeleteLegacyArchives(artifactPath);

        var completed = false;
        try
        {
            Directory.CreateDirectory(artifactPath);

            List<ArtifactFileInfo> fileInfos;
            long? compressedSize = null;

            if (options.Compression == CompressionType.None)
            {
                var filesDirectory = Path.Combine(artifactPath, FilesDirectoryName);
                fileInfos = await CopyFilesAsync(selection, filesDirectory, progress, cancellationToken);
            }
            else
            {
                fileInfos = await DescribeFilesAsync(selection, cancellationToken);

                var archivePath = Path.Combine(artifactPath, ArchiveBaseName + _compressor.GetExtension(options.Compression));
                var entries = selection
                    .Select(f => new ArchiveFileEntry(f.AbsolutePath, f.ArtifactPath))
                    .ToList();

                await _compressor.CompressFilesAsync(entries, archivePath, options.Compression, progress, cancellationToken);
                compressedSize = new FileInfo(archivePath).Length;

                _logger?.LogDebug("Compressed artifact '{ArtifactName}' to {Size} bytes", artifactName, compressedSize);
            }

            var totalSize = fileInfos.Sum(f => f.SizeBytes);
            var metadata = CreateMetadata(artifactName, context, fileInfos, options, compressedSize, GetConfiguredRetentionDays());
            await WriteMetadataAsync(artifactPath, metadata, cancellationToken);

            _logger?.LogInformation("Uploaded artifact '{ArtifactName}' with {FileCount} files ({TotalSize} bytes)",
                artifactName, fileInfos.Count, totalSize);

            completed = true;

            return new UploadResult
            {
                ArtifactName = artifactName,
                FileCount = fileInfos.Count,
                TotalSizeBytes = totalSize,
                CompressedSizeBytes = compressedSize,
                StoragePath = artifactPath,
                RunId = context.RunId
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            throw ArtifactException.PermissionDenied(artifactPath, ex);
        }
        finally
        {
            if (!completed)
            {
                TryDeleteDirectory(artifactPath);
            }
        }
    }

    #endregion

    #region Download

    /// <inheritdoc/>
    public Task<DownloadResult> DownloadAsync(
        string artifactName,
        string targetPath,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return DownloadAsync(CurrentDirectoryContext(), artifactName, targetPath, options, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DownloadResult> DownloadAsync(
        ArtifactContext context,
        string? artifactName,
        string targetPath,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Target path cannot be null or empty.", nameof(targetPath));
        }

        if (string.IsNullOrWhiteSpace(artifactName))
        {
            return await DownloadAllAsync(context, targetPath, progress, cancellationToken);
        }

        var warnings = new List<string>();
        var artifact = await LocateArtifactAsync(context, artifactName, warnings)
                       ?? throw ArtifactException.NotFound(artifactName);

        _logger?.LogDebug("Downloading artifact '{ArtifactName}' from {Path} to {Target}",
            artifactName, artifact.StoragePath, targetPath);

        var metadata = await LoadMetadataAsync(Path.Combine(artifact.StoragePath, MetadataFileName), cancellationToken)
                       ?? throw ArtifactException.CorruptMetadata(Path.Combine(artifact.StoragePath, MetadataFileName));

        var fileCount = await ExtractArtifactAsync(artifact.StoragePath, metadata, targetPath, progress, cancellationToken);

        _logger?.LogInformation("Downloaded artifact '{ArtifactName}' with {FileCount} files to {Target}",
            artifactName, fileCount, targetPath);

        return new DownloadResult
        {
            ArtifactName = metadata.Artifact.Name,
            FileCount = fileCount,
            TargetPath = targetPath,
            RunId = artifact.RunId,
            Artifacts = new[] { metadata.Artifact.Name },
            Warnings = warnings
        };
    }

    private async Task<DownloadResult> DownloadAllAsync(
        ArtifactContext context,
        string targetPath,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var hasCurrentRun = !string.IsNullOrEmpty(context.RunId);

        var artifacts = hasCurrentRun
            ? (await ListAsync(context, context.RunId)).ToList()
            : new List<ArtifactListItem>();

        string? runId = hasCurrentRun ? context.RunId : null;

        if (artifacts.Count == 0)
        {
            var all = (await ListAsync(context)).ToList();
            if (all.Count == 0)
            {
                warnings.Add("No artifacts found in the artifact store. Nothing was downloaded.");
                Directory.CreateDirectory(targetPath);
                return new DownloadResult
                {
                    ArtifactName = string.Empty,
                    FileCount = 0,
                    TargetPath = targetPath,
                    RunId = runId,
                    Warnings = warnings
                };
            }

            runId = all[0].RunId;
            artifacts = all.Where(a => a.RunId == runId).ToList();

            if (hasCurrentRun)
            {
                warnings.Add($"The current run ({context.RunId}) has no artifacts; using artifacts from run {runId}.");
            }
        }

        // One directory per artifact name; the newest upload of a name wins.
        var byName = artifacts
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => a.UploadedAt).First())
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ToList();

        var total = 0;
        var names = new List<string>();

        foreach (var item in byName)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadataPath = Path.Combine(item.StoragePath, MetadataFileName);
            var metadata = await LoadMetadataAsync(metadataPath, cancellationToken)
                           ?? throw ArtifactException.CorruptMetadata(metadataPath);

            var destination = Path.Combine(targetPath, ArtifactNames.SanitizeForFileSystem(item.Name));
            total += await ExtractArtifactAsync(item.StoragePath, metadata, destination, progress, cancellationToken);
            names.Add(item.Name);
        }

        _logger?.LogInformation("Downloaded {Count} artifact(s) with {FileCount} files to {Target}", names.Count, total, targetPath);

        return new DownloadResult
        {
            ArtifactName = string.Empty,
            FileCount = total,
            TargetPath = targetPath,
            RunId = runId,
            Artifacts = names,
            Warnings = warnings
        };
    }

    private async Task<ArtifactListItem?> LocateArtifactAsync(ArtifactContext context, string artifactName, List<string> warnings)
    {
        if (!string.IsNullOrEmpty(context.RunId))
        {
            var current = (await ListAsync(context, context.RunId))
                .FirstOrDefault(a => NameEquals(a.Name, artifactName));

            if (current != null)
            {
                return current;
            }
        }

        var fallback = (await ListAsync(context))
            .Where(a => NameEquals(a.Name, artifactName))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault();

        if (fallback != null && !string.IsNullOrEmpty(context.RunId))
        {
            var warning = $"Artifact '{artifactName}' was not produced by the current run ({context.RunId}); " +
                          $"using artifact from run {fallback.RunId} (uploaded {fallback.UploadedAt:yyyy-MM-dd HH:mm:ss} UTC).";
            warnings.Add(warning);
            _logger?.LogWarning("{Warning}", warning);
        }

        return fallback;
    }

    private async Task<int> ExtractArtifactAsync(
        string storagePath,
        ArtifactMetadata metadata,
        string targetPath,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(targetPath);

            if (metadata.Artifact.Compression != CompressionType.None)
            {
                var archivePath = FindArchive(storagePath, metadata.Artifact.Compression);
                if (archivePath != null)
                {
                    await _compressor.DecompressAsync(archivePath, targetPath, progress, cancellationToken);
                    return metadata.Summary.FileCount;
                }

                // No archive: fall back to a stored tree (legacy artifacts kept both).
            }

            var treePath = Path.Combine(storagePath, FilesDirectoryName);
            if (!Directory.Exists(treePath))
            {
                treePath = storagePath;
            }

            return await CopyFilesToTargetAsync(treePath, targetPath, metadata.Files, progress, cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw ArtifactException.PermissionDenied(targetPath, ex);
        }
    }

    private string? FindArchive(string storagePath, CompressionType compression)
    {
        var extension = _compressor.GetExtension(compression);

        var current = Path.Combine(storagePath, ArchiveBaseName + extension);
        if (File.Exists(current))
        {
            return current;
        }

        var legacy = storagePath + extension;
        return File.Exists(legacy) ? legacy : null;
    }

    #endregion

    #region List / Exists / Delete

    /// <inheritdoc/>
    public Task<IEnumerable<ArtifactListItem>> ListAsync(string? runId = null)
    {
        return ListAsync(CurrentDirectoryContext(), runId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ArtifactListItem>> ListAsync(ArtifactContext context, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var basePath = GetArtifactsBasePath(context.WorkspacePath);
        if (!Directory.Exists(basePath))
        {
            return Enumerable.Empty<ArtifactListItem>();
        }

        var results = new List<ArtifactListItem>();

        foreach (var runDir in GetRunDirectories(basePath))
        {
            var currentRunId = GetRunId(runDir);

            if (!string.IsNullOrEmpty(runId) && !string.Equals(currentRunId, runId, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var artifactDir in FindArtifactDirectories(runDir))
            {
                var metadataPath = Path.Combine(artifactDir, MetadataFileName);
                var metadata = await LoadMetadataAsync(metadataPath, CancellationToken.None);
                if (metadata == null)
                {
                    continue;
                }

                results.Add(new ArtifactListItem
                {
                    Name = metadata.Artifact.Name,
                    RunId = currentRunId,
                    JobName = metadata.Artifact.Job,
                    StepName = metadata.Artifact.Step,
                    UploadedAt = metadata.Artifact.UploadedAt,
                    FileCount = metadata.Summary.FileCount,
                    TotalSizeBytes = metadata.Summary.TotalSizeBytes,
                    StoragePath = artifactDir,
                    Compression = metadata.Artifact.Compression,
                    RetentionDays = metadata.Artifact.RetentionDays
                });
            }
        }

        return results.OrderByDescending(a => a.UploadedAt).ToList();
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string artifactName, string? runId = null)
    {
        return ExistsAsync(CurrentDirectoryContext(), artifactName, runId);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(ArtifactContext context, string artifactName, string? runId = null)
    {
        if (string.IsNullOrWhiteSpace(artifactName))
        {
            return false;
        }

        var artifacts = await ListAsync(context, runId);
        return artifacts.Any(a => NameEquals(a.Name, artifactName));
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string artifactName, string? runId = null)
    {
        return DeleteAsync(CurrentDirectoryContext(), artifactName, runId);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(ArtifactContext context, string artifactName, string? runId = null)
    {
        var artifacts = (await ListAsync(context, runId))
            .Where(a => NameEquals(a.Name, artifactName))
            .ToList();

        foreach (var artifact in artifacts)
        {
            _logger?.LogDebug("Deleting artifact '{ArtifactName}' from {Path}", artifactName, artifact.StoragePath);
            DeleteArtifactDirectory(artifact.StoragePath);
        }

        _logger?.LogInformation("Deleted {Count} artifact(s) named '{ArtifactName}'", artifacts.Count, artifactName);
    }

    #endregion

    #region Cleanup

    /// <inheritdoc/>
    public Task<int> CleanupAsync(int retentionDays)
    {
        return CleanupAsync(CurrentDirectoryContext(), retentionDays);
    }

    /// <inheritdoc/>
    public async Task<int> CleanupAsync(ArtifactContext context, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (retentionDays <= 0)
        {
            _logger?.LogDebug("Artifact cleanup is disabled (retention {Days})", retentionDays);
            return 0;
        }

        var basePath = GetArtifactsBasePath(context.WorkspacePath);
        if (!Directory.Exists(basePath))
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var cutoffDate = now.AddDays(-retentionDays);
        var deletedCount = 0;

        foreach (var runDir in GetRunDirectories(basePath))
        {
            var runId = GetRunId(runDir);

            // The run that is currently executing is never touched.
            if (!string.IsNullOrEmpty(context.RunId) && string.Equals(runId, context.RunId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryParseRunTimestamp(runId, out var runTimestamp))
            {
                _logger?.LogDebug("Skipping run directory with unrecognized name: {Path}", runDir);
                continue;
            }

            try
            {
                var artifactDirs = FindArtifactDirectories(runDir);
                var remaining = artifactDirs.Count;

                foreach (var artifactDir in artifactDirs)
                {
                    var metadata = await LoadMetadataAsync(Path.Combine(artifactDir, MetadataFileName), CancellationToken.None);

                    // Age is measured from the earliest known creation time: the run timestamp or the
                    // upload time, whichever is older.
                    var createdAt = runTimestamp;
                    if (metadata != null)
                    {
                        var uploadedAt = metadata.Artifact.UploadedAt.Kind == DateTimeKind.Utc
                            ? metadata.Artifact.UploadedAt
                            : metadata.Artifact.UploadedAt.ToUniversalTime();

                        if (uploadedAt < createdAt)
                        {
                            createdAt = uploadedAt;
                        }
                    }

                    var effectiveRetention = metadata?.Artifact.RetentionDays is > 0
                        ? metadata.Artifact.RetentionDays.Value
                        : retentionDays;

                    if (createdAt.AddDays(effectiveRetention) < now)
                    {
                        _logger?.LogDebug("Deleting expired artifact: {Path} (created {Timestamp}, retention {Days} days)",
                            artifactDir, createdAt, effectiveRetention);
                        DeleteArtifactDirectory(artifactDir);
                        deletedCount++;
                        remaining--;
                    }
                }

                if (remaining == 0 && (artifactDirs.Count > 0 || runTimestamp < cutoffDate))
                {
                    _logger?.LogDebug("Deleting run directory without artifacts: {Path}", runDir);
                    Directory.Delete(runDir, recursive: true);
                }
            }
            catch (IOException ex)
            {
                _logger?.LogWarning(ex, "Failed to clean up run directory: {Path}", runDir);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogWarning(ex, "Failed to clean up run directory: {Path}", runDir);
            }
        }

        _logger?.LogInformation("Cleaned up {Count} artifact(s) older than {Days} days", deletedCount, retentionDays);
        return deletedCount;
    }

    #endregion

    #region File selection

    private sealed record SelectedFile(string AbsolutePath, string SourcePath, string ArtifactPath);

    private sealed record IncludePattern(string Original, string AbsolutePattern, string SearchPath);

    /// <summary>
    /// Selects the files for an upload and computes their paths inside the artifact.
    /// Patterns are resolved against <paramref name="sourceDirectory"/>; absolute patterns are honoured.
    /// The artifact root is the parent directory for a single file, the directory itself for a single
    /// directory or glob prefix, and the least common ancestor of all search paths otherwise.
    /// </summary>
    private List<SelectedFile> SelectFiles(string sourceDirectory, IReadOnlyList<string> patterns)
    {
        var comparison = ArtifactPathResolver.PathComparison;
        var source = ArtifactPathResolver.NormalizeAbsolute(sourceDirectory);

        var includes = new List<IncludePattern>();
        var excludes = new List<string>();

        foreach (var raw in patterns)
        {
            var isExclude = ArtifactPathResolver.IsExclusion(raw);
            var body = ArtifactPathResolver.Normalize(isExclude ? raw.TrimStart()[1..] : raw);

            if (isExclude)
            {
                if (body.Length > 0)
                {
                    excludes.Add(ArtifactPathResolver.IsAbsolute(body) ? NormalizeAbsolutePattern(body) : body);
                }

                continue;
            }

            var absolutePattern = ArtifactPathResolver.IsAbsolute(body)
                ? NormalizeAbsolutePattern(body)
                : ArtifactPathResolver.Combine(source, body);

            includes.Add(new IncludePattern(raw, absolutePattern, ArtifactPathResolver.GetSearchPath(absolutePattern)));
        }

        var files = new Dictionary<string, string>(ArtifactPathResolver.PathComparer);

        if (includes.Count == 0)
        {
            return new List<SelectedFile>();
        }

        // Patterns inside the source directory are evaluated together so that anchored exclusions work.
        var insideSource = includes.Where(i => ArtifactPathResolver.IsUnder(i.SearchPath, source, comparison)).ToList();
        if (insideSource.Count > 0)
        {
            var selectorPatterns = new List<string>();

            foreach (var include in insideSource)
            {
                var relative = ArtifactPathResolver.MakeRelative(include.AbsolutePattern, source);
                selectorPatterns.Add(relative.Length == 0 ? "**" : relative);
            }

            foreach (var exclude in excludes)
            {
                var relative = ToRelativePattern(exclude, source, comparison);
                if (relative != null)
                {
                    selectorPatterns.Add("!" + relative);
                }
            }

            foreach (var match in _fileSelector.SelectFiles(sourceDirectory, selectorPatterns))
            {
                var relative = match.Replace('\\', '/');
                files[ArtifactPathResolver.Combine(source, relative)] = relative;
            }
        }

        // Patterns outside the source directory (absolute paths) are evaluated in their own directory.
        foreach (var include in includes.Except(insideSource))
        {
            var searchOsPath = ArtifactPathResolver.ToOsPath(include.SearchPath);
            string baseDirectory;
            string relativePattern;

            if (File.Exists(searchOsPath))
            {
                baseDirectory = ArtifactPathResolver.GetParent(include.SearchPath);
                relativePattern = ArtifactPathResolver.GetFileName(include.SearchPath);
            }
            else if (Directory.Exists(searchOsPath))
            {
                baseDirectory = include.SearchPath;
                relativePattern = ArtifactPathResolver.MakeRelative(include.AbsolutePattern, include.SearchPath);
                if (relativePattern.Length == 0)
                {
                    relativePattern = "**";
                }
            }
            else
            {
                _logger?.LogDebug("Pattern '{Pattern}' does not match anything: {Path} does not exist", include.Original, searchOsPath);
                continue;
            }

            foreach (var match in _fileSelector.SelectFiles(ArtifactPathResolver.ToOsPath(baseDirectory), new[] { relativePattern }))
            {
                var relative = match.Replace('\\', '/');
                var absolute = ArtifactPathResolver.Combine(baseDirectory, relative);

                if (IsExcluded(absolute, relative, baseDirectory, excludes, comparison))
                {
                    continue;
                }

                files[absolute] = absolute;
            }
        }

        var root = ResolveRootDirectory(includes, comparison);

        return files
            .Select(kv => new SelectedFile(
                ArtifactPathResolver.ToOsPath(kv.Key),
                kv.Value,
                ComputeArtifactPath(kv.Key, root, source, comparison)))
            .OrderBy(f => f.ArtifactPath, StringComparer.Ordinal)
            .ToList();
    }

    private static string ResolveRootDirectory(IReadOnlyList<IncludePattern> includes, StringComparison comparison)
    {
        if (includes.Count == 1)
        {
            var include = includes[0];

            // A single file uploaded without a glob: the artifact contains just that file.
            if (!ArtifactPathResolver.ContainsGlob(include.AbsolutePattern)
                && File.Exists(ArtifactPathResolver.ToOsPath(include.AbsolutePattern)))
            {
                return ArtifactPathResolver.GetParent(include.AbsolutePattern);
            }

            return include.SearchPath;
        }

        return ArtifactPathResolver.GetLeastCommonAncestor(includes.Select(i => i.SearchPath), comparison);
    }

    private static string ComputeArtifactPath(string absoluteFile, string root, string source, StringComparison comparison)
    {
        if (!string.IsNullOrEmpty(root)
            && !string.Equals(absoluteFile, root.TrimEnd('/'), comparison)
            && ArtifactPathResolver.IsUnder(absoluteFile, root, comparison))
        {
            var relative = ArtifactPathResolver.MakeRelative(absoluteFile, root);
            if (relative.Length > 0)
            {
                return relative;
            }
        }

        if (ArtifactPathResolver.IsUnder(absoluteFile, source, comparison))
        {
            var relative = ArtifactPathResolver.MakeRelative(absoluteFile, source);
            if (relative.Length > 0)
            {
                return relative;
            }
        }

        return ArtifactPathResolver.GetFileName(absoluteFile);
    }

    private static string? ToRelativePattern(string pattern, string directory, StringComparison comparison)
    {
        if (!ArtifactPathResolver.IsAbsolute(pattern))
        {
            return pattern;
        }

        var searchPath = ArtifactPathResolver.GetSearchPath(pattern);
        if (!ArtifactPathResolver.IsUnder(searchPath, directory, comparison))
        {
            return null;
        }

        var relative = ArtifactPathResolver.MakeRelative(pattern, directory);
        return relative.Length == 0 ? "**" : relative;
    }

    private bool IsExcluded(string absoluteFile, string relativeToBase, string baseDirectory, IReadOnlyList<string> excludes, StringComparison comparison)
    {
        foreach (var exclude in excludes)
        {
            if (ArtifactPathResolver.IsAbsolute(exclude))
            {
                var relativeExclude = ToRelativePattern(exclude, baseDirectory, comparison);
                if (relativeExclude != null)
                {
                    if (_fileSelector.Matches(relativeToBase, relativeExclude))
                    {
                        return true;
                    }
                }
                else if (!ArtifactPathResolver.ContainsGlob(exclude)
                         && ArtifactPathResolver.IsUnder(absoluteFile, exclude, comparison))
                {
                    return true;
                }
            }
            else if (exclude.StartsWith("**", StringComparison.Ordinal) && _fileSelector.Matches(relativeToBase, exclude))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeAbsolutePattern(string normalizedPattern)
    {
        var searchPath = ArtifactPathResolver.GetSearchPath(normalizedPattern);
        var remainder = normalizedPattern.Length > searchPath.Length
            ? normalizedPattern[searchPath.Length..].TrimStart('/')
            : string.Empty;

        var fullSearchPath = ArtifactPathResolver.NormalizeAbsolute(ArtifactPathResolver.ToOsPath(searchPath));
        return ArtifactPathResolver.Combine(fullSearchPath, remainder);
    }

    #endregion

    #region Private helpers

    private static ArtifactContext CurrentDirectoryContext()
    {
        return ArtifactContext.ForWorkspace(Directory.GetCurrentDirectory());
    }

    private static bool NameEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private string GetArtifactsBasePath(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            workspacePath = Directory.GetCurrentDirectory();
        }

        var configuredPath = _configuration.GetString(BasePathConfigKey);
        var relativeOrAbsolute = string.IsNullOrWhiteSpace(configuredPath) ? DefaultBasePath : configuredPath;

        return Path.IsPathRooted(relativeOrAbsolute)
            ? Path.GetFullPath(relativeOrAbsolute)
            : Path.GetFullPath(Path.Combine(Path.GetFullPath(workspacePath), relativeOrAbsolute));
    }

    private int? GetConfiguredRetentionDays()
    {
        var configured = _configuration.GetInt(RetentionConfigKey, 0);
        return configured > 0 ? configured : null;
    }

    private List<string> HandleNoFilesFound(IReadOnlyList<string> patterns, string basePath, IfNoFilesFound behavior)
    {
        var shownPatterns = patterns.Count == 0 ? "(none)" : string.Join(", ", patterns);

        switch (behavior)
        {
            case IfNoFilesFound.Warn:
                var warning = $"No files were found with the provided path: {shownPatterns} (searched in '{basePath}'). No artifacts will be uploaded.";
                _logger?.LogWarning("{Warning}", warning);
                return new List<string> { warning };

            case IfNoFilesFound.Ignore:
                _logger?.LogDebug("No files matched {Patterns} in {Path}; ignored", shownPatterns, basePath);
                return new List<string>();

            default:
                throw ArtifactException.NoFilesMatched(patterns, basePath);
        }
    }

    private async Task<List<ArtifactFileInfo>> CopyFilesAsync(
        IReadOnlyList<SelectedFile> files,
        string targetDirectory,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fileInfos = new List<ArtifactFileInfo>();
        var totalBytes = files.Sum(f => new FileInfo(f.AbsolutePath).Length);
        var processedBytes = 0L;
        var processedFiles = 0;

        Directory.CreateDirectory(targetDirectory);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destPath = Path.Combine(targetDirectory, ArtifactPathResolver.ToOsPath(file.ArtifactPath));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            var sourceInfo = new FileInfo(file.AbsolutePath);
            await CopyFileAsync(file.AbsolutePath, destPath, cancellationToken);
            var sha256 = await ComputeSha256Async(destPath, cancellationToken);

            fileInfos.Add(new ArtifactFileInfo
            {
                SourcePath = file.SourcePath,
                ArtifactPath = file.ArtifactPath,
                SizeBytes = sourceInfo.Length,
                Sha256 = sha256
            });

            processedBytes += sourceInfo.Length;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = files.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = file.ArtifactPath
            });

            _logger?.LogDebug("Uploaded file: {Path} ({Size} bytes)", file.ArtifactPath, sourceInfo.Length);
        }

        return fileInfos;
    }

    private static async Task<List<ArtifactFileInfo>> DescribeFilesAsync(
        IReadOnlyList<SelectedFile> files,
        CancellationToken cancellationToken)
    {
        var fileInfos = new List<ArtifactFileInfo>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceInfo = new FileInfo(file.AbsolutePath);
            var sha256 = await ComputeSha256Async(file.AbsolutePath, cancellationToken);

            fileInfos.Add(new ArtifactFileInfo
            {
                SourcePath = file.SourcePath,
                ArtifactPath = file.ArtifactPath,
                SizeBytes = sourceInfo.Length,
                Sha256 = sha256
            });
        }

        return fileInfos;
    }

    private static async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920; // 80KB
        await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true))
        await using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
        {
            await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
        }

        // Preserve file timestamps
        var sourceInfo = new FileInfo(sourcePath);
        File.SetLastWriteTimeUtc(destPath, sourceInfo.LastWriteTimeUtc);
    }

    private async Task<int> CopyFilesToTargetAsync(
        string artifactPath,
        string targetPath,
        IReadOnlyList<ArtifactFileInfo> files,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = files.Sum(f => f.SizeBytes);
        var processedBytes = 0L;
        var processedFiles = 0;
        var targetRoot = Path.GetFullPath(targetPath);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourcePath = Path.Combine(artifactPath, ArtifactPathResolver.ToOsPath(file.ArtifactPath));
            if (!File.Exists(sourcePath))
            {
                _logger?.LogWarning("Artifact file listed in metadata is missing: {Path}", sourcePath);
                continue;
            }

            var destPath = ArtifactCompressor.GetSafeExtractionPath(targetRoot, file.ArtifactPath, artifactPath);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            await CopyFileAsync(sourcePath, destPath, cancellationToken);

            processedBytes += file.SizeBytes;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = files.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = file.ArtifactPath
            });

            _logger?.LogDebug("Downloaded file: {Path} ({Size} bytes)", file.ArtifactPath, file.SizeBytes);
        }

        return processedFiles;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ArtifactMetadata CreateMetadata(
        string artifactName,
        ArtifactContext context,
        List<ArtifactFileInfo> files,
        ArtifactOptions options,
        long? compressedSize,
        int? configuredRetentionDays)
    {
        return new ArtifactMetadata
        {
            Version = ArtifactMetadata.CurrentVersion,
            Artifact = new ArtifactInfo
            {
                Name = artifactName,
                UploadedAt = DateTime.UtcNow,
                Job = context.JobName,
                Step = context.StepName,
                Compression = options.Compression,
                RunId = context.RunId,
                RetentionDays = options.RetentionDays is > 0 ? options.RetentionDays : configuredRetentionDays
            },
            Files = files,
            Summary = new ArtifactSummary
            {
                FileCount = files.Count,
                TotalSizeBytes = files.Sum(f => f.SizeBytes),
                CompressedSizeBytes = compressedSize
            }
        };
    }

    private static async Task WriteMetadataAsync(string artifactPath, ArtifactMetadata metadata, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(artifactPath, MetadataFileName);
        await File.WriteAllTextAsync(metadataPath, metadata.ToJson(), cancellationToken);
    }

    private static async Task<ArtifactMetadata?> LoadMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            return ArtifactMetadata.FromJson(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> GetRunDirectories(string basePath)
    {
        return Directory.GetDirectories(basePath, RunDirectoryPrefix + "*").OrderBy(d => d, StringComparer.Ordinal);
    }

    private static string GetRunId(string runDirectory)
    {
        var name = Path.GetFileName(runDirectory);
        return name.StartsWith(RunDirectoryPrefix, StringComparison.Ordinal) ? name[RunDirectoryPrefix.Length..] : name;
    }

    /// <summary>
    /// Finds artifact directories using the fixed run/job/step/artifact layout, so that user files
    /// inside an artifact are never mistaken for artifacts.
    /// </summary>
    private static List<string> FindArtifactDirectories(string runDirectory)
    {
        var results = new List<string>();

        try
        {
            foreach (var jobDir in Directory.GetDirectories(runDirectory, JobDirectoryPrefix + "*"))
            {
                foreach (var stepDir in Directory.GetDirectories(jobDir, StepDirectoryPrefix + "*"))
                {
                    foreach (var artifactDir in Directory.GetDirectories(stepDir, ArtifactNames.DirectoryPrefix + "*"))
                    {
                        if (File.Exists(Path.Combine(artifactDir, MetadataFileName)))
                        {
                            results.Add(artifactDir);
                        }
                    }
                }
            }
        }
        catch (IOException)
        {
            // Ignore access errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore access errors
        }

        return results.OrderBy(d => d, StringComparer.Ordinal).ToList();
    }

    private void DeleteArtifactDirectory(string storagePath)
    {
        if (Directory.Exists(storagePath))
        {
            Directory.Delete(storagePath, recursive: true);
        }

        DeleteLegacyArchives(storagePath);
    }

    private void DeleteLegacyArchives(string storagePath)
    {
        foreach (var compressionType in Enum.GetValues<CompressionType>())
        {
            var extension = _compressor.GetExtension(compressionType);
            if (string.IsNullOrEmpty(extension))
            {
                continue;
            }

            var archivePath = storagePath + extension;
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ignore cleanup errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Parses a run identifier (yyyyMMdd-HHmmss[-fff]) as a UTC timestamp.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="timestamp">The parsed UTC timestamp.</param>
    /// <returns>True when the run identifier is a timestamp.</returns>
    public static bool TryParseRunTimestamp(string runId, out DateTime timestamp)
    {
        if (DateTime.TryParseExact(
                runId,
                RunIdFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestamp))
        {
            return true;
        }

        timestamp = default;
        return false;
    }

    #endregion
}
