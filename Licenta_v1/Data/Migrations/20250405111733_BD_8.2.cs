using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_82 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InaccessibleHeavyVehicleIds",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ManuallyRestrictedVehicleIds",
                table: "Orders");

            migrationBuilder.AddColumn<int>(
                name: "OrderId1",
                table: "OrderVehicleRestrictions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderVehicleRestrictions_OrderId1",
                table: "OrderVehicleRestrictions",
                column: "OrderId1");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderVehicleRestrictions_Orders_OrderId1",
                table: "OrderVehicleRestrictions",
                column: "OrderId1",
                principalTable: "Orders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderVehicleRestrictions_Orders_OrderId1",
                table: "OrderVehicleRestrictions");

            migrationBuilder.DropIndex(
                name: "IX_OrderVehicleRestrictions_OrderId1",
                table: "OrderVehicleRestrictions");

            migrationBuilder.DropColumn(
                name: "OrderId1",
                table: "OrderVehicleRestrictions");

            migrationBuilder.AddColumn<string>(
                name: "InaccessibleHeavyVehicleIds",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ManuallyRestrictedVehicleIds",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
