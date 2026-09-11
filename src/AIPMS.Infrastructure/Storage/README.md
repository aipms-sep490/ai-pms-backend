# Private file storage

LocalFileStorage implements Application's IFileStorage. Database `storage_path`
and `stored_file_name` contain an opaque server-generated 32-hex object key, never
a path from the client. Upload names are metadata only. Keys are checked before
every filesystem operation and files are created with CreateNew (no overwrite).
Content is streamed to disk; failed/interrupted writes remove their partial file.
Providers must preserve existing objects when creation fails, including collisions.

Configure an absolute `FileStorage:RootPath` outside the repository, webroot and
any statically served directory. Environment variable: `FileStorage__RootPath`.
Default: the service account's LocalApplicationData/AIPMS/private-files directory.
Grant only the API service account filesystem access. Production deployments must
mount a persistent private volume and back it up together with database metadata;
container-local ephemeral storage is unsuitable. Multiple API instances must share
that persistent volume or replace IFileStorage with a private object-store provider.
Root configuration is operational, not exposed through the API.

UploadValidator allows pdf, txt (strict UTF-8), png, jpg/jpeg, zip, docx, xlsx and
pptx, up to 20 MiB per file. It checks filename, extension/MIME match, actual stream
length, format signatures, and calculates SHA-256. ZIP/Office archives allow up to
10,000 entries and 200 MiB declared expanded content. Office formats require their
expected document parts and reject vbaProject.bin. These are format checks, not
malware scanning or a full document parser. Uploaded archives are never extracted
or executed. The HTTP multipart envelope is bounded at 22 MiB.

All downloads pass through the authorized application workflow. Content is sent as
an attachment with no-store and nosniff headers; no public URLs or raw storage paths
are returned. Missing content returns 404. Open streams are disposed by MVC after
the response; uploads are disposed by their controller/workflow.

The database and filesystem do not share a distributed transaction. The workflow
retains objects on uncertain commit outcomes and cleans successful uploads only
after a confirmed pre-commit rollback. Metadata deletions commit before physical
cleanup. Failed cleanup is logged and may leave an inaccessible orphan; there is
no durable retry worker in this package. Reconciliation must compare keys against
committed file metadata after in-flight uploads have ended (use a maintenance
window), retain every referenced object, investigate uncertain commit logs and
back up before deleting confirmed orphans. Never sweep recent files merely because
their database reference has not appeared yet. See the Deliverables feature README
for the transaction and retry contract.
