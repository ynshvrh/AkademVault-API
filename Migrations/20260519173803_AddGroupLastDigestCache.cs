using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkademVault_API.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupLastDigestCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastDigestAssignmentCount",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDigestGeneratedAt",
                table: "Groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastDigestMaterialCount",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastDigestMessageCount",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDigestSummary",
                table: "Groups",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastDigestAssignmentCount",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "LastDigestGeneratedAt",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "LastDigestMaterialCount",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "LastDigestMessageCount",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "LastDigestSummary",
                table: "Groups");
        }
    }
}
