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

public class PlannerTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateAssignment_ShouldReturnForbid_WhenUserIsNotOwner()
    {
        
        var context = GetDbContext();
        var controller = new PlannerController(context);
        
        var userId = Guid.NewGuid();
        
        
        var group = new Group { Id = Guid.NewGuid(), Name = "КН-31", OwnerId = Guid.NewGuid() };
        context.Groups.Add(group);
        await context.SaveChangesAsync();

        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext 
        { 
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) } 
        };

        
        var dto = new AssignmentDto("Лаба 1", "Опис", DateTime.UtcNow.AddDays(7));

       
        var result = await controller.CreateAssignment(dto);

      
        result.Should().BeOfType<ForbidResult>(); 
    }

    [Fact]
    public async Task DeleteAssignment_ShouldReturnOk_WhenUserIsOwner()
    {
       
        var context = GetDbContext();
        var controller = new PlannerController(context);
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var group = new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId };
        var assignment = new Assignment { Id = Guid.NewGuid(), Title = "Тест", GroupId = groupId, Group = group };
        
        context.Groups.Add(group);
        context.Assignments.Add(assignment);
        await context.SaveChangesAsync();

        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, ownerId.ToString()) };
        controller.ControllerContext = new ControllerContext 
        { 
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) } 
        };

        
        var result = await controller.DeleteAssignment(assignment.Id);

       
        result.Should().BeOfType<OkObjectResult>();
        context.Assignments.Should().BeEmpty(); 
    }

    [Fact]
    public async Task GetAssignments_ShouldReturnOnlyGroupAssignments()
    {
       
        var context = GetDbContext();
        var controller = new PlannerController(context);
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        
        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });
        
        
        context.Assignments.Add(new Assignment { Id = Guid.NewGuid(), Title = "Наша Лаба", GroupId = groupId });
        
 
        context.Assignments.Add(new Assignment { Id = Guid.NewGuid(), Title = "Чужа Лаба", GroupId = Guid.NewGuid() });
        
        await context.SaveChangesAsync();

        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext 
        { 
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) } 
        };

      
        var result = await controller.GetAssignments();

       
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var assignments = okResult.Value as List<Assignment>;
        assignments.Should().NotBeNull();
        assignments.Should().HaveCount(1);
        assignments!.First().Title.Should().Be("Наша Лаба");
    }

    [Fact]
public async Task GetWeeklyAssignments_ShouldOnlyReturnTasksForCurrentWeek()
{
    // Arrange
    var context = GetDbContext();
    var controller = new PlannerController(context);
    var groupId = Guid.NewGuid();
    var userId = Guid.NewGuid();


    DateTime now = DateTime.UtcNow;
    int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
    DateTime startOfWeek = now.AddDays(-1 * diff).Date;

    context.Users.Add(new User { Id = userId, GroupId = groupId });

    
    context.Assignments.Add(new Assignment { 
        Id = Guid.NewGuid(), Title = "Поточна лаба", 
        GroupId = groupId, DueDate = startOfWeek.AddDays(1) 
    });

    
    context.Assignments.Add(new Assignment { 
        Id = Guid.NewGuid(), Title = "Стара лаба", 
        GroupId = groupId, DueDate = startOfWeek.AddDays(-2) 
    });

   
    context.Assignments.Add(new Assignment { 
        Id = Guid.NewGuid(), Title = "Майбутня лаба", 
        GroupId = groupId, DueDate = startOfWeek.AddDays(8) 
    });

    await context.SaveChangesAsync();

    var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
    controller.ControllerContext = new ControllerContext { 
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) } 
    };


    var result = await controller.GetWeeklyAssignments(); 

  
    var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
    var assignments = okResult.Value as List<Assignment>;
    
    assignments.Should().HaveCount(1);
    assignments!.First().Title.Should().Be("Поточна лаба");
}
}