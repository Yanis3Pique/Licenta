using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_104 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AggressiveEvents_Deliveries_DeliveryId",
                table: "AggressiveEvents");

            migrationBuilder.DropIndex(
                name: "IX_AggressiveEvents_DeliveryId",
                table: "AggressiveEvents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AggressiveEvents_DeliveryId",
                table: "AggressiveEvents",
                column: "DeliveryId");

            migrationBuilder.AddForeignKey(
                name: "FK_AggressiveEvents_Deliveries_DeliveryId",
                table: "AggressiveEvents",
                column: "DeliveryId",
                principalTable: "Deliveries",
                principalColumn: "Id");
        }
    }
}
