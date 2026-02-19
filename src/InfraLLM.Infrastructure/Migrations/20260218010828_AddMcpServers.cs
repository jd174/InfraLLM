using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraLLM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpServers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpServers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TransportType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    Command = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Arguments = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    WorkingDirectory = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    EnvironmentVariables = table.Column<string>(type: "jsonb", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpServers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpServers_OrganizationId",
                table: "McpServers",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_McpServers_OrganizationId_IsEnabled",
                table: "McpServers",
                columns: new[] { "OrganizationId", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpServers");
        }
    }
}
