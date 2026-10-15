using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class PersistUserCloudAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchedFolderUserCloudAssignments",
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
                    table.PrimaryKey("PK_WatchedFolderUserCloudAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchedFolderUserCloudAssignments_WatchedFolders_WatchedFolderId",
                        column: x => x.WatchedFolderId,
                        principalTable: "WatchedFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolderUserCloudAssignments_WatchedFolderId_Provider_AccountId",
                table: "WatchedFolderUserCloudAssignments",
                columns: new[] { "WatchedFolderId", "Provider", "AccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchedFolderUserCloudAssignments");
        }
    }
}
