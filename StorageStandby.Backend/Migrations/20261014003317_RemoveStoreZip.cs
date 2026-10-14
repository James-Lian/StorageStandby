using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStoreZip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoreZip",
                table: "WatchedFolderCloudMetadata");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StoreZip",
                table: "WatchedFolderCloudMetadata",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
