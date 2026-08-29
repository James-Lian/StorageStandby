using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class WatchedFolderSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Path",
                table: "WatchedFolders",
                newName: "PathsBlacklist");

            migrationBuilder.RenameColumn(
                name: "AssignedCloud",
                table: "WatchedFolders",
                newName: "LocalPath");

            migrationBuilder.AddColumn<bool>(
                name: "IsZipCompressionEnabled",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSync",
                table: "WatchedFolders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PreferredProvider = table.Column<int>(type: "INTEGER", nullable: true),
                    LargeFolderThresholdBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupIgnoreProfile = table.Column<string>(type: "TEXT", nullable: false),
                    PauseSyncUntil = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchedFolderCloudMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    FolderId = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigFileId = table.Column<string>(type: "TEXT", nullable: false),
                    WatchedFolderParentId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedFolderCloudMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchedFolderCloudMetadata_WatchedFolders_WatchedFolderParentId",
                        column: x => x.WatchedFolderParentId,
                        principalTable: "WatchedFolders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SyncEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    SystemSettingsId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncEvent_SystemSettings_SystemSettingsId",
                        column: x => x.SystemSettingsId,
                        principalTable: "SystemSettings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WatchedChild",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSync = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncEventId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedChild", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchedChild_SyncEvent_SyncEventId",
                        column: x => x.SyncEventId,
                        principalTable: "SyncEvent",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WatchedChild_WatchedFolders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "WatchedFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "BackupIgnoreProfile", "LargeFolderThresholdBytes", "PauseSyncUntil", "PreferredProvider" },
                values: new object[] { 1, "[\"*.tmp\",\"node_modules/\"]", 1073741824L, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_SyncEvent_SystemSettingsId",
                table: "SyncEvent",
                column: "SystemSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedChild_ParentId",
                table: "WatchedChild",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedChild_SyncEventId",
                table: "WatchedChild",
                column: "SyncEventId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolderCloudMetadata_WatchedFolderParentId",
                table: "WatchedFolderCloudMetadata",
                column: "WatchedFolderParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchedChild");

            migrationBuilder.DropTable(
                name: "WatchedFolderCloudMetadata");

            migrationBuilder.DropTable(
                name: "SyncEvent");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "IsZipCompressionEnabled",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "LastSync",
                table: "WatchedFolders");

            migrationBuilder.RenameColumn(
                name: "PathsBlacklist",
                table: "WatchedFolders",
                newName: "Path");

            migrationBuilder.RenameColumn(
                name: "LocalPath",
                table: "WatchedFolders",
                newName: "AssignedCloud");
        }
    }
}
