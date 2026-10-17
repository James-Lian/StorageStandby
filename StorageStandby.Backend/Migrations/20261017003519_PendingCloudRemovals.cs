using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class PendingCloudRemovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchedFolderUserCloudRemovalAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WatchedFolderId = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedFolderUserCloudRemovalAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchedFolderUserCloudRemovalAssignments_WatchedFolders_WatchedFolderId",
                        column: x => x.WatchedFolderId,
                        principalTable: "WatchedFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolderUserCloudRemovalAssignments_WatchedFolderId_Provider_AccountId",
                table: "WatchedFolderUserCloudRemovalAssignments",
                columns: new[] { "WatchedFolderId", "Provider", "AccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchedFolderUserCloudRemovalAssignments");
        }
    }
}
