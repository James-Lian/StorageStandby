using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class TimelineEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimelineEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WatchedFolderId = table.Column<long>(type: "INTEGER", nullable: false),
                    SyncEventId = table.Column<long>(type: "INTEGER", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: true),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    PendingSyncItemId = table.Column<long>(type: "INTEGER", nullable: true),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelineEvents_SyncEvents_SyncEventId",
                        column: x => x.SyncEventId,
                        principalTable: "SyncEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TimelineEvents_WatchedFolders_WatchedFolderId",
                        column: x => x.WatchedFolderId,
                        principalTable: "WatchedFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEvents_SyncEventId",
                table: "TimelineEvents",
                column: "SyncEventId");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEvents_WatchedFolderId_OccurredAtUtc",
                table: "TimelineEvents",
                columns: new[] { "WatchedFolderId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimelineEvents");
        }
    }
}
