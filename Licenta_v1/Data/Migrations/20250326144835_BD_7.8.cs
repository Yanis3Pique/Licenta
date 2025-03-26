using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_78 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeavyVehicleRestricted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsHeavyVehicleRestricted",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "InaccessibleHeavyVehicleIds",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ManuallyRestrictedVehicleIds",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InaccessibleHeavyVehicleIds",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ManuallyRestrictedVehicleIds",
                table: "Orders");

            migrationBuilder.AddColumn<bool>(
                name: "HeavyVehicleRestricted",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHeavyVehicleRestricted",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
