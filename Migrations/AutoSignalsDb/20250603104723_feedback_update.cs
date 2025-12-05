using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class feedback_update : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScreenshotData",
                table: "UserFeedback");

            migrationBuilder.CreateTable(
                name: "UserFeedbackImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserFeedbackId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFeedbackImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFeedbackImages_UserFeedback_UserFeedbackId",
                        column: x => x.UserFeedbackId,
                        principalTable: "UserFeedback",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedbackImages_UserFeedbackId",
                table: "UserFeedbackImages",
                column: "UserFeedbackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserFeedbackImages");

            migrationBuilder.AddColumn<byte[]>(
                name: "ScreenshotData",
                table: "UserFeedback",
                type: "varbinary(max)",
                nullable: true);
        }
    }
}
