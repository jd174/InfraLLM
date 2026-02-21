using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraLLM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePolicyRequireApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireApproval",
                table: "Policies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireApproval",
                table: "Policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
