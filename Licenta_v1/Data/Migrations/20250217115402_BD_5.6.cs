using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_56 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliverySequence",
                table: "Orders",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliverySequence",
                table: "Orders");
        }
    }
}
