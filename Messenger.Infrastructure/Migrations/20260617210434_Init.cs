using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "Account_Settings",
                columns: table => new
                {
                    Setting_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Account_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Avatar = table.Column<string>(type: "text", nullable: true),
                    Push_Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Notify_Messages = table.Column<bool>(type: "boolean", nullable: false),
                    Notify_GroupChats = table.Column<bool>(type: "boolean", nullable: false),
                    Notify_Mentions = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Account_Settings_pkey", x => x.Setting_ID);
                    table.UniqueConstraint("AK_Account_Settings_Account_ID", x => x.Account_ID);
                });

            migrationBuilder.CreateTable(
                name: "InboxState",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceiveCount = table.Column<int>(type: "integer", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Role_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Roles_pkey", x => x.Role_ID);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Account_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Last_Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    First_Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Registration_Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Login = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    External_ID = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Users_pkey", x => x.User_ID);
                    table.ForeignKey(
                        name: "fk_account",
                        column: x => x.Account_ID,
                        principalTable: "Account_Settings",
                        principalColumn: "Account_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnqueueTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateTable(
                name: "Blacklist",
                columns: table => new
                {
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Blocked_User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Block_Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("Blacklist_pkey", x => new { x.User_ID, x.Blocked_User_ID });
                    table.ForeignKey(
                        name: "fk_blocked_by",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_blocked_to",
                        column: x => x.Blocked_User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Broadcasts",
                columns: table => new
                {
                    Broadcast_ID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message_Text = table.Column<string>(type: "text", nullable: false),
                    Sender_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Created_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Total_Recipients = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Broadcasts_pkey", x => x.Broadcast_ID);
                    table.ForeignKey(
                        name: "fk_broadcast_sender",
                        column: x => x.Sender_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Chats",
                columns: table => new
                {
                    Chat_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Creation_Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("Chats_pkey", x => x.Chat_ID);
                    table.ForeignKey(
                        name: "fk_chat_creator",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Logins",
                columns: table => new
                {
                    Login_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    IP_Address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Login_Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Logout_Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Logins_pkey", x => x.Login_ID);
                    table.ForeignKey(
                        name: "fk_login_user",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Notification_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Creation_Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    Read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Read_At = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Notifications_pkey", x => x.Notification_ID);
                    table.ForeignKey(
                        name: "fk_notification_user",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    P256dh = table.Column<string>(type: "text", nullable: false),
                    Auth = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PushSubscriptions_pkey", x => x.Id);
                    table.ForeignKey(
                        name: "fk_push_subscription_user",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "User_Roles",
                columns: table => new
                {
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Role_ID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("UserRoles_pkey", x => new { x.User_ID, x.Role_ID });
                    table.ForeignKey(
                        name: "fk_userrole_role",
                        column: x => x.Role_ID,
                        principalTable: "Roles",
                        principalColumn: "Role_ID");
                    table.ForeignKey(
                        name: "fk_userrole_user",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID");
                });

            migrationBuilder.CreateTable(
                name: "User_Statuses",
                columns: table => new
                {
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Online = table.Column<bool>(type: "boolean", nullable: false),
                    Last_Activity = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("User_Statuses_pkey", x => x.User_ID);
                    table.ForeignKey(
                        name: "fk_status_user",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Broadcast_Recipients",
                columns: table => new
                {
                    Broadcast_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Sent_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Delivered_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Read_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Is_Read = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Broadcast_Recipients_pkey", x => new { x.Broadcast_ID, x.User_ID });
                    table.ForeignKey(
                        name: "fk_recipient_broadcast",
                        column: x => x.Broadcast_ID,
                        principalTable: "Broadcasts",
                        principalColumn: "Broadcast_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recipient_user",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Chat_Participants",
                columns: table => new
                {
                    Chat_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'participant'::character varying"),
                    Join_Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("Chat_Participants_pkey", x => new { x.Chat_ID, x.User_ID });
                    table.ForeignKey(
                        name: "fk_participant_chat",
                        column: x => x.Chat_ID,
                        principalTable: "Chats",
                        principalColumn: "Chat_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_participant_user",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Message_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Sender_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Chat_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Message_Text = table.Column<string>(type: "text", nullable: false),
                    Has_Attachments = table.Column<bool>(type: "boolean", nullable: false),
                    Send_Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Sequence_Number = table.Column<long>(type: "bigint", nullable: false),
                    Delivery_Status = table.Column<int>(type: "integer", nullable: false),
                    Read_Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Messages_pkey", x => x.Message_ID);
                    table.ForeignKey(
                        name: "fk_chat_messages",
                        column: x => x.Chat_ID,
                        principalTable: "Chats",
                        principalColumn: "Chat_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sender",
                        column: x => x.Sender_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Attachment_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Message_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    File_Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    File_Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Size_in_Bytes = table.Column<int>(type: "integer", nullable: true),
                    URL = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Attachments_pkey", x => x.Attachment_ID);
                    table.ForeignKey(
                        name: "fk_attachment_message",
                        column: x => x.Message_ID,
                        principalTable: "Messages",
                        principalColumn: "Message_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reactions",
                columns: table => new
                {
                    Reaction_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Message_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Reaction_Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Reactions_pkey", x => x.Reaction_ID);
                    table.ForeignKey(
                        name: "fk_reaction_message",
                        column: x => x.Message_ID,
                        principalTable: "Messages",
                        principalColumn: "Message_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reaction_user",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "Account_Settings_Account_ID_key",
                table: "Account_Settings",
                column: "Account_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Message_ID",
                table: "Attachments",
                column: "Message_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Blacklist_Blocked_User_ID",
                table: "Blacklist",
                column: "Blocked_User_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Broadcast_Recipients_User_ID",
                table: "Broadcast_Recipients",
                column: "User_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Broadcasts_Sender_ID",
                table: "Broadcasts",
                column: "Sender_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Chat_Participants_User_ID",
                table: "Chat_Participants",
                column: "User_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Chats_User_ID",
                table: "Chats",
                column: "User_ID");

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_Logins_User_ID",
                table: "Logins",
                column: "User_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatId_ReadTime",
                table: "Messages",
                columns: new[] { "Chat_ID", "Read_Time" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatId_SendTime",
                table: "Messages",
                columns: new[] { "Chat_ID", "Send_Time" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatId_SequenceNumber",
                table: "Messages",
                columns: new[] { "Chat_ID", "Sequence_Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Sender_ID",
                table: "Messages",
                column: "Sender_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_User_ID",
                table: "Notifications",
                column: "User_ID");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                table: "OutboxState",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId",
                table: "PushSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_MessageId_UserId",
                table: "Reactions",
                columns: new[] { "Message_ID", "User_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_User_ID",
                table: "Reactions",
                column: "User_ID");

            migrationBuilder.CreateIndex(
                name: "IX_User_Roles_Role_ID",
                table: "User_Roles",
                column: "Role_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Account_ID",
                table: "Users",
                column: "Account_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Blacklist");

            migrationBuilder.DropTable(
                name: "Broadcast_Recipients");

            migrationBuilder.DropTable(
                name: "Chat_Participants");

            migrationBuilder.DropTable(
                name: "Logins");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "Reactions");

            migrationBuilder.DropTable(
                name: "User_Roles");

            migrationBuilder.DropTable(
                name: "User_Statuses");

            migrationBuilder.DropTable(
                name: "Broadcasts");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Chats");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Account_Settings");
        }
    }
}
