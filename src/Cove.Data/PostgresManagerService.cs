using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Cove.Core.Interfaces;

namespace Cove.Data;

/// <summary>
/// Manages a self-contained PostgreSQL instance that starts/stops with the app.
/// On first run, downloads portable PostgreSQL binaries automatically.
/// </summary>
public class PostgresManagerService : IHostedService
{
    private readonly PostgresConfig _config;
    private readonly ILogger<PostgresManagerService> _logger;
    private bool _started;

    // PostgreSQL 18.3 - latest stable release
    private const string PgMajor = "18";
    private const string PgFullVersion = "18.3";
    private const string PgvectorVersion = "0.8.2";

    // Windows: EDB portable binaries (still available for Windows/macOS)
    private const string WinUrl = "https://sbp.enterprisedb.com/getfile.jsp?fileid=1260146";
    // macOS: EDB portable binaries
    private const string MacUrl = "https://sbp.enterprisedb.com/getfile.jsp?fileid=1260163";

    public PostgresManagerService(IOptions<PostgresConfig> config, ILogger<PostgresManagerService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>Root directory for all managed postgres files (binaries + data).</summary>
    private string CoveDir => string.IsNullOrWhiteSpace(_config.DataPath)
        ? CoveDefaultPaths.GetDataRoot()
        : CoveDefaultPaths.ResolveDataPath(_config.DataPath);

    private string BinDir => Path.Combine(CoveDir, "pgsql", "bin");
    private string DataDir => Path.Combine(CoveDir, "pgdata");
    private string LogFile => Path.Combine(CoveDir, "pg.log");
    private string BundledPgvectorDir => BundledPgvectorCandidateDirs().First();

    private string Exe(string name) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Path.Combine(BinDir, $"{name}.exe")
                                                            : Path.Combine(BinDir, name);

    // ─── Lifecycle ──────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_config.Managed)
        {
            _logger.LogInformation("Managed PostgreSQL disabled — using external connection string");
            return;
        }

        _logger.LogInformation("Managed PostgreSQL mode enabled");

        // 1. On Linux/macOS, check if a system postgres is already available in PATH
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var systemPgCtl = FindSystemPgCtl();
            if (systemPgCtl != null && !File.Exists(Exe("pg_ctl")))
            {
                _logger.LogInformation("Found system PostgreSQL at {Path} — symlinking to managed bin dir", systemPgCtl);
                LinkSystemPostgresBinDir(systemPgCtl);
            }
        }

        // 2. Ensure binaries exist (download if needed)
        if (!File.Exists(Exe("pg_ctl")))
        {
            _logger.LogInformation("PostgreSQL binaries not found — downloading portable {Version}…", PgFullVersion);
            await DownloadPostgresAsync(ct);
        }
        else
        {
            _logger.LogInformation("PostgreSQL binaries found at {BinDir}", BinDir);
        }

        // 2. Check if a stale instance exists from a previous crash
        await StopStaleInstanceAsync(ct);

        await EnsurePgvectorInstalledAsync(ct);

        // 3. Init data directory if needed
        if (!File.Exists(Path.Combine(DataDir, "PG_VERSION")))
        {
            _logger.LogInformation("Initializing data directory at {DataDir}", DataDir);
            await InitDbAsync(ct);
        }

        await EnsureManagedConfigurationAsync(ct);

        // 4. Start PostgreSQL
        _logger.LogInformation("Starting PostgreSQL on port {Port}", _config.Port);
        await PgCtlAsync($"start -D \"{DataDir}\" -l \"{LogFile}\" -w -t 300 -o \"-p {_config.Port}\"", ct);
        _started = true;

        // 5. Wait for ready
        await WaitForReadyAsync(ct);

        // 6. Create database if it doesn't exist
        await EnsureDatabaseAsync(ct);

