using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_752 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeavyVehicleRestrictedIds",
                table: "Orders");

            migrationBuilder.AddColumn<bool>(
                name: "HeavyVehicleRestricted",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeavyVehicleRestricted",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "HeavyVehicleRestrictedIds",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
