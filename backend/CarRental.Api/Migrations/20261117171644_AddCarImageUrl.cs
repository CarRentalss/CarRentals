using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarRental.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCarImageUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Cars",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 1,
                column: "ImageUrl",
                value: "toyota-corolla-2022.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 2,
                column: "ImageUrl",
                value: "toyota-yaris-2023.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 3,
                column: "ImageUrl",
                value: "skoda-octavia-2021.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 4,
                column: "ImageUrl",
                value: "volkswagen-golf-2022.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 5,
                column: "ImageUrl",
                value: "kia-sportage-2023.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 6,
                column: "ImageUrl",
                value: "hyundai-i30-2020.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 7,
                column: "ImageUrl",
                value: "ford-focus-2021.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 8,
                column: "ImageUrl",
                value: "renault-clio-2022.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 9,
                column: "ImageUrl",
                value: "bmw-seria-3-2023.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 10,
                column: "ImageUrl",
                value: "audi-a4-2022.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 11,
                column: "ImageUrl",
                value: "toyota-rav4-2024.jpg");

            migrationBuilder.UpdateData(
                table: "Cars",
                keyColumn: "Id",
                keyValue: 12,
                column: "ImageUrl",
                value: "opel-corsa-2021.jpg");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Cars");
        }
    }
}
