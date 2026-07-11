using InfraLLM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraLLM.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260710000000_AddAccessTokenScopes")]
    public partial class AddAccessTokenScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Null = unrestricted, preserving the behavior of tokens created
            // before scopes existed.
            migrationBuilder.AddColumn<string>(
                name: "Scopes",
                table: "AccessTokens",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scopes",
                table: "AccessTokens");
        }
    }
}
