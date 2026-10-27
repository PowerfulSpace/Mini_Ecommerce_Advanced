using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PS.MiniEcommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCartItemIsSelected : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSelected",
                table: "CartItems",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSelected",
                table: "CartItems");
        }
    }
}
