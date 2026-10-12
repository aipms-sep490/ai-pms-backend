SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.chat_member_state','muted_until') IS NULL
 ALTER TABLE dbo.chat_member_state ADD muted_until datetime2(7) NULL;
IF COL_LENGTH('dbo.chat_outbox','message_id') IS NULL
 ALTER TABLE dbo.chat_outbox ADD message_id bigint NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='fk_chat_outbox_message')
 EXEC(N'ALTER TABLE dbo.chat_outbox ADD CONSTRAINT fk_chat_outbox_message FOREIGN KEY(message_id) REFERENCES dbo.chat_messages(id)');
IF OBJECT_ID('dbo.chat_uploads','U') IS NULL
BEGIN
 CREATE TABLE dbo.chat_uploads(
  id bigint IDENTITY PRIMARY KEY, conversation_id bigint NOT NULL, uploaded_by bigint NOT NULL,
  storage_key varchar(32) NOT NULL, file_name nvarchar(255) NOT NULL, content_type varchar(128) NOT NULL,
  size_bytes bigint NOT NULL, sha256 varchar(64) NOT NULL, status varchar(16) NOT NULL,
  created_at datetime2(7) NOT NULL, expires_at datetime2(7) NOT NULL,
  CONSTRAINT fk_chat_upload_conversation FOREIGN KEY(conversation_id) REFERENCES dbo.chat_conversations(id),
  CONSTRAINT fk_chat_upload_actor FOREIGN KEY(uploaded_by) REFERENCES dbo.users(id),
  CONSTRAINT ck_chat_upload_status CHECK(status IN ('UPLOADING','READY','ATTACHED','DELETING','DELETED')),
  CONSTRAINT ck_chat_upload_size CHECK(size_bytes BETWEEN 1 AND 10485760));
 CREATE UNIQUE INDEX ux_chat_upload_key ON dbo.chat_uploads(storage_key);
 CREATE INDEX ix_chat_upload_cleanup ON dbo.chat_uploads(status,expires_at,id);
 CREATE INDEX ix_chat_upload_owner ON dbo.chat_uploads(conversation_id,uploaded_by,status);
END;
IF OBJECT_ID('dbo.chat_message_attachments','U') IS NULL
BEGIN
 CREATE TABLE dbo.chat_message_attachments(message_id bigint NOT NULL,upload_id bigint NOT NULL,
  CONSTRAINT pk_chat_message_attachment PRIMARY KEY(message_id,upload_id),
  CONSTRAINT fk_chat_attachment_message FOREIGN KEY(message_id) REFERENCES dbo.chat_messages(id),
  CONSTRAINT fk_chat_attachment_upload FOREIGN KEY(upload_id) REFERENCES dbo.chat_uploads(id));
 CREATE UNIQUE INDEX ux_chat_attachment_upload ON dbo.chat_message_attachments(upload_id);
END;
IF OBJECT_ID('dbo.chat_reactions','U') IS NULL
BEGIN
 CREATE TABLE dbo.chat_reactions(message_id bigint NOT NULL,user_id bigint NOT NULL,emoji nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  created_at datetime2(7) NOT NULL,
  CONSTRAINT pk_chat_reaction PRIMARY KEY(message_id,user_id,emoji),
  CONSTRAINT fk_chat_reaction_message FOREIGN KEY(message_id) REFERENCES dbo.chat_messages(id),
  CONSTRAINT fk_chat_reaction_actor FOREIGN KEY(user_id) REFERENCES dbo.users(id));
END;
-- Empty body permits attachment-only messages; the service requires at least one linked upload.
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='ck_chat_message_body')
 ALTER TABLE dbo.chat_messages DROP CONSTRAINT ck_chat_message_body;
ALTER TABLE dbo.chat_messages WITH CHECK ADD CONSTRAINT ck_chat_message_body
 CHECK((recalled_at IS NULL AND body IS NOT NULL) OR (recalled_at IS NOT NULL AND body IS NULL));
COMMIT;
GO
