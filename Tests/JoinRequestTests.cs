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

// Tests for the JoinRequest approval flow (a member-to-group request awaiting Owner action).
public class JoinRequestTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    // Approve assigns the requester to the group and flips JoinRequest.Status to Approved.
    [Fact]
    public async Task ApproveRequest_ShouldChangeUserGroup_WhenOwnerApproves()
    {

        var context = GetDbContext();
        var controller = new RequestController(context);

        var ownerId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var groupId = Guid.NewGuid();


        var group = new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId };
        var student = new User { Id = studentId, Username = "student_test", Email = "s@test.com", PasswordHash = "hash" };
        context.Groups.Add(group);
        context.Users.Add(student);


        var joinRequest = new JoinRequest { Id = Guid.NewGuid(), GroupId = groupId, UserId = studentId, Status = RequestStatus.Pending };
        context.JoinRequests.Add(joinRequest);
        await context.SaveChangesAsync();


        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, ownerId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };


        var result = await controller.Approve(joinRequest.Id);


        result.Should().BeOfType<OkObjectResult>();

        var updatedStudent = await context.Users.FindAsync(studentId);
        updatedStudent!.GroupId.Should().Be(groupId);

        var updatedRequest = await context.JoinRequests.FindAsync(joinRequest.Id);
        updatedRequest!.Status.Should().Be(RequestStatus.Approved);
    }
}
