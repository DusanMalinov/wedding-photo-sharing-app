using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using System.Security.AccessControl;

namespace WeddingPhotoSharingApp.Services
{
    public class GoogleDriveService
    {
        private readonly DriveService _drive;
        private readonly string _folderId;
        private readonly ILogger<GoogleDriveService> _logger;
        private const string GoogleDriveUrl = "https://drive.google.com/";

        public GoogleDriveService(IConfiguration config, ILogger<GoogleDriveService> logger)
        {
            _logger = logger;

            var clientId = config["GoogleDrive:ClientId"]
                ?? throw new InvalidOperationException("GoogleDrive:ClientId nije postavljen.");

            var clientSecret = config["GoogleDrive:ClientSecret"]
                ?? throw new InvalidOperationException("GoogleDrive:ClientSecret nije postavljen.");

            var refreshToken = config["GoogleDrive:RefreshToken"]
                ?? throw new InvalidOperationException("GoogleDrive:RefreshToken nije postavljen.");

            _folderId = config["GoogleDrive:FolderId"]
                ?? throw new InvalidOperationException("GoogleDrive:FolderId nije postavljen.");

            var token = new TokenResponse
            {
                RefreshToken = refreshToken
            };

            var flow = new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = clientId,
                        ClientSecret = clientSecret
                    },
                    Scopes = new[] { DriveService.Scope.Drive }
                });

            var credential = new UserCredential(
                flow,
                "user",
                token);

            _drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Wedding Upload"
            });
        }

        /// <summary>
        /// Uploaduje fajl u Google Drive folder i vraća ID fajla.
        /// </summary>
        public async Task<string?> UploadFileAsync(Stream content, string fileName, string mimeType)
        {
            try
            {
                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = fileName,
                    Parents = [_folderId],
                };

                var request = _drive.Files.Create(fileMetadata, content, mimeType);
                request.Fields = "id, name";

                var result = await request.UploadAsync();

                if (result.Status == Google.Apis.Upload.UploadStatus.Completed)
                {
                    var fileId = request.ResponseBody?.Id;

                    if (!string.IsNullOrEmpty(fileId))
                    {
                        var permission = new Google.Apis.Drive.v3.Data.Permission
                        {
                            Type = "anyone",
                            Role = "reader"
                        };

                        await _drive.Permissions.Create(permission, fileId).ExecuteAsync();
                    }

                    _logger.LogInformation("Uploaded: {FileName} → {FileId}", fileName, fileId);
                    return fileId;
                }

                _logger.LogError("Upload failed for {FileName}: {Exception}", fileName, result.Exception?.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception uploading {FileName}", fileName);
                return null;
            }
        }

        /// <summary>
        /// Vraća listu slika iz Google Drive foldera (sortirano od najnovijeg).
        /// </summary>
        public async Task<List<PhotoDto>> ListPhotosAsync(int limit = 200)
        {
            try
            {
                var request = _drive.Files.List();
                request.Q = $"'{_folderId}' in parents and mimeType contains 'image/' and trashed = false";
                request.Fields = "files(id, name, createdTime, imageMediaMetadata)";
                request.OrderBy = "createdTime desc";
                request.PageSize = Math.Min(limit, 1000);

                var result = await request.ExecuteAsync();

                return result.Files
                    .Select(f => new PhotoDto(
                        Id: f.Id,
                        Name: f.Name,
                        CreatedAt: f.CreatedTimeDateTimeOffset?.ToString("o") ?? string.Empty,
                        // Thumbnail URL koji Google Drive generira automatski
                        ThumbnailUrl: $"{GoogleDriveUrl}thumbnail?id={f.Id}&sz=w400",
                        FullUrl: $"{GoogleDriveUrl}thumbnail?id={f.Id}&sz=w1800"
                    ))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing photos from Drive folder {FolderId}", _folderId);
                return [];
            }
        }
    }

    public record PhotoDto(
        string Id,
        string Name,
        string CreatedAt,
        string ThumbnailUrl,
        string FullUrl
    );
}
