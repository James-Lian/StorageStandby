using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class SyncArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncEvent_SystemSettings_SystemSettingsId",
                table: "SyncEvent");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SyncEvent",
                table: "SyncEvent");

            migrationBuilder.DropColumn(
                name: "IsZipCompressionEnabled",
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
                name: "SyncFrequency",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "GlobalPreferredProvider",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "SyncEvent");

            migrationBuilder.RenameTable(
                name: "SyncEvent",
                newName: "SyncEvents");

            migrationBuilder.RenameColumn(
                name: "ProviderName",
                table: "CloudTokens",
                newName: "Provider");

            migrationBuilder.RenameColumn(
                name: "MetadataJson",
                table: "SyncEvents",
                newName: "UnfinishedItems");

            migrationBuilder.RenameIndex(
                name: "IX_SyncEvent_SystemSettingsId",
                table: "SyncEvents",
                newName: "IX_SyncEvents_SystemSettingsId");

            migrationBuilder.AlterColumn<int>(
                name: "Problem",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ConfigFileId",
                table: "WatchedFolderCloudMetadata",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "WatchedFolderCloudMetadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "WatchedFolderCloudMetadata",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "StoreZip",
                table: "WatchedFolderCloudMetadata",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticSyncs",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSyncPaused",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SyncedItemIds",
                table: "SyncEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UnfinishedItemIds",
                table: "SyncEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SyncEvents",
                table: "SyncEvents",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "FailedItemsDetails",
                columns: table => new
                {
                    SyncEventId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    FailedItem = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedItemsDetails", x => new { x.SyncEventId, x.Id });
                    table.ForeignKey(
                        name: "FK_FailedItemsDetails_SyncEvents_SyncEventId",
                        column: x => x.SyncEventId,
                        principalTable: "SyncEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PendingSyncQueue",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WatchedFolderId = table.Column<long>(type: "INTEGER", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: false),
                    IsFolder = table.Column<bool>(type: "INTEGER", nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Moved = table.Column<bool>(type: "INTEGER", nullable: false),
                    OriginalLocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    Renamed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Changed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Created = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingSyncQueue", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AutomaticSyncs", "GlobalIgnoreRules", "IsSyncPaused" },
                values: new object[] { true, "*C:/", false });

            migrationBuilder.AddForeignKey(
                name: "FK_SyncEvents_SystemSettings_SystemSettingsId",
                table: "SyncEvents",
                column: "SystemSettingsId",
                principalTable: "SystemSettings",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncEvents_SystemSettings_SystemSettingsId",
                table: "SyncEvents");

            migrationBuilder.DropTable(
                name: "FailedItemsDetails");

            migrationBuilder.DropTable(
                name: "PendingSyncQueue");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SyncEvents",
                table: "SyncEvents");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropColumn(
                name: "State",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropColumn(
                name: "StoreZip",
                table: "WatchedFolderCloudMetadata");

            migrationBuilder.DropColumn(
                name: "AutomaticSyncs",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "IsSyncPaused",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "SyncedItemIds",
                table: "SyncEvents");

            migrationBuilder.DropColumn(
                name: "UnfinishedItemIds",
                table: "SyncEvents");

            migrationBuilder.RenameTable(
                name: "SyncEvents",
                newName: "SyncEvent");

            migrationBuilder.RenameColumn(
                name: "Provider",
                table: "CloudTokens",
                newName: "ProviderName");

            migrationBuilder.RenameColumn(
                name: "UnfinishedItems",
                table: "SyncEvent",
                newName: "MetadataJson");

            migrationBuilder.RenameIndex(
                name: "IX_SyncEvents_SystemSettingsId",
                table: "SyncEvent",
                newName: "IX_SyncEvent_SystemSettingsId");

            migrationBuilder.AlterColumn<int>(
                name: "Problem",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<bool>(
                name: "IsZipCompressionEnabled",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

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
                name: "SyncFrequency",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "ConfigFileId",
                table: "WatchedFolderCloudMetadata",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GlobalPreferredProvider",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "SyncEvent",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SyncEvent",
                table: "SyncEvent",
                column: "Id");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "GlobalIgnoreRules", "GlobalPreferredProvider" },
                values: new object[] { "*.tmp;node_modules/", null });

            migrationBuilder.AddForeignKey(
                name: "FK_SyncEvent_SystemSettings_SystemSettingsId",
                table: "SyncEvent",
                column: "SystemSettingsId",
                principalTable: "SystemSettings",
                principalColumn: "Id");
        }
    }
}
