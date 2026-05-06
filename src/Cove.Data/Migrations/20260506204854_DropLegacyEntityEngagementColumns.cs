using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyEntityEngagementColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_studios_Rating",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_scenes_Rating",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_performers_Rating",
                table: "performers");

            migrationBuilder.DropIndex(
                name: "IX_images_Rating",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_galleries_Rating",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "LastPlayedAt",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "LikeCounter",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "PlayCount",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "PlayDuration",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "ResumeTime",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "performers");

            migrationBuilder.DropColumn(
                name: "LikeCounter",
                table: "images");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "images");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "galleries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "studios",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPlayedAt",
                table: "scenes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LikeCounter",
                table: "scenes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlayCount",
                table: "scenes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "PlayDuration",
                table: "scenes",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "scenes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ResumeTime",
                table: "scenes",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "performers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LikeCounter",
                table: "images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "images",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "galleries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_studios_Rating",
                table: "studios",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_Rating",
                table: "scenes",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_performers_Rating",
                table: "performers",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_images_Rating",
                table: "images",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_Rating",
                table: "galleries",
                column: "Rating");
        }
    }
}
