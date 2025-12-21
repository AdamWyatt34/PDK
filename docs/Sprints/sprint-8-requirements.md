# PDK Sprint 8: Artifact Handling
## Requirements Document

**Document Version:** 1.0  
**Status:** Ready for Implementation  
**Sprint:** 8  
**Author:** PDK Development Team  
**Last Updated:** 2024-12-07  

---

## Executive Summary

Sprint 8 adds artifact management capabilities to PDK, enabling users to save files from one step and retrieve them in later steps, mirroring the artifact functionality found in GitHub Actions and Azure DevOps pipelines. This sprint delivers a robust artifact system with wildcard support, compression options, and proper error handling.

### Goals
- Support uploading and downloading artifacts between pipeline steps
- Store artifacts in organized local directory structure
- Support wildcard file patterns for flexible artifact selection
- Preserve directory structure and file metadata
- Integrate seamlessly with existing parsers and job runner

### Success Criteria
- Users can upload artifacts from a step
- Users can download artifacts in later steps
- Artifacts stored in predictable, organized locations
- Wildcard patterns work for file selection
- Directory structure preserved
- All features have 80%+ test coverage

---

## Feature Requirements

### FR-08-001: Artifact Manager
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** Sprint 7 (Configuration - for artifact path configuration)

#### Description
Implement core artifact management system that handles saving, organizing, and retrieving artifacts with support for wildcards, compression, and metadata tracking.

#### Requirements

**REQ-08-001: Artifact Storage Location**
- Default storage location: `.pdk/artifacts/` in workspace root
- Configurable via configuration file: `artifacts.basePath`
- Organize artifacts by run/job/step hierarchy:
  ```
  .pdk/artifacts/
  ├── run-{timestamp}/
  │   ├── job-{jobName}/
  │   │   ├── step-{stepName}/
  │   │   │   ├── artifact-{artifactName}/
  │   │   │   │   ├── file1.txt
  │   │   │   │   └── file2.log
  ```
- Create directories as needed
- Handle cross-platform paths correctly

**REQ-08-002: Artifact Metadata**
- Store metadata alongside artifacts:
  - Artifact name
  - Upload timestamp
  - Source job and step
  - File count and total size
  - Compression algorithm (if used)
  - Original paths (for restoration)
- Metadata format: JSON file `artifact.metadata.json`
- Example metadata:
  ```json
  {
    "name": "build-output",
    "uploadedAt": "2024-01-15T10:30:00Z",
    "job": "build",
    "step": "Build .NET Project",
    "fileCount": 15,
    "totalSizeBytes": 1048576,
    "compression": "gzip",
    "files": [
      {
        "sourcePath": "bin/Release/net8.0/app.dll",
        "artifactPath": "app.dll",
        "sizeBytes": 524288
      }
    ]
  }
  ```

**REQ-08-003: File Selection with Wildcards**
- Support glob patterns for file selection:
  - `*.dll` - All DLL files in current directory
  - `**/*.log` - All log files recursively
  - `bin/Release/**/*` - All files in bin/Release and subdirectories
  - `[Bb]uild/**` - Case variations
  - `!*.tmp` - Exclusion patterns (files to skip)
- Use standard glob syntax
- Support multiple patterns (OR logic)
- Support exclusion patterns (prefixed with `!`)
- Case-sensitive on Linux/macOS, case-insensitive on Windows

**REQ-08-004: Artifact Upload**
- Copy files from container to host filesystem
- Preserve directory structure relative to pattern root
- Handle symlinks (follow or preserve based on option)
- Skip duplicate files (same name and content hash)
- Validate artifact name (alphanumeric, hyphens, underscores only)
- Maximum artifact name length: 100 characters
- Error if no files match pattern (optional: allow empty artifacts)

**REQ-08-005: Artifact Download**
- Retrieve artifacts by name
- Copy files from artifact storage to target location
- Restore original directory structure
- Handle conflicts (overwrite vs skip vs error)
- Support downloading from specific job/step or latest
- Support partial downloads (subset of files)

**REQ-08-006: Compression Support**
- Optional compression for artifacts
- Supported formats:
  - None (no compression)
  - Gzip (.tar.gz)
  - Zip (.zip)
