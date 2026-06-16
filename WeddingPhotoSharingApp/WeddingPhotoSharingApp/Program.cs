using WeddingPhotoSharingApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // U produkciji zameni sa tačnim domenom: https://milicaidusanvencanje.com
        var allowedOrigins = builder.Configuration
            .GetSection("AllowedOrigins")
            .Get<string[]>() ?? ["*"];

        if (allowedOrigins.Contains("*"))
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddSingleton<GoogleDriveService>();

var app = builder.Build();

app.UseCors();

// ── Health check ──────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new { status = "ok", message = "Wedding Upload API" }));

// ── Upload endpoint ───────────────────────────────────────────────────
app.MapPost("/upload", async Task<IResult> (HttpRequest request, GoogleDriveService drive) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Očekujem multipart/form-data." });

    var form = await request.ReadFormAsync();
    var files = form.Files;
    var guestName = form["guestName"].ToString().Trim();

    if (files.Count == 0)
        return Results.BadRequest(new { error = "Nema fajlova u zahtevu." });

    // Validacija: samo slike, max 20MB po fajlu, max 20 fajlova
    const long maxFileSize = 20 * 1024 * 1024;
    const int maxFiles = 20;

    if (files.Count > maxFiles)
        return Results.BadRequest(new { error = $"Maksimalno {maxFiles} slika odjednom." });

    var invalidFiles = files.Where(f =>
        !f.ContentType.StartsWith("image/") || f.Length > maxFileSize).ToList();

    if (invalidFiles.Count > 0)
        return Results.BadRequest(new { error = "Neki fajlovi nisu slike ili su preveliki (max 20MB)." });

    var results = new List<UploadResult>();

    foreach (var file in files)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var safeName = SanitizeFileName(file.FileName);
        var safeGuest = SanitizeFileName(guestName);
        var fileName = string.IsNullOrEmpty(safeGuest)
            ? $"{timestamp}_{safeName}"
            : $"{safeGuest}_{timestamp}_{safeName}";

        using var stream = file.OpenReadStream();
        var fileId = await drive.UploadFileAsync(stream, fileName, file.ContentType);

        results.Add(new UploadResult(fileName, fileId, fileId != null));
    }

    var allOk = results.All(r => r.Success);
    var anyOk = results.Any(r => r.Success);

    return allOk
        ? Results.Ok(new { message = $"Uspešno uploadovano {results.Count} slika. Hvala!", results })
        : anyOk
            ? Results.Ok(new { message = "Neke slike nisu uploadovane.", results })
            : Results.StatusCode(500);
})
.DisableAntiforgery();

// ── Galerija endpoint ─────────────────────────────────────────────────
app.MapGet("/gallery", async (GoogleDriveService drive, int? limit) =>
{
    var photos = await drive.ListPhotosAsync(limit ?? 200);
    return Results.Ok(new { photos, count = photos.Count });
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();

// ── Helper ────────────────────────────────────────────────────────────
static string SanitizeFileName(string name)
{
    if (string.IsNullOrWhiteSpace(name)) return string.Empty;
    var invalid = Path.GetInvalidFileNameChars();
    return new string(name.Where(c => !invalid.Contains(c)).ToArray())
        .Replace(" ", "_")
        .Trim('_')
        [..Math.Min(name.Length, 50)];
}

record UploadResult(string FileName, string? FileId, bool Success);
