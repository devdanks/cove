using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tag_applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    SourceRunId = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    ModelKey = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Confidence = table.Column<float>(type: "real", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tag_applications_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId_TagId_SourceKey_SourceRunI~",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId", "TagId", "SourceKey", "SourceRunId", "ModelKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_SourceKey",
                table: "tag_applications",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_TagId",
                table: "tag_applications",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tag_applications");
        }
    }
}