- Configure via artifact upload options
- Automatic decompression on download
- Compress directories as single archive
- Skip compression for already-compressed files (.zip, .gz, etc.)

**REQ-08-007: Artifact Retention**
- Configurable retention period (default: 7 days from config)
- Cleanup old artifacts automatically
- Respect retention policy from configuration
- Manual cleanup command: `pdk artifact clean`
- Preserve artifacts if explicitly saved (flag option)

**REQ-08-008: Error Handling**
- Clear errors for common issues:
  - No files match pattern
  - Artifact already exists (duplicate name)
  - Permission denied
  - Disk space issues
  - Corrupt artifact metadata
- Detailed error messages with file paths
- Suggestions for resolution

#### Acceptance Criteria
- ✅ Artifacts stored in organized directory structure
- ✅ Metadata tracked for all artifacts
- ✅ Wildcard patterns select correct files
- ✅ Exclusion patterns work correctly
- ✅ Directory structure preserved on download
- ✅ Compression reduces artifact size
- ✅ Retention policy enforced
- ✅ Error messages are helpful and actionable
- ✅ Unit tests cover artifact manager logic
- ✅ Integration tests verify file operations

---

### FR-08-002: Parser Integration
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** Sprint 1 (GitHub Parser), Sprint 2 (Azure Parser), FR-08-001

#### Description
Extend existing parsers to recognize and parse artifact-related actions/tasks in GitHub Actions workflows and Azure DevOps pipelines, mapping them to a common artifact model.

#### Requirements

**REQ-08-010: Common Artifact Model**
- Define provider-agnostic artifact model:
  ```csharp
  public record ArtifactDefinition
  {
      public string Name { get; init; }
      public ArtifactOperation Operation { get; init; } // Upload or Download
      public string[] Patterns { get; init; }
      public string? TargetPath { get; init; }
      public ArtifactOptions Options { get; init; }
  }
  
  public enum ArtifactOperation
  {
      Upload,
      Download
  }
  
  public record ArtifactOptions
  {
      public CompressionType Compression { get; init; }
      public bool IfNoFilesFound { get; init; } // error, warn, or ignore
      public int? RetentionDays { get; init; }
      public bool OverwriteExisting { get; init; }
  }
  ```
- All parsers map to this common model
- Model is part of Step definition

**REQ-08-011: GitHub Actions Parser Support**
- Recognize `actions/upload-artifact` action:
  ```yaml
  - uses: actions/upload-artifact@v3
    with:
      name: build-output
      path: |
        bin/
        *.dll
      retention-days: 7
      if-no-files-found: error
  ```
- Recognize `actions/download-artifact` action:
  ```yaml
  - uses: actions/download-artifact@v3
    with:
      name: build-output
      path: ./artifacts/
  ```
- Map to ArtifactDefinition model
- Support all official artifact action parameters
- Version support: @v3, @v4 (latest)

**REQ-08-012: Azure DevOps Parser Support**
- Recognize `PublishBuildArtifacts` task:
  ```yaml
  - task: PublishBuildArtifacts@1
    inputs:
      PathtoPublish: '$(Build.ArtifactStagingDirectory)'
      ArtifactName: 'drop'
      publishLocation: 'Container'
  ```
- Recognize `DownloadBuildArtifacts` task:
  ```yaml
  - task: DownloadBuildArtifacts@0
    inputs:
      buildType: 'current'
      downloadType: 'single'
      artifactName: 'drop'
      downloadPath: '$(System.ArtifactsDirectory)'
  ```
- Recognize newer `PublishPipelineArtifact` and `DownloadPipelineArtifact` tasks
- Map to ArtifactDefinition model
- Handle Azure-specific path variables (`$(Build.ArtifactStagingDirectory)`)

**REQ-08-013: Step Type Identification**
- Add artifact step types to Step model:
  - `StepType.UploadArtifact`
  - `StepType.DownloadArtifact`
- Parsers set appropriate step type
- Job runner uses step type to dispatch to correct executor

**REQ-08-014: Path Variable Resolution**
- Resolve provider-specific path variables:
  - GitHub: `${{ github.workspace }}`, `${{ runner.temp }}`
  - Azure: `$(Build.ArtifactStagingDirectory)`, `$(System.ArtifactsDirectory)`
