using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneImageFileMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FileCount",
                table: "scenes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "MaxBitRate",
                table: "scenes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "MaxDuration",
                table: "scenes",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MaxFileModTime",
                table: "scenes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxFileSize",
                table: "scenes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "MaxFrameRate",
                table: "scenes",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "MaxHeight",
                table: "scenes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MaxPath",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxResolution",
                table: "scenes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MinPath",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FileCount",
                table: "images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MaxFileModTime",
                table: "images",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxFileSize",
                table: "images",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "MaxPath",
                table: "images",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxResolution",
                table: "images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MinPath",
                table: "images",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE scenes
                SET ""FileCount"" = summary.file_count,
                    ""MaxBitRate"" = summary.max_bit_rate,
                    ""MaxDuration"" = summary.max_duration,
                    ""MaxFileModTime"" = summary.max_file_mod_time,
                    ""MaxFileSize"" = summary.max_file_size,
                    ""MaxFrameRate"" = summary.max_frame_rate,
                    ""MaxHeight"" = summary.max_height,
                    ""MaxPath"" = summary.max_path,
                    ""MaxResolution"" = summary.max_resolution,
                    ""MinPath"" = summary.min_path
                FROM (
                    SELECT ""SceneId"" AS sid,
                           COUNT(*) AS file_count,
                              COALESCE(MAX(""BitRate""), 0) AS max_bit_rate,
                              COALESCE(MAX(""Duration""), 0) AS max_duration,
                           MAX(""ModTime"") AS max_file_mod_time,
                              COALESCE(MAX(""Size""), 0) AS max_file_size,
                              COALESCE(MAX(""FrameRate""), 0) AS max_frame_rate,
                              COALESCE(MAX(""Height""), 0) AS max_height,
                              COALESCE(MAX(GREATEST(""Width"", ""Height"")), 0) AS max_resolution,
                           MIN(""Path"") AS min_path,
                           MAX(""Path"") AS max_path
                    FROM files
                    WHERE ""FileType"" = 'Video' AND ""SceneId"" IS NOT NULL
                    GROUP BY ""SceneId""
                ) summary
                WHERE scenes.""Id"" = summary.sid;
            ");

            migrationBuilder.Sql(@"
                UPDATE images
                SET ""FileCount"" = summary.file_count,
                    ""MaxFileModTime"" = summary.max_file_mod_time,
                    ""MaxFileSize"" = summary.max_file_size,
                    ""MaxPath"" = summary.max_path,
                    ""MaxResolution"" = summary.max_resolution,
                    ""MinPath"" = summary.min_path
                FROM (
                    SELECT ""ImageId"" AS iid,
                           COUNT(*) AS file_count,
                           MAX(""ModTime"") AS max_file_mod_time,
                              COALESCE(MAX(""Size""), 0) AS max_file_size,
                              COALESCE(MAX(GREATEST(""Width"", ""Height"")), 0) AS max_resolution,
                           MIN(""Path"") AS min_path,
                           MAX(""Path"") AS max_path
                    FROM files
                    WHERE ""FileType"" = 'Image' AND ""ImageId"" IS NOT NULL
                    GROUP BY ""ImageId""
                ) summary
                WHERE images.""Id"" = summary.iid;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_FileCount",
                table: "scenes",
                column: "FileCount");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxBitRate",
                table: "scenes",
                column: "MaxBitRate");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxDuration",
                table: "scenes",
                column: "MaxDuration");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxFileModTime",
                table: "scenes",
                column: "MaxFileModTime");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxFileSize",
                table: "scenes",
                column: "MaxFileSize");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxFrameRate",
                table: "scenes",
                column: "MaxFrameRate");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxHeight",
                table: "scenes",
                column: "MaxHeight");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxPath",
                table: "scenes",
                column: "MaxPath");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxResolution",
                table: "scenes",
                column: "MaxResolution");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MinPath",
                table: "scenes",
                column: "MinPath");

            migrationBuilder.CreateIndex(
                name: "IX_images_FileCount",
                table: "images",
                column: "FileCount");

            migrationBuilder.CreateIndex(
                name: "IX_images_MaxFileModTime",
                table: "images",
                column: "MaxFileModTime");

            migrationBuilder.CreateIndex(
                name: "IX_images_MaxFileSize",
                table: "images",
                column: "MaxFileSize");

            migrationBuilder.CreateIndex(
                name: "IX_images_MaxPath",
                table: "images",
                column: "MaxPath");

            migrationBuilder.CreateIndex(
                name: "IX_images_MaxResolution",
                table: "images",
                column: "MaxResolution");

            migrationBuilder.CreateIndex(
                name: "IX_images_MinPath",
                table: "images",
                column: "MinPath");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scenes_FileCount",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MaxBitRate",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MaxDuration",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MaxFileModTime",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MaxFileSize",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MaxFrameRate",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MaxHeight",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MaxPath",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MaxResolution",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_MinPath",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_images_FileCount",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_MaxFileModTime",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_MaxFileSize",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_MaxPath",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_MaxResolution",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_MinPath",
                table: "images");

            migrationBuilder.DropColumn(
                name: "FileCount",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MaxBitRate",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MaxDuration",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MaxFileModTime",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MaxFileSize",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MaxFrameRate",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MaxHeight",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MaxPath",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MaxResolution",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MinPath",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "FileCount",
                table: "images");

            migrationBuilder.DropColumn(
                name: "MaxFileModTime",
                table: "images");

            migrationBuilder.DropColumn(
                name: "MaxFileSize",
                table: "images");

            migrationBuilder.DropColumn(
                name: "MaxPath",
                table: "images");

            migrationBuilder.DropColumn(
                name: "MaxResolution",
                table: "images");

            migrationBuilder.DropColumn(
                name: "MinPath",
                table: "images");
        }
    }
}
