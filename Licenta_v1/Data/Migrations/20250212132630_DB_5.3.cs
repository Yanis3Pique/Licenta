using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class DB_53 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RouteHistories_DeliveryId",
                table: "RouteHistories");

            migrationBuilder.CreateIndex(
                name: "IX_RouteHistories_DeliveryId",
                table: "RouteHistories",
                column: "DeliveryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RouteHistories_DeliveryId",
                table: "RouteHistories");

            migrationBuilder.CreateIndex(
                name: "IX_RouteHistories_DeliveryId",
                table: "RouteHistories",
                column: "DeliveryId");
        }
    }
}
