# Upgrade baseline

`schema-09abf95.sql.gz` is the exact `db/schema.sql` Git blob from backend
`09abf9595193cbb721130f5db8dd84d67cd13116`, gzip-compressed with timestamp zero.
Archive SHA-256:
`0a2eceedc1429ebc8ed848c5dddf7fdab03d862463c86f79577897a223152292`.

Keep this fixture immutable. It represents the pre-v5 bootstrap, not a backup
of a real database. It contains no accounts, credentials or student records.
`SchemaParityTests` restores its schema only into an owned, random
`AI_PMS_TEST_<guid>` database, applies the ordered migration manifest and compares
it with the current bootstrap plus the same manifest. Database selectors in
historical scripts never execute against their original catalog.

The test also replays migrations with a contribution snapshot present and verifies
that its contents remain unchanged. Columns, types, defaults, computed columns,
indexes, foreign keys, check constraints and SQL modules are compared. EF mapped
columns must exist. This does not prove business lifecycle acceptance or data
compatibility for every possible historical production dataset.
