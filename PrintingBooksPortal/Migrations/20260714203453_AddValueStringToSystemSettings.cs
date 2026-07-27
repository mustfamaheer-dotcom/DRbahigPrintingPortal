using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintingBooksPortal.Migrations
{
    public partial class AddValueStringToSystemSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ValueString",
                table: "SystemSettings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValueString",
                table: "SystemSettings");
        }
    }
}
