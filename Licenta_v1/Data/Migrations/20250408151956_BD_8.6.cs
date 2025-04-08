using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_86 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriverId",
                table: "RouteHistories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "RouteHistories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderIdsJson",
                table: "RouteHistories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RouteData",
                table: "RouteHistories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VehicleDescription",
                table: "RouteHistories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "RouteHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "RouteHistories");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "RouteHistories");

            migrationBuilder.DropColumn(
                name: "OrderIdsJson",
                table: "RouteHistories");

            migrationBuilder.DropColumn(
                name: "RouteData",
                table: "RouteHistories");

            migrationBuilder.DropColumn(
                name: "VehicleDescription",
                table: "RouteHistories");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "RouteHistories");
        }
    }
}
