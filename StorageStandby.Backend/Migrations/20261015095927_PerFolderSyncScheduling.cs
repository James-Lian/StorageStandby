using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class PerFolderSyncScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncEvents_SystemSettings_SystemSettingsId",
                table: "SyncEvents");

            migrationBuilder.DropIndex(
                name: "IX_SyncEvents_SystemSettingsId",
                table: "SyncEvents");

            migrationBuilder.DropColumn(
                name: "AutomaticSyncIntervalMinutes",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "SystemSettingsId",
                table: "SyncEvents");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextSyncAt",
                table: "WatchedFolders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "SyncFrequency",
                table: "WatchedFolders",
                newName: "LegacySyncFrequency");

            migrationBuilder.AddColumn<string>(
                name: "SyncFrequency",
                table: "WatchedFolders",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"Multiplier\":1,\"Identifiers\":[],\"SyncIncrements\":2}");

            migrationBuilder.Sql(
                "UPDATE WatchedFolders "
                + "SET SyncFrequency = '{\"Multiplier\":1,\"Identifiers\":[],\"SyncIncrements\":' "
                + "|| LegacySyncFrequency || '}'");

            migrationBuilder.DropColumn(
                name: "LegacySyncFrequency",
                table: "WatchedFolders");

            migrationBuilder.AddColumn<int>(
                name: "SyncsToSkip",
                table: "WatchedFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledTimestamp",
                table: "SyncEvents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextSyncAt",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "SyncFrequency",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "SyncsToSkip",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "ScheduledTimestamp",
                table: "SyncEvents");

            migrationBuilder.AddColumn<int>(
                name: "AutomaticSyncIntervalMinutes",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SystemSettingsId",
                table: "SyncEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "AutomaticSyncIntervalMinutes",
                value: 60);

            migrationBuilder.CreateIndex(
                name: "IX_SyncEvents_SystemSettingsId",
                table: "SyncEvents",
                column: "SystemSettingsId");

            migrationBuilder.AddForeignKey(
                name: "FK_SyncEvents_SystemSettings_SystemSettingsId",
                table: "SyncEvents",
                column: "SystemSettingsId",
                principalTable: "SystemSettings",
                principalColumn: "Id");
        }
    }
}