- Map to PDK equivalents or expand to actual paths
- Use variable resolver from Sprint 7

#### Acceptance Criteria
- ✅ GitHub upload-artifact actions parsed correctly
- ✅ GitHub download-artifact actions parsed correctly
- ✅ Azure publish tasks parsed correctly
- ✅ Azure download tasks parsed correctly
- ✅ All parameters mapped to common model
- ✅ Step type set correctly for artifact steps
- ✅ Path variables resolved
- ✅ Unit tests cover parser logic
- ✅ Integration tests with real workflow files

---

### FR-08-003: Runner Integration
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** Sprint 4 (Job Runner), FR-08-001, FR-08-002

#### Description
Implement step executors for artifact upload and download operations, integrating with the Docker container manager to copy files between container and host filesystem.

#### Requirements

**REQ-08-020: Upload Artifact Executor**
- Implement `UploadArtifactExecutor` for artifact uploads
- Execute upload operations:
  1. Resolve wildcard patterns in container filesystem
  2. Copy matched files from container to host
  3. Create artifact directory structure
  4. Generate metadata
  5. Optionally compress files
  6. Report success and file count
- Preserve file permissions where possible
- Handle symlinks (follow by default)
- Log each file uploaded (at DEBUG level)
- Progress reporting for large uploads

**REQ-08-021: Download Artifact Executor**
- Implement `DownloadArtifactExecutor` for artifact downloads
- Execute download operations:
  1. Locate artifact in storage
  2. Decompress if needed
  3. Copy files to target location in container
  4. Restore directory structure
  5. Report success and file count
- Handle missing artifacts gracefully
- Support overwrite options
- Log each file downloaded (at DEBUG level)
- Progress reporting for large downloads

**REQ-08-022: Container File Operations**
- Copy files from container to host:
  - Use Docker API: `container.GetArchive()` or equivalent
  - Extract tar archive from container
  - Write to host filesystem
- Copy files from host to container:
  - Create tar archive on host
  - Use Docker API: `container.PutArchive()` or equivalent
  - Extract in container
- Preserve directory structure
- Handle file permissions
- Validate file integrity (checksums)

**REQ-08-023: Step Execution Integration**
- Job runner dispatches artifact steps to correct executor
- Artifact executors implement `IStepExecutor` interface
- Report progress via `IProgressReporter`
- Update step status (uploading/downloading/complete)
- Handle cancellation via CancellationToken
- Clean up on errors

**REQ-08-024: Artifact Context**
- Track current run/job/step context for artifact paths
- Generate unique run identifier (timestamp-based)
- Pass context to artifact manager
- Enable artifact download from current or previous runs
- Support cross-job artifact sharing

**REQ-08-025: Error Handling**
- Handle file copy failures
- Handle permission errors
- Handle disk space errors
- Handle container communication errors
- Provide actionable error messages
- Log detailed errors for troubleshooting

#### Acceptance Criteria
- ✅ Can upload files from container to host
- ✅ Can download files from host to container
- ✅ Directory structure preserved
- ✅ File permissions preserved where possible
- ✅ Wildcards select correct files in container
- ✅ Progress reported during operations
- ✅ Errors handled gracefully
- ✅ Step status updated correctly
- ✅ Unit tests cover executor logic
- ✅ Integration tests verify file operations

---

## Non-Functional Requirements

### NFR-08-001: Performance
- Upload/download: Handle 1000+ files efficiently
- Compression: Use streaming to avoid memory issues with large files
- Progress updates: Report every 1 second or 10MB, whichever comes first
- Cleanup: Run in background, don't block pipeline execution
- Wildcard matching: Efficient for large directory trees

### NFR-08-002: Storage Efficiency
- Compress artifacts by default (configurable)
- Deduplicate files with same content hash (optional)
- Cleanup old artifacts automatically
- Warn when disk space low (< 1GB available)
- Maximum single artifact size: 2GB (configurable)

### NFR-08-003: Reliability
- Atomic operations (all files or none)
- Validate file integrity (checksums)
- Recover from partial failures where possible
- Metadata always consistent with actual files
- Retry transient failures (network, disk)

