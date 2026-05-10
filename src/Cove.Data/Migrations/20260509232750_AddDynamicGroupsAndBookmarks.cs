using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicGroupsAndBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "AllowedHostTypes",
                table: "groups",
                type: "text[]",
                nullable: false,
                defaultValue: new[] { "scene" });

            migrationBuilder.AddColumn<int>(
                name: "CacheTtlSec",
                table: "groups",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "CachedItemCount",
                table: "groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "groups",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastResolvedAt",
                table: "groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueryJson",
                table: "groups",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuerySourceKey",
                table: "groups",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowInSceneLists",
                table: "groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<int>(
                name: "SceneId",
                table: "group_items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "ChildGroupId",
                table: "group_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HostId",
                table: "group_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HostType",
                table: "group_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "scene");

            migrationBuilder.AddColumn<int>(
                name: "ImageId",
                table: "group_items",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE group_items
                SET "HostId" = "SceneId"
                WHERE "SceneId" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "user_bookmarks",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_bookmarks", x => new { x.UserId, x.HostType, x.HostId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_group_items_ChildGroupId",
                table: "group_items",
                column: "ChildGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_group_items_HostType_HostId",
                table: "group_items",
                columns: new[] { "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_group_items_ImageId",
                table: "group_items",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_user_bookmarks_UserId_CreatedAt",
                table: "user_bookmarks",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_group_items_groups_ChildGroupId",
                table: "group_items",
                column: "ChildGroupId",
                principalTable: "groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_group_items_images_ImageId",
                table: "group_items",
                column: "ImageId",
                principalTable: "images",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_group_items_groups_ChildGroupId",
                table: "group_items");

            migrationBuilder.DropForeignKey(
                name: "FK_group_items_images_ImageId",
                table: "group_items");

            migrationBuilder.DropTable(
                name: "user_bookmarks");

            migrationBuilder.DropIndex(
                name: "IX_group_items_ChildGroupId",
                table: "group_items");

            migrationBuilder.DropIndex(
                name: "IX_group_items_HostType_HostId",
                table: "group_items");

            migrationBuilder.DropIndex(
                name: "IX_group_items_ImageId",
                table: "group_items");

            migrationBuilder.DropColumn(
                name: "AllowedHostTypes",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "CacheTtlSec",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "CachedItemCount",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "LastResolvedAt",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "QueryJson",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "QuerySourceKey",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "ShowInSceneLists",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "ChildGroupId",
                table: "group_items");

            migrationBuilder.DropColumn(
                name: "HostId",
                table: "group_items");

            migrationBuilder.DropColumn(
                name: "HostType",
                table: "group_items");

            migrationBuilder.DropColumn(
                name: "ImageId",
                table: "group_items");

            migrationBuilder.AlterColumn<int>(
                name: "SceneId",
                table: "group_items",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
