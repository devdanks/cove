using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagSegmentOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SegmentColorOverride",
                table: "tags",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SegmentLaneOverride",
                table: "tags",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowAsSegment",
                table: "tags",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SegmentColorOverride",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "SegmentLaneOverride",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "ShowAsSegment",
                table: "tags");
        }
    }
}