using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class WatchedFolderFileSyncRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WatchedFolderCloudMetadata_WatchedFolders_WatchedFolderParentId",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropTable(
                name: "WatchedChild");

            migrationBuilder.DropIndex(
                name: "IX_WatchedFolderCloudMetadata_WatchedFolderParentId",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropColumn(
                name: "WatchedFolderParentId",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropColumn(
                name: "LargeFolderThresholdBytes",
                table: "SystemSettings");

            migrationBuilder.RenameColumn(
                name: "PathsBlacklist",
                table: "WatchedFolders",
                newName: "SnapshotUrls");

            migrationBuilder.RenameColumn(
                name: "FolderId",
                table: "WatchedFolderCloudMetadata",
                newName: "RemoteFolderId");

            migrationBuilder.RenameColumn(
                name: "PreferredProvider",
                table: "SystemSettings",
                newName: "GlobalPreferredProvider");

            migrationBuilder.RenameColumn(
                name: "BackupIgnoreProfile",
                table: "SystemSettings",
                newName: "GlobalIgnoreRules");

            migrationBuilder.RenameColumn(
                name: "Timestamp",
                table: "SyncEvent",
                newName: "SyncedItems");

            migrationBuilder.RenameColumn(
                name: "EventType",
                table: "SyncEvent",
                newName: "MetadataJson");

            migrationBuilder.AddColumn<string>(
                name: "IgnoreRules",
                table: "WatchedFolders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "KeepSnapshotsInSameAccount",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PreferredProvider",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Problem",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SyncFrequency",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TrackSnapshots",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "WatchedFolderId",
                table: "WatchedFolderCloudMetadata",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedTimestamp",
                table: "SyncEvent",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompletionType",
                table: "SyncEvent",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "WatchedFolderId",
                table: "SyncEvent",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "GlobalIgnoreRules",
                value: "*.tmp;node_modules/");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolderCloudMetadata_WatchedFolderId",
                table: "WatchedFolderCloudMetadata",
                column: "WatchedFolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_WatchedFolderCloudMetadata_WatchedFolders_WatchedFolderId",
                table: "WatchedFolderCloudMetadata",
                column: "WatchedFolderId",
                principalTable: "WatchedFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WatchedFolderCloudMetadata_WatchedFolders_WatchedFolderId",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropIndex(
                name: "IX_WatchedFolderCloudMetadata_WatchedFolderId",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropColumn(
                name: "IgnoreRules",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "KeepSnapshotsInSameAccount",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "PreferredProvider",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "Problem",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "SyncFrequency",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "TrackSnapshots",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "WatchedFolderId",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropColumn(
                name: "CompletedTimestamp",
                table: "SyncEvent");

            migrationBuilder.DropColumn(
                name: "CompletionType",
                table: "SyncEvent");

            migrationBuilder.DropColumn(
                name: "WatchedFolderId",
                table: "SyncEvent");

            migrationBuilder.RenameColumn(
                name: "SnapshotUrls",
                table: "WatchedFolders",
                newName: "PathsBlacklist");

            migrationBuilder.RenameColumn(
                name: "RemoteFolderId",
                table: "WatchedFolderCloudMetadata",
                newName: "FolderId");

            migrationBuilder.RenameColumn(
                name: "GlobalPreferredProvider",
                table: "SystemSettings",
                newName: "PreferredProvider");

            migrationBuilder.RenameColumn(
                name: "GlobalIgnoreRules",
                table: "SystemSettings",
                newName: "BackupIgnoreProfile");

            migrationBuilder.RenameColumn(
                name: "SyncedItems",
                table: "SyncEvent",
                newName: "Timestamp");

            migrationBuilder.RenameColumn(
                name: "MetadataJson",
                table: "SyncEvent",
                newName: "EventType");

            migrationBuilder.AddColumn<int>(
                name: "WatchedFolderParentId",
                table: "WatchedFolderCloudMetadata",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LargeFolderThresholdBytes",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "WatchedChild",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSync = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: true),
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

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BackupIgnoreProfile", "LargeFolderThresholdBytes" },
                values: new object[] { "[\"*.tmp\",\"node_modules/\"]", 1073741824L });

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolderCloudMetadata_WatchedFolderParentId",
                table: "WatchedFolderCloudMetadata",
                column: "WatchedFolderParentId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedChild_ParentId",
                table: "WatchedChild",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedChild_SyncEventId",
                table: "WatchedChild",
                column: "SyncEventId");

            migrationBuilder.AddForeignKey(
                name: "FK_WatchedFolderCloudMetadata_WatchedFolders_WatchedFolderParentId",
                table: "WatchedFolderCloudMetadata",
                column: "WatchedFolderParentId",
                principalTable: "WatchedFolders",
                principalColumn: "Id");
        }
    }
}
