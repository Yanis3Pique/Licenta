using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
	/// <inheritdoc />
	public partial class BD_831 : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// Drop any foreign key that was created using the shadow column
			migrationBuilder.DropForeignKey(
				name: "FK_OrderVehicleRestrictions_Orders_OrderId1",
				table: "OrderVehicleRestrictions");

			// Drop the index on the shadow column
			migrationBuilder.DropIndex(
				name: "IX_OrderVehicleRestrictions_OrderId1",
				table: "OrderVehicleRestrictions");

			// Finally, drop the shadow column if it exists
			migrationBuilder.DropColumn(
				name: "OrderId1",
				table: "OrderVehicleRestrictions");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			// Re-add the column (nullable)
			migrationBuilder.AddColumn<int>(
				name: "OrderId1",
				table: "OrderVehicleRestrictions",
				type: "int",
				nullable: true);

			// Re-create the index on the column
			migrationBuilder.CreateIndex(
				name: "IX_OrderVehicleRestrictions_OrderId1",
				table: "OrderVehicleRestrictions",
				column: "OrderId1");

			// Re-create the foreign key linking the shadow column to the Orders table
			migrationBuilder.AddForeignKey(
				name: "FK_OrderVehicleRestrictions_Orders_OrderId1",
				table: "OrderVehicleRestrictions",
				column: "OrderId1",
				principalTable: "Orders",
				principalColumn: "Id");
		}
	}
}