### NFR-08-004: Usability
- Clear progress indicators for large uploads/downloads
- Helpful error messages for common issues
- Default behavior matches GitHub Actions/Azure DevOps
- Examples in documentation
- CLI commands for artifact management

### NFR-08-005: Testability
- All artifact operations unit testable
- Mock filesystem for tests
- Mock container manager for tests
- Test coverage target: 80%+

---

## Technical Specifications

### TS-08-001: Artifact Manager Architecture

**Core interfaces:**
```csharp
public interface IArtifactManager
{
    Task<UploadResult> UploadAsync(
        string artifactName,
        IEnumerable<string> patterns,
        ArtifactOptions options,
        CancellationToken cancellationToken = default);
    
    Task<DownloadResult> DownloadAsync(
        string artifactName,
        string targetPath,
        ArtifactOptions options,
        CancellationToken cancellationToken = default);
    
    Task<IEnumerable<ArtifactInfo>> ListAsync();
    Task<bool> ExistsAsync(string artifactName);
    Task DeleteAsync(string artifactName);
    Task CleanupAsync(int retentionDays);
}

public record UploadResult
{
    public string ArtifactName { get; init; }
    public int FileCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public string StoragePath { get; init; }
}

public record DownloadResult
{
    public string ArtifactName { get; init; }
    public int FileCount { get; init; }
    public string TargetPath { get; init; }
}

public record ArtifactInfo
{
    public string Name { get; init; }
    public DateTime UploadedAt { get; init; }
    public int FileCount { get; init; }
    public long TotalSizeBytes { get; init; }
}
```

### TS-08-002: File Selection Architecture

**File selector interface:**
```csharp
public interface IFileSelector
{
    IEnumerable<string> SelectFiles(
        string basePath,
        IEnumerable<string> patterns,
        IEnumerable<string>? exclusions = null);
    
    bool Matches(string filePath, string pattern);
}
```

**Glob pattern support:**
- Use `Microsoft.Extensions.FileSystemGlobbing` library
- Patterns:
  - `*` - Matches any characters except `/`
  - `**` - Matches any characters including `/` (recursive)
  - `?` - Matches single character
  - `[abc]` - Matches any character in brackets
  - `[!abc]` - Matches any character NOT in brackets
  - `{a,b}` - Matches either a or b

### TS-08-003: Compression Architecture

**Compression strategy:**
```csharp
public interface IArtifactCompressor
{
    Task CompressAsync(
        string sourcePath,
        string targetPath,
        CompressionType type,
        CancellationToken cancellationToken = default);
    
    Task DecompressAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken = default);
}

public enum CompressionType
{
    None,
    Gzip,  // .tar.gz
    Zip    // .zip
}
```

**Implementation:**
- Gzip: Use `System.IO.Compression.GZipStream` with tar
- Zip: Use `System.IO.Compression.ZipArchive`
- Stream files to avoid loading entire archive in memory
- Progress callbacks for large operations

### TS-08-004: Metadata Schema

**Metadata file format:**
```json
{
  "version": "1.0",
  "artifact": {
    "name": "build-output",
    "uploadedAt": "2024-01-15T10:30:00Z",
    "job": "build",
    "step": "3",
    "compression": "gzip"
  },
  "files": [
    {
      "sourcePath": "bin/Release/app.dll",
      "artifactPath": "bin/Release/app.dll",
      "sizeBytes": 524288,
      "sha256": "abc123..."
    }
  ],
  "summary": {
    "fileCount": 15,
    "totalSizeBytes": 1048576,
    "compressedSizeBytes": 524288
  }
}
```

---

## Dependencies

### External Dependencies
- **Microsoft.Extensions.FileSystemGlobbing** (≥8.0.0): Glob pattern matching
- **System.IO.Compression** (built-in): Compression support
- **Docker.DotNet** (already used): Container file operations

### Internal Dependencies
- **Sprint 4**: Job runner and step executors (for integration)
- **Sprint 3**: Docker container manager (for file operations)
- **Sprint 7**: Variable resolver (for path variable resolution)
- **Sprint 7**: Configuration (for artifact settings)

