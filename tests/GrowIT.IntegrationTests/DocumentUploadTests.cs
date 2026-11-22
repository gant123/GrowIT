using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using GrowIT.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClientEntity = GrowIT.Core.Entities.Client;

namespace GrowIT.Backend.Tests;

/// <summary>
/// Real document upload (finding #57): files are stored in a private folder outside wwwroot,
/// served only through the authenticated, tenant-checked download endpoint, and validated by
/// size, extension and actual content.
/// </summary>
public sealed class DocumentUploadTests : IDisposable
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n%%EOF\n");

    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"growit-doc-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private GrowItApiFactory CreateFactory() =>
        new(new Dictionary<string, string?> { ["Storage:DocumentsPath"] = _storageRoot });

    [Fact]
    public async Task Upload_StoresFileUnderGeneratedName_AndListsMetadata()
    {
        using var factory = CreateFactory();
        var tenantId = Guid.NewGuid();
        var clientId = await SeedClientAsync(factory, tenantId, "Maria", "Alvarez");

        using var client = factory.CreateTenantClient(tenantId);
        using var response = await client.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "Maria lease 2026.pdf", "application/pdf", clientId, DocumentCategory.Contract, "Apartment lease"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documentId = await response.ReadGuidPropertyAsync("documentId");

        var documents = await (await client.GetAsync("/api/documents")).ReadRequiredJsonAsync<List<DocumentDto>>();
        var document = Assert.Single(documents);
        Assert.Equal(documentId, document.Id);
        Assert.True(document.IsUploaded);
        Assert.Equal(clientId, document.ClientId);
        Assert.Equal("Maria Alvarez", document.ClientName);
        Assert.Equal("Apartment lease", document.Title);
        Assert.Equal("Maria lease 2026.pdf", document.OriginalFileName);
        Assert.Equal("PDF", document.FileType);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal(PdfBytes.Length, document.SizeBytes);
        Assert.Equal(DocumentCategory.Contract, document.Category);
        Assert.NotNull(document.UploadedAt);
        Assert.Null(document.LinkUrl);

        // Stored in the private store, in the tenant's folder, under a generated name.
        var stored = Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories);
        var storedFile = Assert.Single(stored);
        Assert.Equal(tenantId.ToString("N"), Path.GetFileName(Path.GetDirectoryName(storedFile)));
        Assert.DoesNotContain("lease", Path.GetFileName(storedFile), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", storedFile);
        Assert.Equal(PdfBytes, await File.ReadAllBytesAsync(storedFile));

        var storagePath = await ReadStoragePathAsync(factory, documentId);
        Assert.False(storagePath!.StartsWith('/'));
        Assert.DoesNotContain("uploads", storagePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_WithoutPerson_IsAnOrganizationDocument()
    {
        using var factory = CreateFactory();
        var tenantId = Guid.NewGuid();
        await SeedClientAsync(factory, tenantId, "Luis", "Ortega");

        using var client = factory.CreateTenantClient(tenantId);
        using var response = await client.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "irs-letter.pdf", "application/pdf", clientId: null, DocumentCategory.Legal, "IRS determination letter"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var organizationDocs = await (await client.GetAsync("/api/documents?scope=Organization")).ReadRequiredJsonAsync<List<DocumentDto>>();
        var document = Assert.Single(organizationDocs);
        Assert.Null(document.ClientId);
        Assert.Null(document.ClientName);
        Assert.Equal(DocumentCategory.Legal, document.Category);

        var peopleDocs = await (await client.GetAsync("/api/documents?scope=People")).ReadRequiredJsonAsync<List<DocumentDto>>();
        Assert.Empty(peopleDocs);
    }

    [Fact]
    public async Task Download_ReturnsBytesAsAttachment_WithSafeFileName()
    {
        using var factory = CreateFactory();
        var tenantId = Guid.NewGuid();
        using var client = factory.CreateTenantClient(tenantId);

        // A hostile client-supplied name (path traversal, quotes, header separators). HttpClient
        // refuses to build such a header itself, so write the raw multipart header.
        using var form = new MultipartFormDataContent();
        var filePart = new ByteArrayContent(PdfBytes);
        filePart.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        filePart.Headers.TryAddWithoutValidation(
            "Content-Disposition",
            "form-data; name=\"file\"; filename=\"../../bylaws <final>;.pdf\"");
        form.Add(filePart);
        form.Add(new StringContent("Governance"), "Category");
        using var upload = await client.PostAsync("/api/documents/upload", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var documentId = await upload.ReadGuidPropertyAsync("documentId");

        using var response = await client.GetAsync($"/api/documents/{documentId}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PdfBytes, await response.Content.ReadAsByteArrayAsync());

        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        var fileName = (disposition.FileNameStar ?? disposition.FileName)!.Trim('"');
        Assert.EndsWith(".pdf", fileName);
        Assert.DoesNotContain("..", fileName);
        Assert.DoesNotContain("\\", fileName);
        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain("\"", fileName);
        Assert.DoesNotContain(";", fileName);
        Assert.DoesNotContain("<", fileName);
        Assert.Contains("bylaws", fileName);

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Documents_AreIsolatedBetweenTenants()
    {
        using var factory = CreateFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var clientA = factory.CreateTenantClient(tenantA);
        using var upload = await clientA.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "id-card.pdf", "application/pdf", clientId: null, DocumentCategory.ID));
        var documentId = await upload.ReadGuidPropertyAsync("documentId");

        using var clientB = factory.CreateTenantClient(tenantB);

        var listB = await (await clientB.GetAsync("/api/documents")).ReadRequiredJsonAsync<List<DocumentDto>>();
        Assert.Empty(listB);

        using var downloadB = await clientB.GetAsync($"/api/documents/{documentId}/download");
        Assert.Equal(HttpStatusCode.NotFound, downloadB.StatusCode);

        using var deleteB = await clientB.DeleteAsync($"/api/documents/{documentId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteB.StatusCode);

        // Still there for its owner.
        Assert.Single(Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories));
        using var downloadA = await clientA.GetAsync($"/api/documents/{documentId}/download");
        Assert.Equal(HttpStatusCode.OK, downloadA.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsPersonFromAnotherTenant()
    {
        using var factory = CreateFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var otherTenantsClientId = await SeedClientAsync(factory, tenantB, "Bob", "Elsewhere");

        using var clientA = factory.CreateTenantClient(tenantA);
        using var response = await clientA.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "lease.pdf", "application/pdf", otherTenantsClientId, DocumentCategory.Contract));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(Directory.Exists(_storageRoot) && Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public async Task StorageService_RefusesKeysOutsideTheTenantFolder()
    {
        using var factory = CreateFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var clientA = factory.CreateTenantClient(tenantA);
        using var upload = await clientA.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "budget.pdf", "application/pdf", clientId: null, DocumentCategory.Financial));
        var documentId = await upload.ReadGuidPropertyAsync("documentId");
        var storagePath = await ReadStoragePathAsync(factory, documentId);

        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<GrowIT.Core.Interfaces.IFileStorageService>();

        Assert.Null(await storage.OpenDocumentAsync(tenantB, storagePath!));
        Assert.Null(await storage.OpenDocumentAsync(tenantA, $"{tenantA:N}/../{tenantB:N}/x.pdf"));
        Assert.Null(await storage.OpenDocumentAsync(tenantA, "/etc/passwd"));

        await using var owned = await storage.OpenDocumentAsync(tenantA, storagePath!);
        Assert.NotNull(owned);
    }

    [Theory]
    [InlineData("page.html", "text/html", "<html><script>alert(1)</script></html>")]
    [InlineData("tool.exe", "application/octet-stream", "MZ binary")]
    [InlineData("drawing.svg", "image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    public async Task Upload_RejectsTypesOutsideTheAllowList(string fileName, string contentType, string body)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid());

        using var response = await client.PostAsync("/api/documents/upload",
            BuildUpload(Encoding.UTF8.GetBytes(body), fileName, contentType, clientId: null, DocumentCategory.Other));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("isn't supported", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_RejectsContentThatDoesNotMatchTheExtension()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid());

        // An HTML page renamed to .pdf (and labelled application/pdf by the client).
        using var response = await client.PostAsync("/api/documents/upload",
            BuildUpload(Encoding.UTF8.GetBytes("<html><body>not a pdf</body></html>"), "statement.pdf", "application/pdf", null, DocumentCategory.Bill));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(Directory.Exists(_storageRoot) && Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public async Task Upload_RejectsFilesOverTheSizeLimit()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid());

        var tooLarge = new byte[DocumentUploadRules.MaxFileBytes + 1];
        PdfBytes.CopyTo(tooLarge, 0);

        using var response = await client.PostAsync("/api/documents/upload",
            BuildUpload(tooLarge, "scan.pdf", "application/pdf", null, DocumentCategory.Other));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("10 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_WithoutAFile_ExplainsWhatIsMissing()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid());

        using var form = new MultipartFormDataContent { { new StringContent("Other"), "Category" } };
        using var response = await client.PostAsync("/api/documents/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Choose a file", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Member")]
    [InlineData("Analyst")]
    public async Task Upload_And_Delete_RequireServiceWriterRole(string role)
    {
        using var factory = CreateFactory();
        var tenantId = Guid.NewGuid();

        using var admin = factory.CreateTenantClient(tenantId, role: "Admin");
        using var upload = await admin.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "policy.pdf", "application/pdf", null, DocumentCategory.Governance));
        var documentId = await upload.ReadGuidPropertyAsync("documentId");

        using var reader = factory.CreateTenantClient(tenantId, role: role);
        using var readerUpload = await reader.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "other.pdf", "application/pdf", null, DocumentCategory.Other));
        Assert.Equal(HttpStatusCode.Forbidden, readerUpload.StatusCode);

        using var readerDelete = await reader.DeleteAsync($"/api/documents/{documentId}");
        Assert.Equal(HttpStatusCode.Forbidden, readerDelete.StatusCode);

        // Reading follows the list endpoint's policy: any signed-in member of the organization.
        using var readerDownload = await reader.GetAsync($"/api/documents/{documentId}/download");
        Assert.Equal(HttpStatusCode.OK, readerDownload.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheRecordAndTheStoredFile()
    {
        using var factory = CreateFactory();
        var tenantId = Guid.NewGuid();
        using var client = factory.CreateTenantClient(tenantId, role: "Case Manager");

        using var upload = await client.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "receipt.pdf", "application/pdf", null, DocumentCategory.Bill));
        var documentId = await upload.ReadGuidPropertyAsync("documentId");
        Assert.Single(Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories));

        using var delete = await client.DeleteAsync($"/api/documents/{documentId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories));
        var documents = await (await client.GetAsync("/api/documents")).ReadRequiredJsonAsync<List<DocumentDto>>();
        Assert.Empty(documents);
    }

    [Fact]
    public async Task LegacyLinkDocuments_AreListedAsLinks_AndCannotBeDownloaded()
    {
        using var factory = CreateFactory();
        var tenantId = Guid.NewGuid();
        var clientId = await SeedClientAsync(factory, tenantId, "Ana", "Legacy");
        var documentId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Documents.Add(new Document
            {
                Id = documentId,
                TenantId = tenantId,
                ClientId = clientId,
                FileUrl = "https://drive.example.org/lease",
                FileType = "PDF",
                Category = DocumentCategory.Contract
            });
            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var document = Assert.Single(await (await client.GetAsync("/api/documents")).ReadRequiredJsonAsync<List<DocumentDto>>());
        Assert.False(document.IsUploaded);
        Assert.Equal("https://drive.example.org/lease", document.LinkUrl);

        using var download = await client.GetAsync($"/api/documents/{documentId}/download");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task DocumentStore_InsideWebRoot_IsRefused()
    {
        var relativeInsideWebRoot = Path.Combine("wwwroot", $"docs-test-{Guid.NewGuid():N}");
        using var factory = new GrowItApiFactory(new Dictionary<string, string?> { ["Storage:DocumentsPath"] = relativeInsideWebRoot });
        using var client = factory.CreateTenantClient(Guid.NewGuid());

        using var response = await client.PostAsync("/api/documents/upload",
            BuildUpload(PdfBytes, "public.pdf", "application/pdf", null, DocumentCategory.Other));

        var environment = factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        var wouldBePath = Path.Combine(environment.ContentRootPath, relativeInsideWebRoot);
        try
        {
            Assert.False(response.IsSuccessStatusCode);
            Assert.False(Directory.Exists(wouldBePath));
        }
        finally
        {
            if (Directory.Exists(wouldBePath))
            {
                Directory.Delete(wouldBePath, recursive: true);
            }
        }
    }

    private static MultipartFormDataContent BuildUpload(
        byte[] bytes,
        string fileName,
        string contentType,
        Guid? clientId,
        DocumentCategory category,
        string? title = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        if (clientId.HasValue)
        {
            form.Add(new StringContent(clientId.Value.ToString()), "ClientId");
        }

        form.Add(new StringContent(category.ToString()), "Category");
        if (title is not null)
        {
            form.Add(new StringContent(title), "Title");
        }

        return form;
    }

    private static async Task<Guid> SeedClientAsync(GrowItApiFactory factory, Guid tenantId, string firstName, string lastName)
    {
        var clientId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Clients.Add(new ClientEntity
            {
                Id = clientId,
                TenantId = tenantId,
                FirstName = firstName,
                LastName = lastName,
                HouseholdCount = 1,
                StabilityScore = 5,
                LifePhase = LifePhase.Stable
            });
            return Task.CompletedTask;
        });
        return clientId;
    }

    private static async Task<string?> ReadStoragePathAsync(GrowItApiFactory factory, Guid documentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Documents.IgnoreQueryFilters()
            .Where(d => d.Id == documentId)
            .Select(d => d.StoragePath)
            .SingleAsync();
    }
}
