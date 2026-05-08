using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class Wave1TagGroupsAndApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tag_applications_HostType_HostId_TagId_SourceKey_SourceRunI~",
                table: "tag_applications");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "tags",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MinOccurrencePercent",
                table: "tags",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MinOccurrenceSec",
                table: "tags",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TagGroupId",
                table: "tags",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContextId",
                table: "tag_applications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextType",
                table: "tag_applications",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HostDurationSec",
                table: "tag_applications",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TotalDurationSec",
                table: "tag_applications",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tag_groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_groups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tags_TagGroupId",
                table: "tags",
                column: "TagGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId_ContextType_ContextId",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId", "ContextType", "ContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId_ContextType_ContextId_TagI~",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId", "ContextType", "ContextId", "TagId", "SourceKey", "SourceRunId", "ModelKey" },
                unique: true,
                filter: "\"ContextType\" IS NOT NULL AND \"ContextId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId_TagId_SourceKey_SourceRunI~",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId", "TagId", "SourceKey", "SourceRunId", "ModelKey" },
                unique: true,
                filter: "\"ContextType\" IS NULL AND \"ContextId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tag_groups_Name",
                table: "tag_groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tag_groups_SortOrder",
                table: "tag_groups",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_tags_tag_groups_TagGroupId",
                table: "tags",
                column: "TagGroupId",
                principalTable: "tag_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tags_tag_groups_TagGroupId",
                table: "tags");

            migrationBuilder.DropTable(
                name: "tag_groups");

            migrationBuilder.DropIndex(
                name: "IX_tags_TagGroupId",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tag_applications_HostType_HostId_ContextType_ContextId",
                table: "tag_applications");

            migrationBuilder.DropIndex(
                name: "IX_tag_applications_HostType_HostId_ContextType_ContextId_TagI~",
                table: "tag_applications");

            migrationBuilder.DropIndex(
                name: "IX_tag_applications_HostType_HostId_TagId_SourceKey_SourceRunI~",
                table: "tag_applications");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "MinOccurrencePercent",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "MinOccurrenceSec",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "TagGroupId",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "ContextId",
                table: "tag_applications");

            migrationBuilder.DropColumn(
                name: "ContextType",
                table: "tag_applications");

            migrationBuilder.DropColumn(
                name: "HostDurationSec",
                table: "tag_applications");

            migrationBuilder.DropColumn(
                name: "TotalDurationSec",
                table: "tag_applications");

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId_TagId_SourceKey_SourceRunI~",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId", "TagId", "SourceKey", "SourceRunId", "ModelKey" },
                unique: true);
        }
    }
}