---

## Testing Strategy

### Unit Testing
- Artifact manager operations (upload, download, list, delete)
- File selection with various glob patterns
- Compression and decompression
- Metadata generation and parsing
- Executor logic (without container)
- Parser integration (artifact step parsing)

### Integration Testing
- Upload and download with real files
- Container file operations (actual Docker containers)
- End-to-end artifact workflow
- Cross-job artifact sharing
- Retention and cleanup
- Error scenarios

### Test Data
- Sample files with various sizes and types
- Nested directory structures
- Edge cases: empty files, symlinks, special characters
- Large files (for performance testing)
- Corrupt archives (for error handling)

---

## Success Metrics

### Functional Metrics
- All artifact operations working correctly
- Wildcard patterns select correct files
- Directory structure preserved
- Test coverage: 80%+ for all artifact code

### Performance Metrics
- Upload 1000 files in < 10 seconds
- Download 1000 files in < 10 seconds
- Compression reduces size by 50%+ on average
- Cleanup runs in background without impacting execution

### Quality Metrics
- Error messages are helpful and actionable
- Progress reporting is smooth and informative
- Artifacts reliably available across job boundaries
- No data loss or corruption

---

## Constraints and Assumptions

### Constraints
- Single machine only (no distributed artifact storage)
- Local disk storage required
- File size limited by available disk space
- Container must support tar archive operations

### Assumptions
- Users have sufficient disk space for artifacts
- File systems support standard file operations
- Docker containers allow file copy operations
- Users understand glob pattern syntax

---

## Future Considerations

### Post-Sprint Enhancements
- Remote artifact storage (S3, Azure Blob, etc.)
- Artifact caching for performance
- Incremental uploads (only changed files)
- Artifact versioning
- Artifact dependencies (download from other pipelines)
- Artifact search and querying
- Web UI for browsing artifacts

### Technical Debt
- Optimize for very large files (>1GB)
- Add artifact signing for integrity
- Implement artifact retention policies per artifact
- Add artifact statistics and reporting

---

## Appendix

### A. Artifact Usage Examples

**GitHub Actions workflow:**
```yaml
name: Build and Test
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Build
        run: dotnet build
      
      - uses: actions/upload-artifact@v3
        with:
          name: build-output
          path: |
            bin/**/*.dll
            bin/**/*.exe
          retention-days: 7
  
  test:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - uses: actions/download-artifact@v3
        with:
          name: build-output
          path: ./bin
      
      - name: Test
        run: dotnet test
```

**Azure DevOps pipeline:**
```yaml
jobs:
- job: Build
  steps:
  - script: dotnet build
  
  - task: PublishPipelineArtifact@1
    inputs:
      targetPath: '$(Build.ArtifactStagingDirectory)'
      artifactName: 'drop'

- job: Deploy
  dependsOn: Build
  steps:
  - task: DownloadPipelineArtifact@2
    inputs:
      artifactName: 'drop'
      targetPath: '$(Pipeline.Workspace)/artifacts'
```

### B. Artifact CLI Commands

```bash
# List artifacts
pdk artifact list

# Show artifact details
pdk artifact info build-output

# Download artifact manually
pdk artifact download build-output ./output

# Clean up old artifacts
pdk artifact clean --older-than 7d

# Delete specific artifact
pdk artifact delete build-output
```

### C. Glob Pattern Examples

```
# All DLL files in bin directory
bin/*.dll

# All files recursively in bin
bin/**/*

# All log files anywhere
**/*.log

# Exclude test files
**/*.dll
!**/*Test.dll

# Specific patterns
{bin,obj}/**/*.{dll,exe}
```

### D. Glossary

- **Artifact**: Set of files produced/consumed by pipeline steps
- **Upload**: Save files from container to host storage
- **Download**: Retrieve files from storage to container
- **Glob Pattern**: File matching pattern with wildcards
- **Compression**: Reduce artifact size using compression algorithms
- **Retention**: How long artifacts are kept before cleanup

---

**Document Status:** Ready for Implementation  
**Next Steps:** Review with stakeholders, begin Sprint 8 implementation  
**Change History:**
- 2024-12-07: v1.0 - Initial requirements document
