using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkademVault_API.Migrations
{
    /// <inheritdoc />
    public partial class UltimateCleanFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Users_OwnerId1",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_OwnerId1",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "OwnerId1",
                table: "Groups");

            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assignments_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JoinRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JoinRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JoinRequests_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JoinRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OwnerId",
                table: "Groups",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_GroupId",
                table: "Assignments",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_JoinRequests_GroupId",
                table: "JoinRequests",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_JoinRequests_UserId",
                table: "JoinRequests",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Users_OwnerId",
                table: "Groups",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Users_OwnerId",
                table: "Groups");

            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "JoinRequests");

            migrationBuilder.DropIndex(
                name: "IX_Groups_OwnerId",
                table: "Groups");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId1",
                table: "Groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OwnerId1",
                table: "Groups",
                column: "OwnerId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Users_OwnerId1",
                table: "Groups",
                column: "OwnerId1",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
