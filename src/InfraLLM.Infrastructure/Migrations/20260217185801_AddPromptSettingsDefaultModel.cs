using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraLLM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptSettingsDefaultModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"PromptSettings\" ADD COLUMN IF NOT EXISTS \"DefaultModel\" character varying(100);");
            migrationBuilder.Sql("ALTER TABLE \"Hosts\" DROP COLUMN IF EXISTS \"Prompt\";");
            migrationBuilder.Sql("ALTER TABLE \"Jobs\" ADD COLUMN IF NOT EXISTS \"Prompt\" text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"PromptSettings\" DROP COLUMN IF EXISTS \"DefaultModel\";");
        }
    }
}
