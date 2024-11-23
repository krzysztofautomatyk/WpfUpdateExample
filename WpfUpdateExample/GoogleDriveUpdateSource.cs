using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Download;
using Google.Apis.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack;
using Velopack.Sources;

public class GoogleDriveUpdateSource : IUpdateSource
{
    private readonly string _folderId;
    private readonly DriveService _driveService;
    private readonly Dictionary<string, string> _fileIdMap = new Dictionary<string, string>();

    public GoogleDriveUpdateSource(string folderId)
    {
        _folderId = folderId;
        _driveService = Authenticate();
    }

    private DriveService Authenticate()
    {
        // Implement authentication here
        // For public files, you might not need authentication
        return new DriveService(new BaseClientService.Initializer()
        {
            ApplicationName = "YourAppName",
            // If necessary, include API key or credentials
        });
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(ILogger logger, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        // List files in the Google Drive folder
        var listRequest = _driveService.Files.List();
        listRequest.Q = $"'{_folderId}' in parents and mimeType!='application/vnd.google-apps.folder'";
        listRequest.Fields = "files(id, name, size)";
        var result = await listRequest.ExecuteAsync();

        var assets = new List<VelopackAsset>();

        foreach (var file in result.Files)
        {
            try
            {
                var version = ParseVersionFromFileName(file.Name);
                var asset = new VelopackAsset
                {
                    PackageId = "YourPackageId", // Set your package ID
                    FileName = file.Name,
                    Version = version,
                    Size = file.Size ?? 0,
                    SHA1 = null, // We'll compute this after download
                    Type = VelopackAssetType.Full // Adjust if you have delta packages
                };
                assets.Add(asset);
                _fileIdMap[asset.FileName] = file.Id; // Map FileName to File ID
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to process file '{file.Name}'");
            }
        }

        var feed = new VelopackAssetFeed
        {
            Assets = assets.ToArray()
        };
        return feed;
    }

    public async Task DownloadReleaseEntry(ILogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
    {
        if (!_fileIdMap.TryGetValue(releaseEntry.FileName, out var fileId))
        {
            throw new Exception($"File ID for '{releaseEntry.FileName}' not found.");
        }

        var getRequest = _driveService.Files.Get(fileId);
        getRequest.MediaDownloader.ProgressChanged += (IDownloadProgress p) =>
        {
            switch (p.Status)
            {
                case DownloadStatus.Downloading:
                    {
                        var percent = (int)((p.BytesDownloaded * 100) / releaseEntry.Size);
                        progress?.Invoke(percent);
                        break;
                    }
                case DownloadStatus.Completed:
                    {
                        progress?.Invoke(100);
                        break;
                    }
                case DownloadStatus.Failed:
                    {
                        throw new Exception("Download failed.");
                    }
            }
        };

        // Download the file
        using (var fileStream = new FileStream(localFile, FileMode.Create, FileAccess.Write))
        {
            await getRequest.DownloadAsync(fileStream, cancelToken);
        }

        // Compute SHA1 checksum after download and set it on the releaseEntry
        var sha1Checksum = CalculateFileSHA1(localFile);
        releaseEntry.SHA1 = sha1Checksum;
    }

    private string CalculateFileSHA1(string filePath)
    {
        using (var sha1 = System.Security.Cryptography.SHA1.Create())
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var hash = sha1.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }
    }

    private SemanticVersion ParseVersionFromFileName(string fileName)
    {
        // Implement version parsing logic based on your file naming convention
        // For example, if file name is 'MyApp-1.2.3.nupkg'
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExtension.Split('-');
        if (parts.Length >= 2)
        {
            var versionString = parts.Last();
            if (SemanticVersion.TryParse(versionString, out var version))
            {
                return version;
            }
        }
        throw new Exception($"Could not parse version from file name '{fileName}'.");
    }
}
