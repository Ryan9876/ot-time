using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OtTime.Infrastructure.Migrations;

[DbContext(typeof(OtTimeDbContext))]
[Migration("20250101000000_InitialSchema")]
public partial class InitialSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AspNetRoles",
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 450, nullable: false),
                Name = table.Column<string>(maxLength: 256, nullable: true),
                NormalizedName = table.Column<string>(maxLength: 256, nullable: true),
                ConcurrencyStamp = table.Column<string>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AspNetRoles", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AspNetUsers",
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 450, nullable: false),
                UserName = table.Column<string>(maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(maxLength: 256, nullable: true),
                Email = table.Column<string>(maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(nullable: false),
                PasswordHash = table.Column<string>(nullable: true),
                SecurityStamp = table.Column<string>(nullable: true),
                ConcurrencyStamp = table.Column<string>(nullable: true),
                PhoneNumber = table.Column<string>(nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(nullable: false),
                TwoFactorEnabled = table.Column<bool>(nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(nullable: true),
                LockoutEnabled = table.Column<bool>(nullable: false),
                AccessFailedCount = table.Column<int>(nullable: false),
                DisplayName = table.Column<string>(maxLength: 200, nullable: true),
                IsEnabled = table.Column<bool>(nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AspNetUsers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ActivityCategories",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 120, nullable: false),
                Description = table.Column<string>(maxLength: 500, nullable: true),
                DisplayOrder = table.Column<int>(nullable: false),
                IsEnabled = table.Column<bool>(nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                ModifiedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActivityCategories", x => x.Id);
                table.ForeignKey("FK_ActivityCategories_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ActivityCategories_AspNetUsers_ModifiedByUserId", x => x.ModifiedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ReportingDimensions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 120, nullable: false),
                Key = table.Column<string>(maxLength: 80, nullable: false),
                DisplayOrder = table.Column<int>(nullable: false),
                IsRequired = table.Column<bool>(nullable: false),
                IsEnabled = table.Column<bool>(nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                ModifiedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportingDimensions", x => x.Id);
                table.ForeignKey("FK_ReportingDimensions_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ReportingDimensions_AspNetUsers_ModifiedByUserId", x => x.ModifiedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ReportDefinitions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 160, nullable: false),
                Description = table.Column<string>(maxLength: 500, nullable: true),
                FiltersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                IsShared = table.Column<bool>(nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                ModifiedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportDefinitions", x => x.Id);
                table.ForeignKey("FK_ReportDefinitions_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ReportDefinitions_AspNetUsers_ModifiedByUserId", x => x.ModifiedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Tags",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 100, nullable: false),
                DisplayOrder = table.Column<int>(nullable: false),
                IsEnabled = table.Column<bool>(nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                ModifiedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => x.Id);
                table.ForeignKey("FK_Tags_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Tags_AspNetUsers_ModifiedByUserId", x => x.ModifiedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TicketSources",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 120, nullable: false),
                Key = table.Column<string>(maxLength: 80, nullable: false),
                Description = table.Column<string>(maxLength: 500, nullable: true),
                DisplayOrder = table.Column<int>(nullable: false),
                IsEnabled = table.Column<bool>(nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                ModifiedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TicketSources", x => x.Id);
                table.ForeignKey("FK_TicketSources_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TicketSources_AspNetUsers_ModifiedByUserId", x => x.ModifiedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AspNetRoleClaims",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                RoleId = table.Column<string>(maxLength: 450, nullable: false),
                ClaimType = table.Column<string>(nullable: true),
                ClaimValue = table.Column<string>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                table.ForeignKey("FK_AspNetRoleClaims_AspNetRoles_RoleId", x => x.RoleId, "AspNetRoles", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserClaims",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                ClaimType = table.Column<string>(nullable: true),
                ClaimValue = table.Column<string>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                table.ForeignKey("FK_AspNetUserClaims_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserLogins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(maxLength: 128, nullable: false),
                ProviderKey = table.Column<string>(maxLength: 128, nullable: false),
                ProviderDisplayName = table.Column<string>(nullable: true),
                UserId = table.Column<string>(maxLength: 450, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey("FK_AspNetUserLogins_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserRoles",
            columns: table => new
            {
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                RoleId = table.Column<string>(maxLength: 450, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey("FK_AspNetUserRoles_AspNetRoles_RoleId", x => x.RoleId, "AspNetRoles", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_AspNetUserRoles_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserTokens",
            columns: table => new
            {
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                LoginProvider = table.Column<string>(maxLength: 128, nullable: false),
                Name = table.Column<string>(maxLength: 128, nullable: false),
                Value = table.Column<string>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey("FK_AspNetUserTokens_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ReportingDimensionValues",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ReportingDimensionId = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 120, nullable: false),
                DisplayOrder = table.Column<int>(nullable: false),
                IsEnabled = table.Column<bool>(nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                ModifiedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportingDimensionValues", x => x.Id);
                table.ForeignKey("FK_ReportingDimensionValues_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ReportingDimensionValues_AspNetUsers_ModifiedByUserId", x => x.ModifiedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ReportingDimensionValues_ReportingDimensions_ReportingDimensionId", x => x.ReportingDimensionId, "ReportingDimensions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ReportSchedules",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ReportDefinitionId = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 160, nullable: false),
                TimeZoneId = table.Column<string>(maxLength: 100, nullable: false),
                Recurrence = table.Column<string>(maxLength: 200, nullable: false),
                DestinationType = table.Column<string>(maxLength: 40, nullable: false),
                DestinationConfiguration = table.Column<string>(type: "nvarchar(max)", nullable: false),
                IsEnabled = table.Column<bool>(nullable: false),
                NextRunAtUtc = table.Column<DateTimeOffset>(nullable: true),
                LeaseId = table.Column<Guid>(nullable: true),
                LeaseExpiresAtUtc = table.Column<DateTimeOffset>(nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                ModifiedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportSchedules", x => x.Id);
                table.ForeignKey("FK_ReportSchedules_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ReportSchedules_AspNetUsers_ModifiedByUserId", x => x.ModifiedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ReportSchedules_ReportDefinitions_ReportDefinitionId", x => x.ReportDefinitionId, "ReportDefinitions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TimeEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                OwnerUserId = table.Column<string>(maxLength: 450, nullable: false),
                WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                DurationMinutes = table.Column<int>(nullable: false),
                ActivityCategoryId = table.Column<Guid>(nullable: false),
                TicketSourceId = table.Column<Guid>(nullable: true),
                TicketReference = table.Column<string>(maxLength: 200, nullable: true),
                Description = table.Column<string>(maxLength: 4000, nullable: true),
                IsAfterHours = table.Column<bool>(nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                ModifiedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                ModifiedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TimeEntries", x => x.Id);
                table.CheckConstraint("CK_TimeEntries_DurationMinutes", "[DurationMinutes] > 0 AND [DurationMinutes] <= 1440");
                table.ForeignKey("FK_TimeEntries_ActivityCategories_ActivityCategoryId", x => x.ActivityCategoryId, "ActivityCategories", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TimeEntries_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TimeEntries_AspNetUsers_ModifiedByUserId", x => x.ModifiedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TimeEntries_AspNetUsers_OwnerUserId", x => x.OwnerUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TimeEntries_TicketSources_TicketSourceId", x => x.TicketSourceId, "TicketSources", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ReportExecutions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ReportScheduleId = table.Column<Guid>(nullable: false),
                IdempotencyKey = table.Column<string>(maxLength: 200, nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                Status = table.Column<string>(maxLength: 40, nullable: false),
                Error = table.Column<string>(maxLength: 4000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportExecutions", x => x.Id);
                table.ForeignKey("FK_ReportExecutions_ReportSchedules_ReportScheduleId", x => x.ReportScheduleId, "ReportSchedules", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TimeEntryDimensionValues",
            columns: table => new
            {
                TimeEntryId = table.Column<Guid>(nullable: false),
                ReportingDimensionValueId = table.Column<Guid>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TimeEntryDimensionValues", x => new { x.TimeEntryId, x.ReportingDimensionValueId });
                table.ForeignKey("FK_TimeEntryDimensionValues_ReportingDimensionValues_ReportingDimensionValueId", x => x.ReportingDimensionValueId, "ReportingDimensionValues", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TimeEntryDimensionValues_TimeEntries_TimeEntryId", x => x.TimeEntryId, "TimeEntries", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TimeEntryTags",
            columns: table => new
            {
                TimeEntryId = table.Column<Guid>(nullable: false),
                TagId = table.Column<Guid>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TimeEntryTags", x => new { x.TimeEntryId, x.TagId });
                table.ForeignKey("FK_TimeEntryTags_Tags_TagId", x => x.TagId, "Tags", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TimeEntryTags_TimeEntries_TimeEntryId", x => x.TimeEntryId, "TimeEntries", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AuditEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                EntityType = table.Column<string>(maxLength: 160, nullable: false),
                EntityId = table.Column<string>(maxLength: 200, nullable: false),
                Action = table.Column<string>(maxLength: 40, nullable: false),
                ActorUserId = table.Column<string>(maxLength: 450, nullable: true),
                OccurredAtUtc = table.Column<DateTimeOffset>(nullable: false),
                BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditEvents", x => x.Id);
                table.ForeignKey("FK_AuditEvents_AspNetUsers_ActorUserId", x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "ReportArtifacts",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ReportExecutionId = table.Column<Guid>(nullable: false),
                FileName = table.Column<string>(maxLength: 260, nullable: false),
                ContentType = table.Column<string>(maxLength: 100, nullable: false),
                StoragePath = table.Column<string>(maxLength: 1000, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportArtifacts", x => x.Id);
                table.ForeignKey("FK_ReportArtifacts_ReportExecutions_ReportExecutionId", x => x.ReportExecutionId, "ReportExecutions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_ActivityCategories_CreatedByUserId", "ActivityCategories", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_ActivityCategories_ModifiedByUserId", "ActivityCategories", "ModifiedByUserId");
        migrationBuilder.CreateIndex("IX_ActivityCategories_Name", "ActivityCategories", "Name", unique: true);
        migrationBuilder.CreateIndex("IX_AspNetRoleClaims_RoleId", "AspNetRoleClaims", "RoleId");
        migrationBuilder.CreateIndex("RoleNameIndex", "AspNetRoles", "NormalizedName", unique: true, filter: "[NormalizedName] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_AspNetUserClaims_UserId", "AspNetUserClaims", "UserId");
        migrationBuilder.CreateIndex("IX_AspNetUserLogins_UserId", "AspNetUserLogins", "UserId");
        migrationBuilder.CreateIndex("IX_AspNetUserRoles_RoleId", "AspNetUserRoles", "RoleId");
        migrationBuilder.CreateIndex("EmailIndex", "AspNetUsers", "NormalizedEmail");
        migrationBuilder.CreateIndex("UserNameIndex", "AspNetUsers", "NormalizedUserName", unique: true, filter: "[NormalizedUserName] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_AuditEvents_ActorUserId", "AuditEvents", "ActorUserId");
        migrationBuilder.CreateIndex("IX_AuditEvents_EntityType_EntityId_OccurredAtUtc", "AuditEvents", new[] { "EntityType", "EntityId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex("IX_ReportingDimensions_CreatedByUserId", "ReportingDimensions", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_ReportingDimensions_Key", "ReportingDimensions", "Key", unique: true);
        migrationBuilder.CreateIndex("IX_ReportingDimensions_ModifiedByUserId", "ReportingDimensions", "ModifiedByUserId");
        migrationBuilder.CreateIndex("IX_ReportingDimensionValues_CreatedByUserId", "ReportingDimensionValues", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_ReportingDimensionValues_ModifiedByUserId", "ReportingDimensionValues", "ModifiedByUserId");
        migrationBuilder.CreateIndex("IX_ReportingDimensionValues_ReportingDimensionId_Name", "ReportingDimensionValues", new[] { "ReportingDimensionId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_ReportArtifacts_ReportExecutionId", "ReportArtifacts", "ReportExecutionId");
        migrationBuilder.CreateIndex("IX_ReportDefinitions_CreatedByUserId", "ReportDefinitions", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_ReportDefinitions_ModifiedByUserId", "ReportDefinitions", "ModifiedByUserId");
        migrationBuilder.CreateIndex("IX_ReportDefinitions_Name", "ReportDefinitions", "Name", unique: true);
        migrationBuilder.CreateIndex("IX_ReportExecutions_ReportScheduleId_IdempotencyKey", "ReportExecutions", new[] { "ReportScheduleId", "IdempotencyKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_ReportSchedules_CreatedByUserId", "ReportSchedules", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_ReportSchedules_ModifiedByUserId", "ReportSchedules", "ModifiedByUserId");
        migrationBuilder.CreateIndex("IX_ReportSchedules_NextRunAtUtc", "ReportSchedules", "NextRunAtUtc", filter: "[IsEnabled] = 1");
        migrationBuilder.CreateIndex("IX_ReportSchedules_ReportDefinitionId", "ReportSchedules", "ReportDefinitionId");
        migrationBuilder.CreateIndex("IX_Tags_CreatedByUserId", "Tags", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_Tags_ModifiedByUserId", "Tags", "ModifiedByUserId");
        migrationBuilder.CreateIndex("IX_Tags_Name", "Tags", "Name", unique: true);
        migrationBuilder.CreateIndex("IX_TicketSources_CreatedByUserId", "TicketSources", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_TicketSources_Key", "TicketSources", "Key", unique: true);
        migrationBuilder.CreateIndex("IX_TicketSources_ModifiedByUserId", "TicketSources", "ModifiedByUserId");
        migrationBuilder.CreateIndex("IX_TimeEntries_ActivityCategoryId", "TimeEntries", "ActivityCategoryId");
        migrationBuilder.CreateIndex("IX_TimeEntries_CreatedByUserId", "TimeEntries", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_TimeEntries_ModifiedByUserId", "TimeEntries", "ModifiedByUserId");
        migrationBuilder.CreateIndex("IX_TimeEntries_OwnerUserId_WorkDate", "TimeEntries", new[] { "OwnerUserId", "WorkDate" });
        migrationBuilder.CreateIndex("IX_TimeEntries_TicketSourceId_TicketReference", "TimeEntries", new[] { "TicketSourceId", "TicketReference" });
        migrationBuilder.CreateIndex("IX_TimeEntryDimensionValues_ReportingDimensionValueId", "TimeEntryDimensionValues", "ReportingDimensionValueId");
        migrationBuilder.CreateIndex("IX_TimeEntryTags_TagId", "TimeEntryTags", "TagId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AuditEvents");
        migrationBuilder.DropTable("AspNetRoleClaims");
        migrationBuilder.DropTable("AspNetUserClaims");
        migrationBuilder.DropTable("AspNetUserLogins");
        migrationBuilder.DropTable("AspNetUserRoles");
        migrationBuilder.DropTable("AspNetUserTokens");
        migrationBuilder.DropTable("ReportArtifacts");
        migrationBuilder.DropTable("TimeEntryDimensionValues");
        migrationBuilder.DropTable("TimeEntryTags");
        migrationBuilder.DropTable("ReportingDimensionValues");
        migrationBuilder.DropTable("ReportExecutions");
        migrationBuilder.DropTable("Tags");
        migrationBuilder.DropTable("TimeEntries");
        migrationBuilder.DropTable("ReportSchedules");
        migrationBuilder.DropTable("ActivityCategories");
        migrationBuilder.DropTable("TicketSources");
        migrationBuilder.DropTable("ReportingDimensions");
        migrationBuilder.DropTable("ReportDefinitions");
        migrationBuilder.DropTable("AspNetRoles");
        migrationBuilder.DropTable("AspNetUsers");
    }
}

[DbContext(typeof(OtTimeDbContext))]
partial class OtTimeDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 128)
            .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

        foreach (var table in new[]
        {
            "ActivityCategories", "ReportingDimensions", "ReportDefinitions", "Tags", "TicketSources"
        })
        {
            modelBuilder.Entity(table, b =>
            {
                b.Property<Guid>("Id").ValueGeneratedNever();
                b.Property<string>("Name").HasMaxLength(120);
                b.Property<int>("DisplayOrder");
                b.Property<bool>("IsEnabled").HasDefaultValue(true);
                b.Property<DateTimeOffset>("CreatedAtUtc");
                b.Property<string>("CreatedByUserId").HasMaxLength(450);
                b.Property<DateTimeOffset?>("ModifiedAtUtc");
                b.Property<string>("ModifiedByUserId").HasMaxLength(450);
                b.Property<byte[]>("RowVersion").IsRowVersion();
                b.HasKey("Id");
                b.ToTable(table);
            });
        }
    }
}