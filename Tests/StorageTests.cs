using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Controllers;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text;

namespace Tests;


public class FakeR2StorageService : IR2StorageService
{
    public List<string> Uploaded { get; } = new();
    public List<string> Deleted { get; } = new();

    public Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        Uploaded.Add(key);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Deleted.Add(key);
        return Task.CompletedTask;
    }

    public string GetPresignedDownloadUrl(string key, string fileName, TimeSpan ttl)
        => $"https://fake-r2/{key}?ttl={ttl.TotalSeconds}";
}

public class StorageTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(StorageController controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    private static IFormFile MakeFile(string name, string contentType, int sizeBytes = 1024)
    {
        var bytes = new byte[sizeBytes];
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, sizeBytes, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Fact]
    public async Task Upload_ShouldReturnBadRequest_WhenExtensionNotAllowed()
    {

        var context = GetDbContext();
        var storage = new FakeR2StorageService();
        var controller = new StorageController(context, storage);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });
        await context.SaveChangesAsync();

        SetUser(controller, userId);

        var file = MakeFile("evil.exe", "application/octet-stream");


        var result = await controller.Upload(file);


        result.Should().BeOfType<BadRequestObjectResult>();
        storage.Uploaded.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_ShouldReturnBadRequest_WhenContentTypeMismatch()
    {

        var context = GetDbContext();
        var storage = new FakeR2StorageService();
        var controller = new StorageController(context, storage);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });
        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var file = MakeFile("lec.pdf", "image/jpeg");


        var result = await controller.Upload(file);


        result.Should().BeOfType<BadRequestObjectResult>();
        storage.Uploaded.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_ShouldStoreFile_WhenValid()
    {

        var context = GetDbContext();
        var storage = new FakeR2StorageService();
        var controller = new StorageController(context, storage);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });
        await context.SaveChangesAsync();

        SetUser(controller, userId);

        var file = MakeFile("lec01.pdf", "application/pdf", 2048);


        var result = await controller.Upload(file);


        result.Should().BeOfType<OkObjectResult>();
        storage.Uploaded.Should().HaveCount(1);
        context.LectureMaterials.Should().HaveCount(1);
        context.LectureMaterials.First().FileName.Should().Be("lec01.pdf");
    }

    [Fact]
    public async Task GetMaterials_ShouldReturnOnlyGroupMaterials()
    {

        var context = GetDbContext();
        var storage = new FakeR2StorageService();
        var controller = new StorageController(context, storage);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });

        context.LectureMaterials.Add(new LectureMaterial
        {
            Id = Guid.NewGuid(), GroupId = groupId, UploaderId = userId,
            FileName = "наша.pdf", ContentType = "application/pdf", R2Key = "k1"
        });

        context.LectureMaterials.Add(new LectureMaterial
        {
            Id = Guid.NewGuid(), GroupId = Guid.NewGuid(), UploaderId = userId,
            FileName = "чужа.pdf", ContentType = "application/pdf", R2Key = "k2"
        });

        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var result = await controller.GetMaterials();


        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var materials = okResult.Value as List<LectureMaterialDto>;
        materials.Should().NotBeNull();
        materials!.Should().HaveCount(1);
        materials.First().FileName.Should().Be("наша.pdf");
    }

    [Fact]
    public async Task DeleteMaterial_ShouldReturnForbid_WhenNotUploaderOrOwner()
    {

        var context = GetDbContext();
        var storage = new FakeR2StorageService();
        var controller = new StorageController(context, storage);
        var groupId = Guid.NewGuid();
        var uploaderId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        var group = new Group { Id = groupId, Name = "КН-31", OwnerId = Guid.NewGuid() };
        var material = new LectureMaterial
        {
            Id = Guid.NewGuid(), GroupId = groupId, Group = group,
            UploaderId = uploaderId, FileName = "lec.pdf",
            ContentType = "application/pdf", R2Key = "key"
        };

        context.Groups.Add(group);
        context.LectureMaterials.Add(material);
        await context.SaveChangesAsync();

        SetUser(controller, strangerId);


        var result = await controller.DeleteMaterial(material.Id);


        result.Should().BeOfType<ForbidResult>();
        storage.Deleted.Should().BeEmpty();
        context.LectureMaterials.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteMaterial_ShouldRemoveBothFromR2AndDb_WhenUploader()
    {

        var context = GetDbContext();
        var storage = new FakeR2StorageService();
        var controller = new StorageController(context, storage);
        var groupId = Guid.NewGuid();
        var uploaderId = Guid.NewGuid();

        var group = new Group { Id = groupId, Name = "КН-31", OwnerId = Guid.NewGuid() };
        var material = new LectureMaterial
        {
            Id = Guid.NewGuid(), GroupId = groupId, Group = group,
            UploaderId = uploaderId, FileName = "lec.pdf",
            ContentType = "application/pdf", R2Key = "my-key"
        };

        context.Groups.Add(group);
        context.LectureMaterials.Add(material);
        await context.SaveChangesAsync();

        SetUser(controller, uploaderId);


        var result = await controller.DeleteMaterial(material.Id);


        result.Should().BeOfType<OkObjectResult>();
        storage.Deleted.Should().Contain("my-key");
        context.LectureMaterials.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateComment_ShouldReturnBadRequest_WhenParentDoesNotBelongToMaterial()
    {

        var context = GetDbContext();
        var storage = new FakeR2StorageService();
        var controller = new StorageController(context, storage);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var materialId = Guid.NewGuid();
        var otherMaterialId = Guid.NewGuid();
        var foreignParentId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });
        context.LectureMaterials.Add(new LectureMaterial
        {
            Id = materialId, GroupId = groupId, UploaderId = userId,
            FileName = "lec.pdf", ContentType = "application/pdf", R2Key = "k"
        });


        context.MaterialComments.Add(new MaterialComment
        {
            Id = foreignParentId, MaterialId = otherMaterialId,
            AuthorId = userId, Content = "чуже"
        });

        await context.SaveChangesAsync();

        SetUser(controller, userId);

        var dto = new CreateCommentDto("відповідь", foreignParentId);


        var result = await controller.CreateComment(materialId, dto);


        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetComments_ShouldReturnThreadedTree()
    {

        var context = GetDbContext();
        var storage = new FakeR2StorageService();
        var controller = new StorageController(context, storage);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var materialId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });
        context.LectureMaterials.Add(new LectureMaterial
        {
            Id = materialId, GroupId = groupId, UploaderId = userId,
            FileName = "lec.pdf", ContentType = "application/pdf", R2Key = "k"
        });

        var root1 = new MaterialComment { Id = Guid.NewGuid(), MaterialId = materialId, AuthorId = userId, Content = "Корінь 1", CreatedAt = DateTime.UtcNow };
        var reply = new MaterialComment { Id = Guid.NewGuid(), MaterialId = materialId, AuthorId = userId, Content = "Відповідь", ParentCommentId = root1.Id, CreatedAt = DateTime.UtcNow.AddSeconds(1) };
        var root2 = new MaterialComment { Id = Guid.NewGuid(), MaterialId = materialId, AuthorId = userId, Content = "Корінь 2", CreatedAt = DateTime.UtcNow.AddSeconds(2) };

        context.MaterialComments.AddRange(root1, reply, root2);
        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var result = await controller.GetComments(materialId);


        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var tree = okResult.Value as List<MaterialCommentDto>;
        tree.Should().NotBeNull();
        tree!.Should().HaveCount(2);
        tree.First(c => c.Id == root1.Id).Replies.Should().HaveCount(1);
        tree.First(c => c.Id == root1.Id).Replies.First().Content.Should().Be("Відповідь");
        tree.First(c => c.Id == root2.Id).Replies.Should().BeEmpty();
    }
}
