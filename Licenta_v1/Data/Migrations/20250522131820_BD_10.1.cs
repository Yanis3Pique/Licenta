using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_101 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AggressiveEvents_AspNetUsers_DriverId",
                table: "AggressiveEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_AggressiveEvents_Vehicles_VehicleId",
                table: "AggressiveEvents");

            migrationBuilder.DropIndex(
                name: "IX_AggressiveEvents_DriverId",
                table: "AggressiveEvents");

            migrationBuilder.DropIndex(
                name: "IX_AggressiveEvents_VehicleId",
                table: "AggressiveEvents");

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "AggressiveEvents",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "DriverId",
                table: "AggressiveEvents",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "AggressiveEvents",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "DriverId",
                table: "AggressiveEvents",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_AggressiveEvents_DriverId",
                table: "AggressiveEvents",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_AggressiveEvents_VehicleId",
                table: "AggressiveEvents",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_AggressiveEvents_AspNetUsers_DriverId",
                table: "AggressiveEvents",
                column: "DriverId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AggressiveEvents_Vehicles_VehicleId",
                table: "AggressiveEvents",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