        _logger.LogInformation("Managed PostgreSQL is ready (port {Port}, database '{Db}')", _config.Port, _config.Database);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_config.Managed || !_started) return;

        _logger.LogInformation("Stopping managed PostgreSQL");
        try
        {
            await PgCtlAsync($"stop -D \"{DataDir}\" -m fast", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during PostgreSQL shutdown — it may already be stopped");
        }
        _started = false;
    }

    // ─── Download ───────────────────────────────────────────────────

    private async Task DownloadPostgresAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(CoveDir);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await DownloadAndExtractArchiveAsync(WinUrl, ".zip", ct);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await DownloadAndExtractArchiveAsync(MacUrl, ".zip", ct);
        }
        else
        {
            // Linux: EDB no longer provides portable binaries.
            // Install from PGDG APT repository packages extracted locally.
            await InstallLinuxPostgresAsync(ct);
        }

        if (!File.Exists(Exe("pg_ctl")))
            throw new FileNotFoundException(
                $"Installation succeeded but pg_ctl not found at expected path: {Exe("pg_ctl")}. " +
                $"Contents of {CoveDir}: {string.Join(", ", Directory.GetDirectories(CoveDir))}");

        _logger.LogInformation("PostgreSQL {Version} binaries ready at {BinDir}", PgFullVersion, BinDir);
    }

    private async Task DownloadAndExtractArchiveAsync(string url, string ext, CancellationToken ct)
    {
        string archivePath = Path.Combine(CoveDir, $"postgresql{ext}");

        await DownloadFileAsync(url, archivePath, ct);

        _logger.LogInformation("Extracting…");

        if (ext == ".zip")
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var exitCode = await RunAsync("/usr/bin/unzip", $"-q -o \"{archivePath}\" -d \"{CoveDir}\"", CoveDir, ct);
                if (exitCode != 0)
                    throw new InvalidOperationException("Failed to extract PostgreSQL archive");
            }
            else
            {
                ZipFile.ExtractToDirectory(archivePath, CoveDir, overwriteFiles: true);
            }
        }
        else
        {
            var exitCode = await RunAsync("/bin/tar", $"xzf \"{archivePath}\" -C \"{CoveDir}\"", CoveDir, ct);
            if (exitCode != 0)
                throw new InvalidOperationException("Failed to extract PostgreSQL archive");
            await RunAsync("/bin/chmod", $"-R +x \"{BinDir}\"", CoveDir, ct);
        }

        File.Delete(archivePath);
    }

    /// <summary>Find pg_ctl in common system paths.</summary>
    private string? FindSystemPgCtl()
    {
        // Common system install locations
        var candidates = new[]
        {
            $"/usr/lib/postgresql/{PgMajor}/bin/pg_ctl",
            $"/usr/lib/postgresql/{int.Parse(PgMajor) - 1}/bin/pg_ctl", // one version back
            "/usr/bin/pg_ctl",
            "/usr/local/bin/pg_ctl",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Symlink (or copy) system postgres bin dir so our managed wrapper can find it.</summary>
    private void LinkSystemPostgresBinDir(string pgCtlPath)
    {
        var systemBinDir = Path.GetDirectoryName(pgCtlPath)!;
        var pgsqlDir = Path.Combine(CoveDir, "pgsql");
        Directory.CreateDirectory(pgsqlDir);
        try
        {
            Directory.CreateSymbolicLink(BinDir, systemBinDir);
        }
        catch
        {
            // Symlink failed (permissions?) — just note the path; we set BinDir to the system dir
            _logger.LogWarning("Could not symlink {SystemBin} to {BinDir} — will use system path directly", systemBinDir, BinDir);
        }
    }

    private async Task InstallLinuxPostgresAsync(CancellationToken ct)
    {
        // Strategy 1: try apt-get (works on Debian/Ubuntu without root if postgresql is already
        // in the package cache, otherwise tries with sudo).  If apt-get is not available or
        // fails, fall back to downloading .deb packages manually.

        // Check if apt-get is available
        var hasAptGet = File.Exists("/usr/bin/apt-get");
        if (hasAptGet)
        {
            _logger.LogInformation("Attempting to install PostgreSQL {Version} via apt-get…", PgMajor);
            // Add PGDG repo key + source if not already present
            await TryAddPgdgRepoAsync(ct);

            var installCode = await RunAsync(
                "/usr/bin/apt-get",
                $"install -y postgresql-{PgMajor} postgresql-client-{PgMajor} postgresql-{PgMajor}-pgvector",
                CoveDir, ct);

            if (installCode == 0)
            {
                // Link the system-installed binaries
                var sysPgCtl = FindSystemPgCtl();
                if (sysPgCtl != null)
                    LinkSystemPostgresBinDir(sysPgCtl);

                // If still not found, make explicit symlinks
                if (!File.Exists(Exe("pg_ctl")))
                {
                    var systemBin = $"/usr/lib/postgresql/{PgMajor}/bin";
                    if (Directory.Exists(systemBin))
                    {
                        var pgsqlDir = Path.Combine(CoveDir, "pgsql");
                        Directory.CreateDirectory(pgsqlDir);
                        try { Directory.CreateSymbolicLink(BinDir, systemBin); }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to link system PostgreSQL bin directory {SystemBin} to {BinDir}; falling back to package extraction", systemBin, BinDir);
                        }
                    }
                }
                return;
            }
            _logger.LogWarning("apt-get install failed (exit {Code}) — falling back to .deb extraction", installCode);
        }

        // Strategy 2: Download .deb packages from the PGDG APT repository and extract locally.
        var tempDir = Path.Combine(CoveDir, "_pg_install_tmp");
        var extractDir = Path.Combine(CoveDir, "_pg_extract_tmp");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(extractDir);

        try
        {
            // Detect distro codename for PGDG repo (default to noble/Ubuntu 24.04)
            var codename = "noble";
            if (File.Exists("/etc/os-release"))
            {
                var osRelease = await File.ReadAllTextAsync("/etc/os-release", ct);
                foreach (var line in osRelease.Split('\n'))
                {
                    if (line.StartsWith("VERSION_CODENAME="))
                    {
                        codename = line.Split('=')[1].Trim().Trim('"');
                        break;
                    }
                }
            }

            // Map distro codename to PGDG numeric suffix (e.g. noble=24.04, jammy=22.04)
            var pgdgSuffix = codename switch
            {
                "noble" => "24.04",
                "jammy" => "22.04",
                "focal" => "20.04",
                "bookworm" => "12",
                "bullseye" => "11",
                "buster" => "10",
                _ => "24.04", // default to latest
            };

            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
            var pgdgBase = $"https://apt.postgresql.org/pub/repos/apt/pool/main/p/postgresql-{PgMajor}";

            // Try both naming conventions: with and without pgdg codename suffix
            var packageNames = new[]
            {
                $"postgresql-{PgMajor}_{PgFullVersion}-1.pgdg{pgdgSuffix}+1_{arch}.deb",
                $"postgresql-{PgMajor}_{PgFullVersion}-1_{arch}.deb",
            };

            foreach (var (baseName, isServer) in new[] { (packageNames, true) })
            {
                string? downloaded = null;
                foreach (var pkgName in baseName)
                {
                    var pkgUrl = $"{pgdgBase}/{pkgName}";
                    var pkgPath = Path.Combine(tempDir, pkgName);
                    _logger.LogInformation("Trying {Url}…", pkgUrl);
                    try
                    {
                        await DownloadFileAsync(pkgUrl, pkgPath, ct);
                        downloaded = pkgPath;
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(ex, "Not found at {Url}", pkgUrl);
                    }
                }
                if (downloaded == null)
                    throw new InvalidOperationException(
                        $"Could not download postgresql-{PgMajor} for {codename}/{arch}. " +
                        "Please install PostgreSQL manually (apt-get install postgresql) or configure an external connection string.");
            }

            // Also try to get the client package (best-effort, not required)
            foreach (var pkgName in new[]
            {
                $"postgresql-client-{PgMajor}_{PgFullVersion}-1.pgdg{pgdgSuffix}+1_{arch}.deb",
                $"postgresql-client-{PgMajor}_{PgFullVersion}-1_{arch}.deb",
            })
            {
                try
                {
                    await DownloadFileAsync($"{pgdgBase}/{pkgName}", Path.Combine(tempDir, pkgName), ct);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Optional PostgreSQL client package {PackageName} was not available; continuing", pkgName);
                }
            }

            var pgvectorBase = "https://apt.postgresql.org/pub/repos/apt/pool/main/p/pgvector";
            var pgvectorDownloaded = false;
            foreach (var pkgName in new[]
            {
                $"postgresql-{PgMajor}-pgvector_{PgvectorVersion}-1.pgdg{pgdgSuffix}+1_{arch}.deb",
                $"postgresql-{PgMajor}-pgvector_{PgvectorVersion}-1_{arch}.deb",
            })
            {
                try
                {
                    await DownloadFileAsync($"{pgvectorBase}/{pkgName}", Path.Combine(tempDir, pkgName), ct);
                    pgvectorDownloaded = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "PostgreSQL pgvector package {PackageName} was not available at {BaseUrl}", pkgName, pgvectorBase);
                }
            }

            if (!pgvectorDownloaded)
            {
                throw new InvalidOperationException(
                    $"Could not download postgresql-{PgMajor}-pgvector {PgvectorVersion} for {codename}/{arch}. " +
                    "Install PostgreSQL and pgvector with your OS package manager or configure an external pgvector-enabled connection string.");
            }

            // Extract .deb packages
            foreach (var debFile in Directory.GetFiles(tempDir, "*.deb"))
            {
                _logger.LogInformation("Extracting {File}", Path.GetFileName(debFile));
                var exitCode = await RunAsync("/usr/bin/dpkg-deb", $"-x \"{debFile}\" \"{extractDir}\"", tempDir, ct);
                if (exitCode != 0)
                {
                    exitCode = await RunAsync("/usr/bin/ar", $"x \"{debFile}\"", tempDir, ct);
                    if (exitCode != 0)
                        throw new InvalidOperationException($"Failed to extract {debFile}");

                    var dataTar = Directory.GetFiles(tempDir, "data.tar.*").FirstOrDefault()
                        ?? throw new FileNotFoundException("data.tar not found in .deb package");
                    exitCode = await RunAsync("/bin/tar", $"xf \"{dataTar}\" -C \"{extractDir}\"", tempDir, ct);
                    if (exitCode != 0)
                        throw new InvalidOperationException($"Failed to extract {dataTar}");
                }
            }

            // Move extracted PG binaries to expected location
            var pgBinSrc = Path.Combine(extractDir, "usr", "lib", "postgresql", PgMajor, "bin");
            var pgLibSrc = Path.Combine(extractDir, "usr", "lib", "postgresql", PgMajor, "lib");
            var pgShareSrc = Path.Combine(extractDir, "usr", "share", "postgresql", PgMajor);
            var pgsqlDir = Path.Combine(CoveDir, "pgsql");
            Directory.CreateDirectory(pgsqlDir);

            if (Directory.Exists(pgBinSrc))
                Directory.Move(pgBinSrc, BinDir);
            if (Directory.Exists(pgLibSrc))
                Directory.Move(pgLibSrc, Path.Combine(pgsqlDir, "lib"));
            if (Directory.Exists(pgShareSrc))
                Directory.Move(pgShareSrc, Path.Combine(pgsqlDir, "share"));

            await RunAsync("/bin/chmod", $"-R +x \"{BinDir}\"", CoveDir, ct);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        }
    }

    private async Task EnsurePgvectorInstalledAsync(CancellationToken ct)
    {
        if (await PgvectorFilesAvailableAsync(ct))
        {
            _logger.LogInformation("pgvector extension files are available for managed PostgreSQL");
            return;
        }

        if (await TryInstallBundledPgvectorAsync(ct))
        {
            _logger.LogInformation("Installed bundled pgvector extension files for managed PostgreSQL");
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var installed = await TryInstallLinuxPgvectorPackageAsync(ct);
            if (installed && await PgvectorFilesAvailableAsync(ct))
            {
                _logger.LogInformation("Installed pgvector extension files for managed PostgreSQL");
                return;
            }
        }

        throw new InvalidOperationException(BuildPgvectorUnavailableMessage());
    }

    private async Task<bool> TryInstallBundledPgvectorAsync(CancellationToken ct)
    {
        var bundleRoot = FindBundledPgvectorRoot();
        if (bundleRoot == null)
        {
            _logger.LogInformation("Bundled pgvector payload was not found. Searched: {BundleDirs}", string.Join(Path.PathSeparator, BundledPgvectorCandidateDirs()));
            return false;
        }

        var pkglibDir = await PgConfigPathAsync("--pkglibdir", ct)
            ?? throw new InvalidOperationException("Could not resolve managed PostgreSQL pkglibdir with pg_config.");
        var sharedDir = await PgConfigPathAsync("--sharedir", ct)
            ?? throw new InvalidOperationException("Could not resolve managed PostgreSQL sharedir with pg_config.");

        var libraryPath = FindPgvectorLibrary(bundleRoot)
            ?? throw new InvalidOperationException($"Bundled pgvector payload at '{bundleRoot}' does not contain one of: {string.Join(", ", ExpectedPgvectorLibraryNames())}.");
        var controlPath = FindPgvectorControlFile(bundleRoot)
            ?? throw new InvalidOperationException($"Bundled pgvector payload at '{bundleRoot}' does not contain vector.control.");

        Directory.CreateDirectory(pkglibDir);
        File.Copy(libraryPath, Path.Combine(pkglibDir, Path.GetFileName(libraryPath)), overwrite: true);

        var extensionDir = Path.Combine(sharedDir, "extension");
        Directory.CreateDirectory(extensionDir);
        File.Copy(controlPath, Path.Combine(extensionDir, "vector.control"), overwrite: true);

        foreach (var sqlPath in Directory.EnumerateFiles(bundleRoot, "vector--*.sql", SearchOption.AllDirectories))
        {
            File.Copy(sqlPath, Path.Combine(extensionDir, Path.GetFileName(sqlPath)), overwrite: true);
        }

        await CopyBundledPgvectorHeadersAsync(bundleRoot, ct);

        if (await PgvectorFilesAvailableAsync(ct))
            return true;

        throw new InvalidOperationException($"Bundled pgvector payload at '{bundleRoot}' was copied but managed PostgreSQL still cannot see pgvector extension files.");
    }

    private string? FindBundledPgvectorRoot()
    {
        return BundledPgvectorCandidateDirs().FirstOrDefault(path =>
            Directory.Exists(path)
            && FindPgvectorLibrary(path) != null
            && FindPgvectorControlFile(path) != null);
    }

    private IEnumerable<string> BundledPgvectorCandidateDirs()
    {
        foreach (var baseDir in RuntimeBaseDirs())
        {
            yield return Path.Combine(baseDir, "runtimes", CurrentRuntimeId(), "native", "postgresql", $"pg{PgMajor}", "pgvector");
            yield return Path.Combine(baseDir, "postgresql", $"pg{PgMajor}", "pgvector", CurrentRuntimeId());
            yield return Path.Combine(baseDir, "pgvector", $"pg{PgMajor}", CurrentRuntimeId());
            yield return Path.Combine(baseDir, "pgvector");
        }
    }

    private static IEnumerable<string> RuntimeBaseDirs()
    {
        var comparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);

        foreach (var path in new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath),
            Directory.GetCurrentDirectory(),
        })
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var fullPath = Path.GetFullPath(path);
            if (seen.Add(fullPath))
                yield return fullPath;
        }
    }

    private static string CurrentRuntimeId()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "osx"
                : "linux";

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        };

        return $"{os}-{arch}";
    }

    private static IReadOnlyList<string> ExpectedPgvectorLibraryNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ["vector.dll"];

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ["vector.so", "vector.dylib"];

        return ["vector.so"];
    }

    private static string? FindPgvectorLibrary(string root)
    {
        foreach (var fileName in ExpectedPgvectorLibraryNames())
        {
            var match = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (match != null)
                return match;
        }

        return null;
    }

    private static string? FindPgvectorControlFile(string root)
        => Directory.EnumerateFiles(root, "vector.control", SearchOption.AllDirectories).FirstOrDefault();

    private async Task CopyBundledPgvectorHeadersAsync(string bundleRoot, CancellationToken ct)
    {
        var sourceHeadersDir = Path.Combine(bundleRoot, "include", "server", "extension", "vector");
        if (!Directory.Exists(sourceHeadersDir))
            return;

        var includeServerDir = await PgConfigPathAsync("--includedir-server", ct);
        if (string.IsNullOrWhiteSpace(includeServerDir))
            return;

        var targetHeadersDir = Path.Combine(includeServerDir, "extension", "vector");
        Directory.CreateDirectory(targetHeadersDir);
        foreach (var headerPath in Directory.EnumerateFiles(sourceHeadersDir, "*.h", SearchOption.TopDirectoryOnly))
        {
            File.Copy(headerPath, Path.Combine(targetHeadersDir, Path.GetFileName(headerPath)), overwrite: true);
        }
    }

    private async Task<bool> TryInstallLinuxPgvectorPackageAsync(CancellationToken ct)
    {
        if (!File.Exists("/usr/bin/apt-get"))
            return false;

        try
        {
            await TryAddPgdgRepoAsync(ct);
            var exitCode = await RunAsync(
                "/usr/bin/apt-get",
                $"install -y postgresql-{PgMajor}-pgvector",
                CoveDir,
                ct);
            if (exitCode == 0)
                return true;

            _logger.LogWarning("apt-get install postgresql-{Major}-pgvector failed (exit {Code})", PgMajor, exitCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install postgresql-{Major}-pgvector via apt-get", PgMajor);
        }

        return false;
    }

    private async Task<bool> PgvectorFilesAvailableAsync(CancellationToken ct)
    {
        var sharedDir = await PgConfigPathAsync("--sharedir", ct);
        if (string.IsNullOrWhiteSpace(sharedDir))
            return false;

        var controlFile = Path.Combine(sharedDir, "extension", "vector.control");
        if (!File.Exists(controlFile))
            return false;

        var pkglibDir = await PgConfigPathAsync("--pkglibdir", ct);
        if (string.IsNullOrWhiteSpace(pkglibDir) || !Directory.Exists(pkglibDir))
            return true;

        return ExpectedPgvectorLibraryNames()
            .Select(name => Path.Combine(pkglibDir, name))
            .Any(File.Exists);
    }

    private async Task<string?> PgConfigPathAsync(string argument, CancellationToken ct)
    {
        var pgConfig = Exe("pg_config");
        if (!File.Exists(pgConfig))
            return null;

        var (exitCode, stdout) = await RunWithOutputAsync(pgConfig, argument, BinDir, ct);
        return exitCode == 0 ? stdout.Trim() : null;
    }

    private static string BuildPgvectorUnavailableMessage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Managed PostgreSQL could not install pgvector automatically because this Cove build does not include the Windows pgvector payload for PostgreSQL " + PgMajor + ". Reinstall the full Cove native package, use Docker, or configure Cove with an external PostgreSQL server that already has pgvector installed.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "Managed PostgreSQL could not install pgvector automatically because this Cove build does not include the macOS pgvector payload for PostgreSQL " + PgMajor + ". Reinstall the full Cove native package, use Docker, or configure Cove with an external PostgreSQL server that already has pgvector installed.";
        }

        return "Managed PostgreSQL could not install pgvector automatically. Reinstall the full Cove native package, install the official PostgreSQL pgvector package for PostgreSQL " + PgMajor + ", use Docker, or configure Cove with an external PostgreSQL server that already has pgvector installed.";
    }

    private static string BuildPgvectorCreateExtensionFailureMessage(string database)
        => $"Could not enable pgvector in database '{database}'. Install pgvector extension files for this PostgreSQL server and rerun Cove migrations.";

    private async Task TryAddPgdgRepoAsync(CancellationToken ct)
    {
        const string pgdgListPath = "/etc/apt/sources.list.d/pgdg.list";
        if (File.Exists(pgdgListPath)) return; // Already configured

        try
        {
            // Download and add PGDG signing key
            await RunAsync("/bin/bash", "-c \"curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor -o /etc/apt/trusted.gpg.d/pgdg.gpg\"", CoveDir, ct);

            // Detect codename
            var codename = "noble";
            if (File.Exists("/etc/os-release"))
            {
                var osRelease = await File.ReadAllTextAsync("/etc/os-release", ct);
                foreach (var line in osRelease.Split('\n'))
                {
                    if (line.StartsWith("VERSION_CODENAME="))
                    {
                        codename = line.Split('=')[1].Trim().Trim('"');
                        break;
                    }
                }
            }

            await File.WriteAllTextAsync(pgdgListPath,
                $"deb https://apt.postgresql.org/pub/repos/apt {codename}-pgdg main\n", ct);
            await RunAsync("/usr/bin/apt-get", "update -qq", CoveDir, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not add PGDG repo — will try default apt packages");
        }
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _logger.LogInformation("Downloading {Url}", url);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long totalRead = 0;
        int lastPct = -1;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            if (totalBytes > 0)
            {
                int pct = (int)(totalRead * 100 / totalBytes);
                if (pct / 10 > lastPct / 10)
                {
                    _logger.LogInformation("  Download progress: {Pct}% ({MB:F0} MB)",
                        pct, totalRead / 1048576.0);
                    lastPct = pct;
                }
            }
        }
        await fileStream.FlushAsync(ct);
        fileStream.Close();
        _logger.LogInformation("Download complete ({MB:F1} MB)", totalRead / 1048576.0);
    }

    // ─── Init / Start / Stop helpers ────────────────────────────────

    private async Task InitDbAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(DataDir);
        var exitCode = await RunAsync(Exe("initdb"),
            $"-D \"{DataDir}\" -U postgres --encoding=UTF8 --locale=C --auth=trust",
            BinDir, ct);

        if (exitCode != 0)
            throw new InvalidOperationException($"initdb failed (exit code {exitCode}). Check {LogFile}");

        // Write pg_hba.conf — local-only trust auth
        await File.WriteAllTextAsync(Path.Combine(DataDir, "pg_hba.conf"),
            """
            # TYPE  DATABASE  USER  ADDRESS       METHOD
            local   all       all                 trust
            host    all       all   127.0.0.1/32  trust
            host    all       all   ::1/128       trust
            """, ct);

        // Append to postgresql.conf
        await File.AppendAllTextAsync(Path.Combine(DataDir, "postgresql.conf"),
            $"""

            # ── Cove managed ──
            port = {_config.Port}
            listen_addresses = '127.0.0.1'
            max_connections = 150
            shared_buffers = 128MB
            log_destination = 'stderr'
            logging_collector = off
            """, ct);
    }

    private async Task EnsureManagedConfigurationAsync(CancellationToken ct)
    {
        var configPath = Path.Combine(DataDir, "postgresql.conf");
        if (!File.Exists(configPath))
            return;

        var desiredSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["port"] = _config.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["listen_addresses"] = "'127.0.0.1'",
            ["max_connections"] = "150",
            ["shared_buffers"] = "128MB",
            ["log_destination"] = "'stderr'",
            ["logging_collector"] = "off",
        };

        var lines = (await File.ReadAllLinesAsync(configPath, ct)).ToList();
        var changed = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
                continue;

            foreach (var (key, value) in desiredSettings)
            {
                if (!IsSettingLine(trimmed, key))
                    continue;

                var nextLine = $"{key} = {value}";
                seen.Add(key);
                if (!string.Equals(line.Trim(), nextLine, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = nextLine;
                    changed = true;
                }
                break;
            }
        }

        var missingSettings = desiredSettings.Where(setting => !seen.Contains(setting.Key)).ToArray();
        if (missingSettings.Length > 0)
        {
            lines.Add("");
            lines.Add("# -- Cove managed --");
            lines.AddRange(missingSettings.Select(setting => $"{setting.Key} = {setting.Value}"));
            changed = true;
        }

        if (!changed)
            return;

        await File.WriteAllLinesAsync(configPath, lines, ct);
        _logger.LogInformation("Updated managed PostgreSQL configuration at {ConfigPath}", configPath);
    }

    private static bool IsSettingLine(string trimmedLine, string settingName)
    {
        if (!trimmedLine.StartsWith(settingName, StringComparison.OrdinalIgnoreCase))
            return false;

        return trimmedLine.Length == settingName.Length
            || char.IsWhiteSpace(trimmedLine[settingName.Length])
            || trimmedLine[settingName.Length] == '=';
    }

    private async Task PgCtlAsync(string args, CancellationToken ct)
    {
        var exitCode = await RunAsync(Exe("pg_ctl"), args, BinDir, ct);
        if (exitCode != 0)
        {
            var lastLines = await ReadLogTailAsync(20, ct);
            throw new InvalidOperationException(
                $"pg_ctl failed (exit code {exitCode}). Last log lines:\n{lastLines}");
        }
    }

    private async Task<string> ReadLogTailAsync(int lineCount, CancellationToken ct)
    {
        if (!File.Exists(LogFile)) return "(no log file)";

        try
        {
            await using var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var logContent = await reader.ReadToEndAsync(ct);
            return string.Join('\n', logContent.Split('\n').TakeLast(lineCount));
        }
        catch (IOException ex)
        {
            return $"(log unavailable: {ex.Message})";
        }
    }

    private async Task StopStaleInstanceAsync(CancellationToken ct)
    {
        var pidFile = Path.Combine(DataDir, "postmaster.pid");
        if (!File.Exists(pidFile)) return;

        _logger.LogInformation("Found stale postmaster.pid — stopping previous instance");
        try
        {
            await RunAsync(Exe("pg_ctl"), $"stop -D \"{DataDir}\" -m fast", BinDir, ct);
        }
        catch (Exception ex)
        {
            // If it fails (process already dead), just remove the pid file
            _logger.LogWarning(ex, "Failed to stop stale PostgreSQL instance; attempting to delete {PidFile}", pidFile);
            try
            {
                File.Delete(pidFile);
                _logger.LogInformation("Deleted stale PostgreSQL pid file {PidFile}", pidFile);
            }
            catch (Exception deleteEx)
            {
                _logger.LogWarning(deleteEx, "Failed to delete stale PostgreSQL pid file {PidFile}", pidFile);
            }
        }
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        for (int i = 0; i < 240; i++)
        {
            ct.ThrowIfCancellationRequested();
            var exitCode = await RunAsync(Exe("pg_isready"),
                $"-h 127.0.0.1 -p {_config.Port} -U postgres", BinDir, ct);
            if (exitCode == 0)
            {
                _logger.LogDebug("PostgreSQL is accepting connections");
                return;
            }
            await Task.Delay(500, ct);
        }

        var lastLines = await ReadLogTailAsync(30, ct);
        throw new TimeoutException(
            $"PostgreSQL did not become ready within 120 seconds. Log:\n{lastLines}");
    }

    private async Task EnsureDatabaseAsync(CancellationToken ct)
    {
        // Check if database exists via psql
        var (exitCode, stdout) = await RunWithOutputAsync(Exe("psql"),
            $"-h 127.0.0.1 -p {_config.Port} -U postgres -tAc \"SELECT 1 FROM pg_database WHERE datname='{_config.Database}'\"",
            BinDir, ct);

        if (stdout.Trim() == "1")
        {
            _logger.LogDebug("Database '{Db}' already exists", _config.Database);

            // Ensure pgvector extension is created
            var vectorExitCode = await RunAsync(Exe("psql"),
                $"-h 127.0.0.1 -p {_config.Port} -U postgres -d {_config.Database} -c \"CREATE EXTENSION IF NOT EXISTS vector\"",
                BinDir, ct);
            if (vectorExitCode != 0)
                throw new InvalidOperationException(BuildPgvectorCreateExtensionFailureMessage(_config.Database));
            return;
        }

        _logger.LogInformation("Creating database '{Db}'", _config.Database);
        exitCode = await RunAsync(Exe("createdb"),
            $"-h 127.0.0.1 -p {_config.Port} -U postgres {_config.Database}", BinDir, ct);

        if (exitCode != 0)
            throw new InvalidOperationException($"createdb failed (exit code {exitCode})");

        // Try to create pgvector extension (will fail silently if not available)
        var extResult = await RunAsync(Exe("psql"),
            $"-h 127.0.0.1 -p {_config.Port} -U postgres -d {_config.Database} -c \"CREATE EXTENSION IF NOT EXISTS vector\"",
            BinDir, ct);

        if (extResult != 0)
            throw new InvalidOperationException(BuildPgvectorCreateExtensionFailureMessage(_config.Database));
    }

    // ─── Process helpers ────────────────────────────────────────────

    private async Task<int> RunAsync(string exe, string args, string workDir, CancellationToken ct)
    {
        _logger.LogDebug("Exec: {Exe} {Args}", Path.GetFileName(exe), args);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Ensure the PG bin dir is on PATH so sub-processes can find each other
        var path = psi.Environment.TryGetValue("PATH", out var existing) ? existing : "";
        psi.Environment["PATH"] = $"{BinDir}{Path.PathSeparator}{path}";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}");
        await proc.WaitForExitAsync(ct);
        return proc.ExitCode;
    }

    private async Task<(int exitCode, string stdout)> RunWithOutputAsync(
        string exe, string args, string workDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var path = psi.Environment.TryGetValue("PATH", out var existing) ? existing : "";
        psi.Environment["PATH"] = $"{BinDir}{Path.PathSeparator}{path}";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}");
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        await stderrTask;
        return (proc.ExitCode, stdout);
    }
}
