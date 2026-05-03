using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Controllers;
using AkademVault_API.Data;
using AkademVault_API.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Tests;

public class GroupControllerTests
{
    
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateGroup_ShouldSuccess_AndCalculateExpiryDateCorrectlty()
    {
       
        var context = GetDbContext();
        var controller = new GroupController(context);
        
        
        var userId = Guid.NewGuid();
        var user = new User { 
            Id = userId, 
            Username = "yanosh_dev", 
            Email = "test@student.ua", 
            PasswordHash = "fake_hash" 
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

       
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var request = new CreateGroupRequest { Name = "КН-31", YearsOfStudy = 4 };

       
        var result = await controller.CreateGroup(request);

        
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        
       
        var groupInDb = await context.Groups.FirstOrDefaultAsync(g => g.Name == "КН-31");
        groupInDb.Should().NotBeNull();
        groupInDb!.OwnerId.Should().Be(userId);
        
    
        var expectedYear = DateTime.UtcNow.AddYears(4).Year;
        groupInDb.ExpiryDate.Year.Should().Be(expectedYear);

    
        var updatedUser = await context.Users.FindAsync(userId);
        updatedUser!.GroupId.Should().Be(groupInDb.Id);
    }
}