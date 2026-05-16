using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkademVault_API.Migrations
{
    /// <inheritdoc />
    public partial class AddStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LectureMaterials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploaderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    R2Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LectureMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LectureMaterials_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LectureMaterials_Users_UploaderId",
                        column: x => x.UploaderId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialComments_LectureMaterials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "LectureMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialComments_MaterialComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "MaterialComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialComments_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LectureMaterials_GroupId_UploadedAt",
                table: "LectureMaterials",
                columns: new[] { "GroupId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LectureMaterials_UploaderId",
                table: "LectureMaterials",
                column: "UploaderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialComments_AuthorId",
                table: "MaterialComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialComments_MaterialId",
                table: "MaterialComments",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialComments_ParentCommentId",
                table: "MaterialComments",
                column: "ParentCommentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialComments");

            migrationBuilder.DropTable(
                name: "LectureMaterials");
        }
    }
}
